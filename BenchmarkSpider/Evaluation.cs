using System.Globalization;
using System.Text;

namespace BenchmarkSpider;

// C# port of spider/evaluation.py (exact / partial matching and execution accuracy).

/// <summary>
/// Représente les métriques d’un score partiel, incluant l’exactitude, le rappel, 
/// le score F1 et les volumes de référence et de prédiction.
/// </summary>
/// <remarks>Acc, Rec et F1 sont généralement exprimés sur l’intervalle [0, 1].</remarks>
/// <param name="Acc">Exactitude (accuracy) obtenue.</param>
/// <param name="Rec">Rappel (recall) obtenu.</param>
/// <param name="F1">Score F1 calculé à partir de l’exactitude et du rappel.</param>
/// <param name="LabelTotal">Nombre total d’éléments de référence attendus.</param>
/// <param name="PredTotal">Nombre total d’éléments prédits.</param>
public record PartialScore(double Acc, double Rec, double F1, int LabelTotal, int PredTotal);

/// <summary>
/// Représente un agrégat partiel de métriques d’évaluation, incluant l’exactitude, le rappel, 
/// le score F1 et leurs compteurs associés.
/// </summary>
/// <remarks>Utile pour accumuler des résultats intermédiaires avant de calculer des métriques globales.</remarks>
public class PartialAggregate
{
    /// <summary>
    /// Exactitude (accuracy)
    /// </summary>
    public double Acc { get; set; }
    /// <summary>
    /// Rappel (recall)
    /// </summary>
    public double Rec { get; set; }
    /// <summary>
    /// Score F1 calculé à partir de l’exactitude et du rappel
    /// </summary>
    public double F1 { get; set; }
    public int AccCount { get; set; }
    public int RecCount { get; set; }
}

/// <summary>
/// Représente les scores agrégés d’un niveau, incluant le nombre d’éléments évalués, 
/// le score exact, le score d’exécution et les scores partiels par catégorie.
/// </summary>
/// <remarks>La collection des scores partiels est initialisée avec une entrée par type défini dans
/// `SpiderEvaluator.PartialTypes`.</remarks>
public class LevelScores
{
    /// <summary>
    /// Nombre d’éléments évalués
    /// </summary>
    public int Count { get; set; }
    /// <summary>
    /// Obtient ou définit la valeur exacte.
    /// </summary>
    public double Exact { get; set; }
    /// <summary>
    /// Score d’exécution
    /// </summary>
    public double Exec { get; set; }

    /// <summary>
    /// Scores partiels par catégorie
    /// </summary>
    public Dictionary<string, PartialAggregate> Partial { get; } =
        SpiderEvaluator.PartialTypes.ToDictionary(t => t, _ => new PartialAggregate());
}

/// <summary>
/// 
/// </summary>
/// <param name="PredictSql"></param>
/// <param name="GoldSql"></param>
/// <param name="Hardness"></param>
/// <param name="Exact"></param>
/// <param name="Partial"></param>
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

    /// <summary>
    /// Extrait les éléments de type CondUnit d’une liste d’éléments de condition.
    /// </summary>
    /// <remarks>L’évaluation de la séquence est différée.</remarks>
    /// <param name="c">Liste en lecture seule des éléments de condition à filtrer.</param>
    /// <returns>Séquence des éléments qui sont de type CondUnit.</returns>
    private static IEnumerable<CondUnit> Units(IReadOnlyList<ConditionItem> c)
        => c.OfType<CondUnit>();

    /// <summary>
    /// Extrait les opérateurs de connexion des éléments de condition.
    /// </summary>
    /// <param name="c">Liste en lecture seule des éléments de condition à filtrer.</param>
    /// <returns>Séquence des valeurs d’opérateur des éléments de type <c>ConditionConnector</c>, dans l’ordre d’énumération.</returns>
    private static IEnumerable<string> Connectors(IReadOnlyList<ConditionItem> c) 
        => c.OfType<ConditionConnector>().Select(k => k.Operator);

    /// <summary>
    /// Rassemble toutes les unités de condition des clauses FROM, WHERE et HAVING d’une requête SQL analysée.
    /// </summary>
    /// <param name="sql">Requête SQL analysée contenant les conditions à parcourir.</param>
    /// <returns>Séquence de toutes les unités de condition trouvées dans FROM, WHERE et HAVING.</returns>
    private static IEnumerable<CondUnit> AllCondUnits(ParsedSql sql) 
        => Units(sql.From.Conds).Concat(Units(sql.Where)).Concat(Units(sql.Having));

    /// <summary>
    /// Collects all logical connectors found across the query's FROM conditions, WHERE clause, and HAVING clause.
    /// </summary>
    /// <param name="sql">Parsed SQL structure containing the FROM, WHERE, and HAVING sections to inspect.</param>
    /// <returns>A sequence of connector tokens found in the FROM conditions, WHERE clause, and HAVING clause.</returns>
    private static IEnumerable<string> AllConnectors(ParsedSql sql) 
        => Connectors(sql.From.Conds).Concat(Connectors(sql.Where)).Concat(Connectors(sql.Having));

    /// <summary>
    /// Calcule trois scores binaires de correspondance exacte entre les résultats prédits et les valeurs de référence.
    /// </summary>
    /// <param name="count">Nombre d’éléments correctement comptés.</param>
    /// <param name="predTotal">Nombre total d’éléments prédits.</param>
    /// <param name="labelTotal">Nombre total d’éléments de référence.</param>
    /// <returns>Un tuple (int, int, int) valant (1, 1, 1) quand les totaux sont égaux et que le nombre correct atteint ce total
    /// ; sinon (0, 0, 0).</returns>
    private static (int, int, int) GetScores(int count, int predTotal, int labelTotal)
    {
        if (predTotal != labelTotal)
            return (0, 0, 0);
        return count == predTotal ? (1, 1, 1) : (0, 0, 0);
    }
    
    /// <summary>
    /// Supprime la première occurrence d’un élément dans une liste, si elle est présente.
    /// </summary>
    /// <param name="list">Liste dans laquelle chercher l’élément.</param>
    /// <param name="item">Élément à supprimer.</param>
    /// <returns>1 si l’élément a été trouvé et supprimé, 0 sinon.</returns>
    private static int RemoveFirstIfPresent(List<string> list, string item)
    {
        var idx = list.IndexOf(item);
        if (idx < 0)
            return 0;
        list.RemoveAt(idx);
        return 1;
    }

    // ---------------------------------------------------------------- partial evaluations

    /// <summary>
    /// Évalue la correspondance entre les éléments SELECT prédits et de référence, 
    /// avec et sans prise en compte des agrégations.
    /// </summary>
    /// <remarks>Les correspondances sont calculées après normalisation canonique et retrait de la première
    /// occurrence trouvée pour éviter de compter plusieurs fois le même élément.</remarks>
    /// <param name="pred">Requête SQL analysée à évaluer.</param>
    /// <param name="label">Requête SQL analysée de référence utilisée pour le calcul des correspondances.</param>
    /// <returns>Un tuple contenant, dans l’ordre, le nombre d’éléments SELECT de référence, le nombre d’éléments SELECT prédits,
    /// le nombre de correspondances exactes, puis le nombre de correspondances en ignorant les agrégations.</returns>
    private static (int LabelTotal, int PredTotal, int Cnt, int CntWoAgg) EvalSel(ParsedSql pred, ParsedSql label)
    {
        var labelSel = label.Select.Items.Select(Canon).ToList();
        var labelWithoutAgg = label.Select.Items.Select(i => Canon(i.ValUnit)).ToList();
        int cnt = 0, cntWoAgg = 0;
        foreach (var unit in pred.Select.Items)
        {
            cnt += RemoveFirstIfPresent(labelSel, Canon(unit));
            cntWoAgg += RemoveFirstIfPresent(labelWithoutAgg, Canon(unit.ValUnit));
        }
        return (label.Select.Items.Count, pred.Select.Items.Count, cnt, cntWoAgg);
    }

    /// <summary>
    /// Évalue la correspondance des conditions <c>WHERE</c> entre une requête prédite et une requête de référence.
    /// </summary>
    /// <param name="pred">Requête SQL analysée utilisée comme prédiction.</param>
    /// <param name="label">Requête SQL analysée utilisée comme référence attendue.</param>
    /// <returns>Un tuple contenant, dans l’ordre, le nombre de conditions <c>WHERE</c> de la référence, 
    /// le nombre de conditions <c>WHERE</c> de la prédiction, 
    /// le nombre de correspondances exactes, 
    /// puis le nombre de correspondances en ignorant l’agrégation.</returns>
    private static (int, int, int, int) EvalWhere(ParsedSql pred, ParsedSql label)
    {
        var predConds = Units(pred.Where).ToList();
        var labelConds = Units(label.Where).Select(c => Canon(c)).ToList();
        var labelWithoutAgg = Units(label.Where).Select(c => Canon(c.ValUnit)).ToList();
        int cnt = 0, cntWoAgg = 0;
        foreach (var unit in predConds)
        {
            cnt += RemoveFirstIfPresent(labelConds, Canon(unit));
            cntWoAgg += RemoveFirstIfPresent(labelWithoutAgg, Canon(unit.ValUnit));
        }
        return (Units(label.Where).Count(), predConds.Count, cnt, cntWoAgg);
    }

    /// <summary>
    /// Retourne la partie de la chaîne située après le premier point ('.'), 
    /// ou la chaîne d'origine si aucun point n'est présent.
    /// </summary>
    /// <param name="s">Chaîne à analyser.</param>
    /// <returns>La sous-chaîne à l'index 1 après division sur '.', ou la chaîne d'origine si aucun séparateur n'est trouvé.</returns>
    private static string AfterDot(string s) => s.Contains('.') ? s.Split('.')[1] : s;

    /// <summary>
    /// Évalue la correspondance des colonnes GROUP BY entre une requête prédite et la référence.
    /// </summary>
    /// <remarks>La comparaison est effectuée sur les identifiants de colonnes normalisés après le dernier
    /// point, avec une mise en correspondance unitaire des doublons.</remarks>
    /// <param name="pred">Requête SQL prédite contenant les colonnes de regroupement à évaluer.</param>
    /// <param name="label">Requête SQL de référence contenant les colonnes de regroupement attendues.</param>
    /// <returns>Un tuple contenant, dans l'ordre, le nombre de colonnes GROUP BY de référence, le nombre de colonnes GROUP BY
    /// prédites et le nombre de correspondances trouvées.</returns>
    private static (int, int, int) EvalGroup(ParsedSql pred, ParsedSql label)
    {
        var predCols = pred.GroupBy.Select(u => AfterDot(u.ColId)).ToList();
        var labelCols = label.GroupBy.Select(u => AfterDot(u.ColId)).ToList();
        int cnt = 0;
        foreach (var col in predCols)
            cnt += RemoveFirstIfPresent(labelCols, col);
        return (label.GroupBy.Count, pred.GroupBy.Count, cnt);
    }

    /// <summary>
    /// Évalue la correspondance de la condition HAVING entre une requête prédite et la référence.
    /// </summary>
    /// <param name="pred">Requête SQL analysée prédite.</param>
    /// <param name="label">Requête SQL analysée de référence.</param>
    /// <returns>Un triplet contenant, dans l’ordre, l’indicateur de présence de GROUP BY dans la référence, l’indicateur de
    /// présence de GROUP BY dans la prédiction, puis 1 si les colonnes de GROUP BY et la condition HAVING normalisée
    /// correspondent, sinon 0.</returns>
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

    /// <summary>
    /// Évalue la présence et la correspondance des clauses ORDER BY entre une requête prédite et une requête de
    /// référence.
    /// </summary>
    /// <param name="pred">Requête SQL analysée à évaluer.</param>
    /// <param name="label">Requête SQL analysée de référence.</param>
    /// <returns>Un triplet contenant le total attendu de clause ORDER BY, le total prédit de clause ORDER BY et un indicateur de
    /// correspondance exacte (1 si ORDER BY correspond et si la présence de LIMIT est identique, sinon 0).</returns>
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
    
    /// <summary>
    /// Évalue la présence et la correspondance des connecteurs AND/OR entre une requête prédite et une requête de référence.
    /// </summary>
    /// <param name="pred">Requête SQL analysée à évaluer.</param>
    /// <param name="label">Requête SQL analysée de référence.</param>
    /// <returns>Un triplet contenant le total attendu de connecteurs AND/OR, le total prédit de connecteurs AND/OR et un indicateur de
    /// correspondance exacte (1 si les connecteurs correspondent, sinon 0).</returns>
    private static (int, int, int) EvalAndOr(ParsedSql pred, ParsedSql label)
    {
        var predAo = Connectors(pred.Where).ToHashSet();
        var labelAo = Connectors(label.Where).ToHashSet();
        if (predAo.SetEquals(labelAo))
            return (1, 1, 1);
        return (predAo.Count, labelAo.Count, 0);
    }

    /// <summary>
    /// Extrait les requêtes SQL imbriquées référencées dans une requête analysée.
    /// </summary>
    /// <remarks>L’ordre reflète l’itération des unités de condition, puis l’ajout de INTERSECT, EXCEPT et
    /// UNION lorsqu’elles sont définies.</remarks>
    /// <param name="sql">Requête SQL analysée à inspecter pour y trouver des sous-requêtes et des opérations d’ensemble imbriquées.</param>
    /// <returns>Liste des requêtes SQL imbriquées trouvées dans les unités de condition et dans les clauses INTERSECT, EXCEPT et
    /// UNION ; peut être vide si aucune n’est présente.</returns>
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

    /// <summary>
    /// Évalue une paire de requêtes SQL analysées et calcule les indicateurs de présence et de correspondance exacte
    /// imbriquée.
    /// </summary>
    /// <param name="pred">Requête prédite analysée à comparer.</param>
    /// <param name="label">Requête de référence analysée utilisée pour la comparaison.</param>
    /// <returns>Un triplet d'entiers contenant, dans l'ordre, l'indicateur de présence de label (1 ou 0), l'indicateur de
    /// présence de pred (1 ou 0) et l'indicateur de correspondance exacte (1 si les deux valeurs sont non nulles et
    /// exactement égales ; sinon 0).</returns>
    private static (int, int, int) EvalNested(ParsedSql? pred, ParsedSql? label)
    {
        var cnt = 0;
        if (pred is not null && label is not null && new SpiderEvaluator().EvalExactMatch(pred, label))
            cnt = 1;
        return (label is not null ? 1 : 0, pred is not null ? 1 : 0, cnt);
    }

    /// <summary>
    /// Évalue les opérations ensemblistes imbriquées entre une requête prédite et une requête de référence 
    /// en agrégeant INTERSECT, EXCEPT et UNION.
    /// </summary>
    /// <param name="pred">Requête SQL analysée à évaluer.</param>
    /// <param name="label">Requête SQL analysée de référence.</param>
    /// <returns>Un tuple de trois entiers contenant la somme des métriques calculées pour INTERSECT, EXCEPT et UNION.</returns>
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

    /// <summary>
    /// Calcule un score de composants pour une requête SQL analysée en additionnant la présence de clauses, 
    /// les jointures supplémentaires, les connecteurs OR et les conditions LIKE.
    /// </summary>
    /// <param name="sql">Requête SQL analysée à évaluer.</param>
    /// <returns>Nombre total de composants comptabilisés.</returns>
    private static int CountComponent1(ParsedSql sql)
    {
        var count = 0;
        if (sql.Where.Count > 0) count++;
        if (sql.GroupBy.Count > 0) count++;
        if (sql.OrderBy is not null) count++;
        if (sql.Limit is not null) count++;
        // JOIN
        if (sql.From.TableUnits.Count > 0) count += sql.From.TableUnits.Count - 1;
        count += AllConnectors(sql).Count(t => t == "or");
        count += AllCondUnits(sql).Count(c => c.OpId == LikeOp);
        return count;
    }

    /// <summary>
    /// Compte le nombre de composants SQL imbriqués dans la requête analysée.
    /// </summary>
    /// <param name="sql">Requête SQL analysée à inspecter.</param>
    /// <returns>Nombre de composants SQL imbriqués.</returns>
    private static int CountComponent2(ParsedSql sql) 
        => GetNestedSql(sql).Count;

    /// <summary>
    /// Calcule un compteur de complexité en additionnant les catégories de la requête qui apparaissent plus d’une fois.
    /// </summary>
    /// <remarks>Le comptage des agrégations suit une logique spécifique : certains éléments de condition
    /// (not_op, connecteurs AND/OR) sont inclus.</remarks>
    /// <param name="sql">Requête SQL analysée à évaluer.</param>
    /// <returns>Nombre de catégories dépassant le seuil de 1 : agrégations, colonnes SELECT, 
    /// conditions WHERE et clauses GROUP BY.</returns>
    private static int CountOthers(ParsedSql sql)
    {
        var count = 0;

        // Same quirks as the Python version: has_agg(cond_unit) looks at not_op, has_agg('and'/'or') is always true.

        // number of aggregation
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

        // number of select columns
        if (sql.Select.Items.Count > 1) count++;
        // number of where conditions
        if (sql.Where.Count > 1) count++;
        // number of group by clauses
        if (sql.GroupBy.Count > 1) count++;
        
        return count;
    }

    /// <summary>
    /// Évalue le niveau de difficulté d’une requête SQL analysée selon des métriques de complexité structurelle.
    /// </summary>
    /// <remarks>La classification est déterminée à partir du nombre de composants principaux, de composants
    /// secondaires et d’autres constructions détectées.</remarks>
    /// <param name="sql">Requête SQL analysée à classifier.</param>
    /// <returns>Une chaîne indiquant le niveau de difficulté : "easy", "medium", "hard" ou "extra".</returns>
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

    /// <summary>
    /// Évalue si une requête prédite correspond exactement à la requête de référence 
    /// à partir des scores partiels et des tables référencées.
    /// </summary>
    /// <remarks>Met à jour la propriété PartialScores avec le résultat de l’évaluation partielle.</remarks>
    /// <param name="pred">Requête SQL analysée prédite.</param>
    /// <param name="label">Requête SQL analysée de référence.</param>
    /// <returns>true si tous les scores partiels ont un F1 de 1 et, lorsque la référence contient des tables, si les ensembles
    /// de tables canonisées sont identiques ; sinon, false.</returns>
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

    /// <summary>
    /// Calcule un score partiel à partir du nombre d’éléments corrects et des totaux prédits et attendus.
    /// </summary>
    /// <param name="cnt">Nombre d’éléments correctement prédits.</param>
    /// <param name="predTotal">Nombre total d’éléments prédits.</param>
    /// <param name="labelTotal">Nombre total d’éléments attendus.</param>
    /// <returns>Instance de <c>PartialScore</c> contenant l’accuracy, le rappel, le score F1, ainsi que les totaux attendus et
    /// prédits.</returns>
    private static PartialScore Score(int cnt, int predTotal, int labelTotal)
    {
        var (acc, rec, f1) = GetScores(cnt, predTotal, labelTotal);
        return new PartialScore(acc, rec, f1, labelTotal, predTotal);
    }

    /// <summary>
    /// Évalue une requête prédite par rapport à une requête de référence et calcule les scores partiels pour chaque composant.
    /// </summary>
    /// <param name="pred">Requête SQL analysée prédite.</param>
    /// <param name="label">Requête SQL analysée de référence.</param>
    /// <returns>Dictionnaire associant chaque composant de la requête à son score partiel.</returns>
    public Dictionary<string, PartialScore> EvalPartialMatch(ParsedSql pred, ParsedSql label)
    {
        var res = new Dictionary<string, PartialScore>();

        var (labelTotal, predTotal, cnt, cntWithoutAgg) = EvalSel(pred, label);
        res["select"] = Score(cnt, predTotal, labelTotal);
        res["select(no AGG)"] = Score(cntWithoutAgg, predTotal, labelTotal);

        (labelTotal, predTotal, cnt, cntWithoutAgg) = EvalWhere(pred, label);
        res["where"] = Score(cnt, predTotal, labelTotal);
        res["where(no OP)"] = Score(cntWithoutAgg, predTotal, labelTotal);

        (labelTotal, predTotal, cnt) = EvalGroup(pred, label);
        res["group(no Having)"] = Score(cnt, predTotal, labelTotal);

        (labelTotal, predTotal, cnt) = EvalHaving(pred, label);
        res["group"] = Score(cnt, predTotal, labelTotal);

        (labelTotal, predTotal, cnt) = EvalOrder(pred, label);
        res["order"] = Score(cnt, predTotal, labelTotal);

        (labelTotal, predTotal, cnt) = EvalAndOr(pred, label);
        res["and/or"] = Score(cnt, predTotal, labelTotal);

        (labelTotal, predTotal, cnt) = EvalIuen(pred, label);
        res["IUEN"] = Score(cnt, predTotal, labelTotal);

        (labelTotal, predTotal, cnt) = EvalKeywords(pred, label);
        res["keywords"] = Score(cnt, predTotal, labelTotal);

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
        var result = new SpiderEvalResult {
            // Indique si l’évaluation par exécution a été effectuée en fonction de la présence du répertoire de bases de données.
            ExecEvaluated = dbDir is not null 
        };
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
