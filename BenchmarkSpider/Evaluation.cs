using System.Globalization;
using System.Text;

namespace BenchmarkSpider;

// C# port of spider/evaluation.py (exact / partial matching and execution accuracy).

public record PartialScore(double Acc, double Rec, double F1, int LabelTotal, int PredTotal);

public class PartialAggregate
{
    public double Acc { get; set; }
    public double Rec { get; set; }
    public double F1 { get; set; }
    public int AccCount { get; set; }
    public int RecCount { get; set; }
}

public class LevelScores
{
    public int Count { get; set; }
    public double Exact { get; set; }
    public double Exec { get; set; }
    public Dictionary<string, PartialAggregate> Partial { get; } =
        SpiderEvaluator.PartialTypes.ToDictionary(t => t, _ => new PartialAggregate());
}

public record SpiderEvalEntry(string PredictSql, string GoldSql, string Hardness, bool Exact, IReadOnlyDictionary<string, PartialScore> Partial);

/// <summary>
/// Represents the aggregated outcome of a Spider evaluation run, including per-level scores, collected entries, error
/// count, and execution-evaluation state.
/// </summary>
/// <remarks>Score containers are initialized for all evaluator levels.</remarks>
public class SpiderEvalResult
{
    public Dictionary<string, LevelScores> Scores { get; } = SpiderEvaluator.Levels.ToDictionary(l => l, _ => new LevelScores());
    public List<SpiderEvalEntry> Entries { get; } = [];
    /// <summary>
    /// Nombre d’erreurs d’évaluation.
    /// </summary>
    public int EvalErrorCount { get; set; }
    /// <summary>
    /// Indique si l’exécution de la requete SQL a été évaluée.
    /// </summary>
    public bool ExecEvaluated { get; set; }
}

/// <summary>
/// Évalue des requêtes SQL de type Spider en comparant des prédictions à des requêtes de référence, avec calcul des
/// scores de correspondance exacte, de correspondance partielle et, optionnellement, d’exécution.
/// </summary>
/// <remarks>Fournit aussi des utilitaires de normalisation des requêtes (suppression des valeurs littérales,
/// harmonisation des colonnes via clés étrangères), de classification de difficulté et de mise en forme des résultats
/// agrégés par niveau.</remarks>
public partial class SpiderEvaluator
{
    /// <summary>
    /// Niveaux de difficulté de la requête SQL.
    /// </summary>
    public static readonly string[] Levels = ["easy", "medium", "hard", "extra", "all"];

    /// <summary>
    /// Catégories de requêtes SQL partielles utilisées pour la classification.
    /// </summary>
    public static readonly string[] PartialTypes =
        ["select", "select(no AGG)", "where", "where(no OP)", "group(no Having)", "group", "order", "and/or", "IUEN", "keywords"];

    /// <summary>
    /// Index de l’opérateur SQL <c>LIKE</c> dans <c>SqlParser.WhereOps</c>.
    /// </summary>
    private static readonly int LikeOp = Array.IndexOf(SqlParser.WhereOps, "like");
    /// <summary>
    /// Index de l’opérateur SQL <c>IN</c> dans <c>SqlParser.WhereOps</c>.
    /// </summary>
    private static readonly int InOp = Array.IndexOf(SqlParser.WhereOps, "in");

    public IReadOnlyDictionary<string, PartialScore>? PartialScores { get; private set; }

    // ---------------------------------------------------------------- canonical forms (structural equality)

    private static string Inv(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static string Canon(ColUnit? c) => c is null ? "N" : $"({c.AggId},{c.ColId},{c.IsDistinct})";

    private static string Canon(ValUnit? v) => v is null ? "N" : $"({v.UnitOp},{Canon(v.ColUnit1)},{Canon(v.ColUnit2)})";

    private static string Canon(SelectItem s) => $"({s.AggId},{Canon(s.ValUnit)})";

    private static string Canon(SqlValue? v) => v switch
    {
        null => "N",
        NumberValue n => "n" + Inv(n.Value),
        StringValue s => "s'" + s.Value + "'",
        SubQueryValue q => Canon(q.Sql),
        ColumnValue c => "c" + Canon(c.ColUnit),
        _ => throw new InvalidOperationException()
    };

    private static string Canon(ConditionItem item) => item switch
    {
        CondUnit c => $"({c.NotOp},{c.OpId},{Canon(c.ValUnit)},{Canon(c.Val1)},{Canon(c.Val2)})",
        ConditionConnector k => k.Operator,
        _ => throw new InvalidOperationException()
    };

    private static string Canon(IEnumerable<ConditionItem> items) => "[" + string.Join("|", items.Select(Canon)) + "]";

    private static string Canon(TableUnit t) => t.TableType == SqlParser.TableTypeSql ? "sql:" + Canon(t.SubQuery) : "table_unit:" + t.TableId;

    private static string Canon(OrderByClause? o) => o is null ? "[]" : $"({o.OrderType},[{string.Join(",", o.ValUnits.Select(v => Canon(v)))}])";

    private static string Canon(ParsedSql? s)
    {
        if (s is null)
            return "N";
        return string.Join(";",
            $"sel({s.Select.IsDistinct},[{string.Join(",", s.Select.Items.Select(Canon))}])",
            $"from([{string.Join(",", s.From.TableUnits.Select(Canon))}],{Canon(s.From.Conds)})",
            "where" + Canon(s.Where),
            $"group[{string.Join(",", s.GroupBy.Select(c => Canon(c)))}]",
            "order" + Canon(s.OrderBy),
            "having" + Canon(s.Having),
            "limit" + (s.Limit?.ToString(CultureInfo.InvariantCulture) ?? "N"),
            "intersect" + Canon(s.Intersect),
            "except" + Canon(s.Except),
            "union" + Canon(s.Union));
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<CondUnit> Units(IReadOnlyList<ConditionItem> c) => c.OfType<CondUnit>();

    private static IEnumerable<string> Connectors(IReadOnlyList<ConditionItem> c) => c.OfType<ConditionConnector>().Select(k => k.Operator);

    private static IEnumerable<CondUnit> AllCondUnits(ParsedSql sql) => Units(sql.From.Conds).Concat(Units(sql.Where)).Concat(Units(sql.Having));

    private static IEnumerable<string> AllConnectors(ParsedSql sql) => Connectors(sql.From.Conds).Concat(Connectors(sql.Where)).Concat(Connectors(sql.Having));

    private static (int, int, int) GetScores(int count, int predTotal, int labelTotal)
    {
        if (predTotal != labelTotal)
            return (0, 0, 0);
        return count == predTotal ? (1, 1, 1) : (0, 0, 0);
    }

    private static int RemoveFirstIfPresent(List<string> list, string item)
    {
        var idx = list.IndexOf(item);
        if (idx < 0)
            return 0;
        list.RemoveAt(idx);
        return 1;
    }

    // ---------------------------------------------------------------- partial evaluations

    private static (int LabelTotal, int PredTotal, int Cnt, int CntWoAgg) EvalSel(ParsedSql pred, ParsedSql label)
    {
        var labelSel = label.Select.Items.Select(Canon).ToList();
        var labelWoAgg = label.Select.Items.Select(i => Canon(i.ValUnit)).ToList();
        int cnt = 0, cntWoAgg = 0;
        foreach (var unit in pred.Select.Items)
        {
            cnt += RemoveFirstIfPresent(labelSel, Canon(unit));
            cntWoAgg += RemoveFirstIfPresent(labelWoAgg, Canon(unit.ValUnit));
        }
        return (label.Select.Items.Count, pred.Select.Items.Count, cnt, cntWoAgg);
    }

    private static (int, int, int, int) EvalWhere(ParsedSql pred, ParsedSql label)
    {
        var predConds = Units(pred.Where).ToList();
        var labelConds = Units(label.Where).Select(c => Canon(c)).ToList();
        var labelWoAgg = Units(label.Where).Select(c => Canon(c.ValUnit)).ToList();
        int cnt = 0, cntWoAgg = 0;
        foreach (var unit in predConds)
        {
            cnt += RemoveFirstIfPresent(labelConds, Canon(unit));
            cntWoAgg += RemoveFirstIfPresent(labelWoAgg, Canon(unit.ValUnit));
        }
        return (Units(label.Where).Count(), predConds.Count, cnt, cntWoAgg);
    }

    private static string AfterDot(string s) => s.Contains('.') ? s.Split('.')[1] : s;

    private static (int, int, int) EvalGroup(ParsedSql pred, ParsedSql label)
    {
        var predCols = pred.GroupBy.Select(u => AfterDot(u.ColId)).ToList();
        var labelCols = label.GroupBy.Select(u => AfterDot(u.ColId)).ToList();
        int cnt = 0;
        foreach (var col in predCols)
            cnt += RemoveFirstIfPresent(labelCols, col);
        return (label.GroupBy.Count, pred.GroupBy.Count, cnt);
    }

    private static (int, int, int) EvalHaving(ParsedSql pred, ParsedSql label)
    {
        var predTotal = pred.GroupBy.Count > 0 ? 1 : 0;
        var labelTotal = label.GroupBy.Count > 0 ? 1 : 0;
        var cnt = 0;
        if (predTotal == 1 && labelTotal == 1
            && pred.GroupBy.Select(u => u.ColId).SequenceEqual(label.GroupBy.Select(u => u.ColId))
            && Canon(pred.Having) == Canon(label.Having))
            cnt = 1;
        return (labelTotal, predTotal, cnt);
    }

    private static (int, int, int) EvalOrder(ParsedSql pred, ParsedSql label)
    {
        var predTotal = pred.OrderBy is not null ? 1 : 0;
        var labelTotal = label.OrderBy is not null ? 1 : 0;
        var cnt = 0;
        if (label.OrderBy is not null && Canon(pred.OrderBy) == Canon(label.OrderBy)
            && (pred.Limit is null) == (label.Limit is null))
            cnt = 1;
        return (labelTotal, predTotal, cnt);
    }

    private static (int, int, int) EvalAndOr(ParsedSql pred, ParsedSql label)
    {
        var predAo = Connectors(pred.Where).ToHashSet();
        var labelAo = Connectors(label.Where).ToHashSet();
        if (predAo.SetEquals(labelAo))
            return (1, 1, 1);
        return (predAo.Count, labelAo.Count, 0);
    }

    private static List<ParsedSql> GetNestedSql(ParsedSql sql)
    {
        var nested = new List<ParsedSql>();
        foreach (var c in AllCondUnits(sql))
        {
            if (c.Val1 is SubQueryValue q1) nested.Add(q1.Sql);
            if (c.Val2 is SubQueryValue q2) nested.Add(q2.Sql);
        }
        if (sql.Intersect is not null) nested.Add(sql.Intersect);
        if (sql.Except is not null) nested.Add(sql.Except);
        if (sql.Union is not null) nested.Add(sql.Union);
        return nested;
    }

    private static (int, int, int) EvalNested(ParsedSql? pred, ParsedSql? label)
    {
        var cnt = 0;
        if (pred is not null && label is not null && new SpiderEvaluator().EvalExactMatch(pred, label))
            cnt = 1;
        return (label is not null ? 1 : 0, pred is not null ? 1 : 0, cnt);
    }

    private static (int, int, int) EvalIuen(ParsedSql pred, ParsedSql label)
    {
        var (lt1, pt1, c1) = EvalNested(pred.Intersect, label.Intersect);
        var (lt2, pt2, c2) = EvalNested(pred.Except, label.Except);
        var (lt3, pt3, c3) = EvalNested(pred.Union, label.Union);
        return (lt1 + lt2 + lt3, pt1 + pt2 + pt3, c1 + c2 + c3);
    }

    /// <summary>
    /// Extrait les mots-clés SQL présents dans une requête analysée 
    /// à partir de ses clauses, connecteurs et opérateurs conditionnels.
    /// </summary>
    /// <param name="sql">Requête SQL analysée à inspecter.</param>
    /// <returns>Ensemble des mots-clés détectés, sans doublons.</returns>
    private static HashSet<string> GetKeywords(ParsedSql sql)
    {
        var res = new HashSet<string>();
        if (sql.Where.Count > 0) res.Add("where");
        if (sql.GroupBy.Count > 0) res.Add("group");
        if (sql.Having.Count > 0) res.Add("having");
        if (sql.OrderBy is not null)
        {
            res.Add(sql.OrderBy.OrderType);
            res.Add("order");
        }
        if (sql.Limit is not null) res.Add("limit");
        if (sql.Except is not null) res.Add("except");
        if (sql.Union is not null) res.Add("union");
        if (sql.Intersect is not null) res.Add("intersect");

        if (AllConnectors(sql).Any(t => t == "or")) res.Add("or");

        var units = AllCondUnits(sql).ToList();
        if (units.Any(c => c.NotOp)) res.Add("not");
        if (units.Any(c => c.OpId == InOp)) res.Add("in");
        if (units.Any(c => c.OpId == LikeOp)) res.Add("like");
        return res;
    }

    /// <summary>
    /// Évalue les mots-clés de deux requêtes SQL analysées et calcule leurs effectifs ainsi que leur recouvrement.
    /// </summary>
    /// <param name="pred">Requête SQL analysée prédite.</param>
    /// <param name="label">Requête SQL analysée de référence.</param>
    /// <returns>Un tuple contenant, dans l’ordre, le nombre de mots-clés de <paramref name="label"/>, le nombre de mots-clés de
    /// <paramref name="pred"/>, puis le nombre de mots-clés de <paramref name="pred"/> présents dans <paramref name="label"/>.</returns>
    private static (int, int, int) EvalKeywords(ParsedSql pred, ParsedSql label)
    {
        var p = GetKeywords(pred);
        var l = GetKeywords(label);
        return (l.Count, p.Count, p.Count(l.Contains));
    }

    // ---------------------------------------------------------------- hardness

    private static int CountComponent1(ParsedSql sql)
    {
        var count = 0;
        if (sql.Where.Count > 0) count++;
        if (sql.GroupBy.Count > 0) count++;
        if (sql.OrderBy is not null) count++;
        if (sql.Limit is not null) count++;
        if (sql.From.TableUnits.Count > 0) count += sql.From.TableUnits.Count - 1;
        count += AllConnectors(sql).Count(t => t == "or");
        count += AllCondUnits(sql).Count(c => c.OpId == LikeOp);
        return count;
    }

    private static int CountComponent2(ParsedSql sql) => GetNestedSql(sql).Count;

    private static int CountOthers(ParsedSql sql)
    {
        var count = 0;

        // Same quirks as the Python version: has_agg(cond_unit) looks at not_op, has_agg('and'/'or') is always true.
        var aggCount = sql.Select.Items.Count(i => i.AggId != 0);
        aggCount += Units(sql.Where).Count(c => c.NotOp);
        aggCount += sql.GroupBy.Count(c => c.AggId != 0);
        if (sql.OrderBy is not null)
        {
            aggCount += sql.OrderBy.ValUnits.Count(v => v.ColUnit1.AggId != 0);
            aggCount += sql.OrderBy.ValUnits.Count(v => v.ColUnit2 is not null && v.ColUnit2.AggId != 0);
        }
        aggCount += sql.Having.Count(i => i is ConditionConnector || (i is CondUnit c && c.NotOp));
        if (aggCount > 1) count++;

        if (sql.Select.Items.Count > 1) count++;
        if (sql.Where.Count > 1) count++;
        if (sql.GroupBy.Count > 1) count++;
        return count;
    }

    public string EvalHardness(ParsedSql sql)
    {
        var c1 = CountComponent1(sql);
        var c2 = CountComponent2(sql);
        var others = CountOthers(sql);

        if (c1 <= 1 && others == 0 && c2 == 0)
            return "easy";
        if ((others <= 2 && c1 <= 1 && c2 == 0) || (c1 <= 2 && others < 2 && c2 == 0))
            return "medium";
        if ((others > 2 && c1 <= 2 && c2 == 0) || (c1 is > 2 and <= 3 && others <= 2 && c2 == 0) || (c1 <= 1 && others == 0 && c2 <= 1))
            return "hard";
        return "extra";
    }

    // ---------------------------------------------------------------- matching

    public bool EvalExactMatch(ParsedSql pred, ParsedSql label)
    {
        var partial = EvalPartialMatch(pred, label);
        PartialScores = partial;

        if (partial.Values.Any(s => s.F1 != 1))
            return false;

        if (label.From.TableUnits.Count > 0)
        {
            var labelTables = label.From.TableUnits.Select(Canon).OrderBy(s => s, StringComparer.Ordinal);
            var predTables = pred.From.TableUnits.Select(Canon).OrderBy(s => s, StringComparer.Ordinal);
            return labelTables.SequenceEqual(predTables);
        }
        return true;
    }

    private static PartialScore Score(int cnt, int predTotal, int labelTotal)
    {
        var (acc, rec, f1) = GetScores(cnt, predTotal, labelTotal);
        return new PartialScore(acc, rec, f1, labelTotal, predTotal);
    }

    public Dictionary<string, PartialScore> EvalPartialMatch(ParsedSql pred, ParsedSql label)
    {
        var res = new Dictionary<string, PartialScore>();

        var (lt, pt, cnt, cntWo) = EvalSel(pred, label);
        res["select"] = Score(cnt, pt, lt);
        res["select(no AGG)"] = Score(cntWo, pt, lt);

        (lt, pt, cnt, cntWo) = EvalWhere(pred, label);
        res["where"] = Score(cnt, pt, lt);
        res["where(no OP)"] = Score(cntWo, pt, lt);

        (lt, pt, cnt) = EvalGroup(pred, label);
        res["group(no Having)"] = Score(cnt, pt, lt);

        (lt, pt, cnt) = EvalHaving(pred, label);
        res["group"] = Score(cnt, pt, lt);

        (lt, pt, cnt) = EvalOrder(pred, label);
        res["order"] = Score(cnt, pt, lt);

        (lt, pt, cnt) = EvalAndOr(pred, label);
        res["and/or"] = Score(cnt, pt, lt);

        (lt, pt, cnt) = EvalIuen(pred, label);
        res["IUEN"] = Score(cnt, pt, lt);

        (lt, pt, cnt) = EvalKeywords(pred, label);
        res["keywords"] = Score(cnt, pt, lt);

        return res;
    }

    // ---------------------------------------------------------------- rebuild (value removal / foreign key normalisation)

    private static ParsedSql Make(SelectClause select, FromClause from, IReadOnlyList<ConditionItem> where, IReadOnlyList<ColUnit> groupBy,
        IReadOnlyList<ConditionItem> having, OrderByClause? orderBy, int? limit, ParsedSql? intersect, ParsedSql? union, ParsedSql? except)
        => new()
        {
            Select = select,
            From = from,
            Where = where,
            GroupBy = groupBy,
            Having = having,
            OrderBy = orderBy,
            Limit = limit,
            Intersect = intersect,
            Union = union,
            Except = except
        };

    private static List<ConditionItem> RebuildConditionVal(IReadOnlyList<ConditionItem> condition)
        => condition.Select(item => item is CondUnit c
            ? (ConditionItem)new CondUnit(c.NotOp, c.OpId, c.ValUnit,
                c.Val1 is SubQueryValue q1 ? new SubQueryValue(RebuildSqlVal(q1.Sql)!) : null,
                c.Val2 is SubQueryValue q2 ? new SubQueryValue(RebuildSqlVal(q2.Sql)!) : null)
            : item).ToList();

    /// <summary>DISABLE_VALUE: drops every literal value, keeping only sub queries.</summary>
    public static ParsedSql? RebuildSqlVal(ParsedSql? sql)
    {
        if (sql is null)
            return null;
        return Make(sql.Select,
            new FromClause(sql.From.TableUnits, RebuildConditionVal(sql.From.Conds)),
            RebuildConditionVal(sql.Where), sql.GroupBy, RebuildConditionVal(sql.Having), sql.OrderBy, sql.Limit,
            RebuildSqlVal(sql.Intersect), RebuildSqlVal(sql.Union), RebuildSqlVal(sql.Except));
    }

    public static List<string> BuildValidColUnits(IEnumerable<TableUnit> tableUnits, SqlSchema schema)
    {
        var prefixes = tableUnits
            .Where(t => t.TableType == SqlParser.TableTypeTableUnit && t.TableId is not null)
            .Select(t => t.TableId![..^2])
            .ToHashSet();
        var valid = new List<string>();
        foreach (var value in schema.IdMap.Values)
        {
            var dot = value.IndexOf('.');
            if (dot >= 0 && prefixes.Contains(value[..dot]))
                valid.Add(value);
        }
        return valid;
    }

    // DISABLE_DISTINCT: distinct flags are always cleared.
    private static ColUnit? RebuildColUnit(ICollection<string> valid, ColUnit? c, IReadOnlyDictionary<string, string> kmap)
    {
        if (c is null)
            return null;
        var colId = kmap.TryGetValue(c.ColId, out var mapped) && valid.Contains(c.ColId) ? mapped : c.ColId;
        return new ColUnit(c.AggId, colId, false);
    }

    private static ValUnit RebuildValUnit(ICollection<string> valid, ValUnit v, IReadOnlyDictionary<string, string> kmap)
        => new(v.UnitOp, RebuildColUnit(valid, v.ColUnit1, kmap)!, RebuildColUnit(valid, v.ColUnit2, kmap));

    private static List<ConditionItem> RebuildConditionCol(ICollection<string> valid, IReadOnlyList<ConditionItem> condition, IReadOnlyDictionary<string, string> kmap)
        => condition.Select(item => item is CondUnit c
            ? (ConditionItem)new CondUnit(c.NotOp, c.OpId, RebuildValUnit(valid, c.ValUnit, kmap), c.Val1, c.Val2)
            : item).ToList();

    public static ParsedSql? RebuildSqlCol(ICollection<string> valid, ParsedSql? sql, IReadOnlyDictionary<string, string> kmap)
    {
        if (sql is null)
            return null;

        var select = new SelectClause(false, sql.Select.Items.Select(i => new SelectItem(i.AggId, RebuildValUnit(valid, i.ValUnit, kmap))).ToList());
        var from = new FromClause(
            sql.From.TableUnits.Select(t => t.TableType == SqlParser.TableTypeTableUnit ? t : t).ToList(),
            RebuildConditionCol(valid, sql.From.Conds, kmap));
        var orderBy = sql.OrderBy is null
            ? null
            : new OrderByClause(sql.OrderBy.OrderType, sql.OrderBy.ValUnits.Select(v => RebuildValUnit(valid, v, kmap)).ToList());

        return Make(select, from,
            RebuildConditionCol(valid, sql.Where, kmap),
            sql.GroupBy.Select(c => RebuildColUnit(valid, c, kmap)!).ToList(),
            RebuildConditionCol(valid, sql.Having, kmap),
            orderBy, sql.Limit,
            RebuildSqlCol(valid, sql.Intersect, kmap), RebuildSqlCol(valid, sql.Union, kmap), RebuildSqlCol(valid, sql.Except, kmap));
    }

    public static Dictionary<string, string> BuildForeignKeyMap(SpiderDatabase db)
    {
        var cols = db.ColumnNamesOriginal
            .Select(c => c.TableIndex >= 0
                ? "__" + db.TableNamesOriginal[c.TableIndex].ToLowerInvariant() + "." + c.Name.ToLowerInvariant() + "__"
                : "__all__")
            .ToList();

        var keySets = new List<SortedSet<int>>();
        foreach (var fk in db.ForeignKeys)
        {
            var set = keySets.FirstOrDefault(s => s.Contains(fk.ColumnIndex) || s.Contains(fk.ReferencedColumnIndex));
            if (set is null)
            {
                set = [];
                keySets.Add(set);
            }
            set.Add(fk.ColumnIndex);
            set.Add(fk.ReferencedColumnIndex);
        }

        var map = new Dictionary<string, string>();
        foreach (var set in keySets)
        {
            var min = set.Min;
            foreach (var idx in set)
                map[cols[idx]] = cols[min];
        }
        return map;
    }

    public static Dictionary<string, Dictionary<string, string>> BuildForeignKeyMaps(IEnumerable<SpiderDatabase> dbs)
        => dbs.ToDictionary(d => d.DbId, BuildForeignKeyMap);

    // ---------------------------------------------------------------- evaluation driver

    private static ParsedSql EmptySql() => Make(new SelectClause(false, []), new FromClause([], []), [], [], [], null, null, null, null, null);

    /// <summary>
    /// Évalue un ensemble de requêtes SQL prédites par rapport aux requêtes de référence et calcule des métriques
    /// agrégées par niveau de difficulté et globalement.
    /// </summary>
    /// <remarks>Lorsque <c>dbDir</c> est <see langword="null"/>, les scores d’exécution ne sont pas évalués.
    /// Les requêtes prédites invalides sont comptées comme erreurs d’évaluation et remplacées par une représentation
    /// SQL vide pour poursuivre le calcul des métriques.</remarks>
    /// <param name="items">Séquence d’éléments à évaluer, chacun contenant la requête prédite, la requête de référence et l’identifiant de
    /// base de données cible.</param>
    /// <param name="dbs">Dictionnaire des schémas de bases de données indexés par identifiant, utilisé pour l’analyse et la normalisation
    /// des requêtes.</param>
    /// <param name="log">Action de journalisation optionnelle appelée pour signaler les erreurs d’évaluation et, en cas de
    /// non-correspondance exacte, les requêtes comparées.</param>
    /// <param name="dbDir">Chemin optionnel vers le répertoire contenant les fichiers SQLite par base de données pour activer l’évaluation
    /// par exécution.</param>
    /// <returns>Un objet <c>SpiderEvalResult</c> contenant les scores exacts, partiels et d’exécution agrégés, ainsi que le
    /// détail par entrée et le nombre d’erreurs d’analyse.</returns>
    public static SpiderEvalResult Evaluate(IEnumerable<(string PredSql, string GoldSql, string DbId)> items, 
        IReadOnlyDictionary<string, SpiderDatabase> dbs, 
        Action<string>? log = null, 
        string? dbDir = null)
    {
        var evaluator = new SpiderEvaluator();
        var result = new SpiderEvalResult { ExecEvaluated = dbDir is not null };
        var scores = result.Scores;
        var kmaps = new Dictionary<string, Dictionary<string, string>>();

        foreach (var (pStr, gStr, dbId) in items)
        {
            var db = dbs[dbId];
            if (!kmaps.TryGetValue(dbId, out var kmap))
                kmaps[dbId] = kmap = BuildForeignKeyMap(db);
            var schema = SqlSchema.FromDatabase(db);

            var gSql = SqlParser.GetSql(schema, gStr);
            var hardness = evaluator.EvalHardness(gSql);
            scores[hardness].Count++;
            scores["all"].Count++;

            ParsedSql pSql;
            try
            {
                pSql = SqlParser.GetSql(schema, pStr);
            }
            catch (Exception)
            {
                pSql = EmptySql();
                result.EvalErrorCount++;
                log?.Invoke($"eval_err_num:{result.EvalErrorCount}");
            }

            var gValid = BuildValidColUnits(gSql.From.TableUnits, schema);
            gSql = RebuildSqlCol(gValid, RebuildSqlVal(gSql), kmap)!;
            var pValid = BuildValidColUnits(pSql.From.TableUnits, schema);
            pSql = RebuildSqlCol(pValid, RebuildSqlVal(pSql), kmap)!;

            if (dbDir is not null && EvalExecMatch(Path.Combine(dbDir, dbId, dbId + ".sqlite"), pStr, gStr, pSql, gSql))
            {
                scores[hardness].Exec += 1;
                scores["all"].Exec += 1;
            }

            var exact = evaluator.EvalExactMatch(pSql, gSql);
            var partial = evaluator.PartialScores!;
            if (!exact)
            {
                log?.Invoke($"{hardness} pred: {pStr}");
                log?.Invoke($"{hardness} gold: {gStr}");
                log?.Invoke("");
            }

            foreach (var level in new[] { hardness, "all" })
            {
                var s = scores[level];
                if (exact) s.Exact += 1;
                foreach (var type in PartialTypes)
                {
                    var ps = partial[type];
                    var agg = s.Partial[type];
                    if (ps.PredTotal > 0)
                    {
                        agg.Acc += ps.Acc;
                        agg.AccCount++;
                    }
                    if (ps.LabelTotal > 0)
                    {
                        agg.Rec += ps.Rec;
                        agg.RecCount++;
                    }
                    agg.F1 += ps.F1;
                }
            }

            result.Entries.Add(new SpiderEvalEntry(pStr, gStr, hardness, exact, partial));
        }

        foreach (var level in Levels)
        {
            var s = scores[level];
            if (s.Count == 0)
                continue;
            s.Exact /= s.Count;
            s.Exec /= s.Count;
            foreach (var type in PartialTypes)
            {
                var agg = s.Partial[type];
                agg.Acc = agg.AccCount == 0 ? 0 : agg.Acc / agg.AccCount;
                agg.Rec = agg.RecCount == 0 ? 0 : agg.Rec / agg.RecCount;
                agg.F1 = agg.Acc == 0 && agg.Rec == 0 ? 1 : 2.0 * agg.Acc * agg.Rec / (agg.Rec + agg.Acc);
            }
        }
        return result;
    }

    /// <summary>
    /// Same inputs as evaluation.py: gold file ("sql\tdb_id" per line), prediction file (first tab separated column per line)
    /// and tables.json.
    /// </summary>
    public static SpiderEvalResult Evaluate(string goldPath, string predictPath, string tablesPath, Action<string>? log = null, string? dbDir = null)
    {
        static List<string[]> Read(string path) => File.ReadAllLines(path)
            .Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToList();

        var gold = Read(goldPath);
        var pred = Read(predictPath);
        var dbs = SpiderTablesLoader.LoadByDbId(tablesPath);
        var items = pred.Zip(gold, (p, g) => (p[0], g[0], g[1]));
        return Evaluate(items, dbs, log, dbDir);
    }

    public static string FormatScores(SpiderEvalResult result)
    {
        var sb = new StringBuilder();
        var scores = result.Scores;

        static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture).PadRight(20);

        void Row(string name, Func<LevelScores, string> value)
            => sb.AppendLine(name.PadRight(20) + " " + string.Join(" ", Levels.Select(l => value(scores[l]))));

        sb.AppendLine("".PadRight(20) + " " + string.Join(" ", Levels.Select(l => l.PadRight(20))));
        Row("count", s => s.Count.ToString(CultureInfo.InvariantCulture).PadRight(20));

        if (result.ExecEvaluated)
        {
            sb.AppendLine();
            sb.AppendLine("====================== EXECUTION ACCURACY =====================");
            Row("execution", s => F(s.Exec));
        }

        sb.AppendLine();
        sb.AppendLine("====================== EXACT MATCHING ACCURACY =====================");
        Row("exact match", s => F(s.Exact));

        sb.AppendLine();
        sb.AppendLine("---------------------PARTIAL MATCHING ACCURACY----------------------");
        foreach (var t in PartialTypes)
            Row(t, s => F(s.Partial[t].Acc));

        sb.AppendLine("---------------------- PARTIAL MATCHING RECALL ----------------------");
        foreach (var t in PartialTypes)
            Row(t, s => F(s.Partial[t].Rec));

        sb.AppendLine("---------------------- PARTIAL MATCHING F1 --------------------------");
        foreach (var t in PartialTypes)
            Row(t, s => F(s.Partial[t].F1));

        return sb.ToString();
    }
}
