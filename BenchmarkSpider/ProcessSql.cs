namespace BenchmarkSpider;

// C# port of spider/process_sql.py.
//
// Assumptions (same as the Python version):
//   1. sql is correct
//   2. only table name has alias
//   3. only one intersect/union/except
//
// val: number(double)/string/sql/col_unit
// col_unit: (agg_id, col_id, isDistinct)
// val_unit: (unit_op, col_unit1, col_unit2)
// table_unit: (table_type, col_unit/sql)
// cond_unit: (not_op, op_id, val_unit, val1, val2)
// condition: [cond_unit1, 'and'/'or', cond_unit2, ...]

public class SqlParseException(string message) : Exception(message);

/// <summary>col_unit: (agg_id, col_id, isDistinct)</summary>
public record ColUnit(int AggId, string ColId, bool IsDistinct);

/// <summary>val_unit: (unit_op, col_unit1, col_unit2)</summary>
public record ValUnit(int UnitOp, ColUnit ColUnit1, ColUnit? ColUnit2);

/// <summary>An item of the select clause: (agg_id, val_unit).</summary>
public record SelectItem(int AggId, ValUnit ValUnit);

/// <summary>select: (isDistinct, [(agg_id, val_unit), ...])</summary>
public record SelectClause(bool IsDistinct, IReadOnlyList<SelectItem> Items);

/// <summary>table_unit: either a table ("table_unit", tableId) or a sub query ("sql", sql).</summary>
public record TableUnit(string TableType, string? TableId, ParsedSql? SubQuery);

public record FromClause(IReadOnlyList<TableUnit> TableUnits, IReadOnlyList<ConditionItem> Conds);

/// <summary>orderBy: ('asc'/'desc', [val_unit1, val_unit2, ...])</summary>
public record OrderByClause(string OrderType, IReadOnlyList<ValUnit> ValUnits);

/// <summary>val: number / string / sql / col_unit</summary>
public abstract record SqlValue;
public record NumberValue(double Value) : SqlValue;
public record StringValue(string Value) : SqlValue;
public record SubQueryValue(ParsedSql Sql) : SqlValue;
public record ColumnValue(ColUnit ColUnit) : SqlValue;

/// <summary>An element of a condition: a cond_unit or an 'and'/'or' connector.</summary>
public abstract record ConditionItem;

/// <summary>cond_unit: (not_op, op_id, val_unit, val1, val2)</summary>
public record CondUnit(bool NotOp, int OpId, ValUnit ValUnit, SqlValue? Val1, SqlValue? Val2) : ConditionItem;
public record ConditionConnector(string Operator) : ConditionItem;

public class ParsedSql
{
    public required SelectClause Select { get; init; }
    public required FromClause From { get; init; }
    public required IReadOnlyList<ConditionItem> Where { get; init; }
    public required IReadOnlyList<ColUnit> GroupBy { get; init; }
    public required IReadOnlyList<ConditionItem> Having { get; init; }

    /// <summary>Null when there is no order by clause.</summary>
    public OrderByClause? OrderBy { get; init; }
    public int? Limit { get; init; }
    public ParsedSql? Intersect { get; init; }
    public ParsedSql? Union { get; init; }
    public ParsedSql? Except { get; init; }
}

/// <summary>Simple schema which maps table and column to a unique identifier.</summary>
public class SqlSchema
{
    public IReadOnlyDictionary<string, List<string>> Tables { get; }

    public IReadOnlyDictionary<string, string> IdMap { get; }

    public SqlSchema(IReadOnlyDictionary<string, List<string>> tables)
    {
        Tables = tables;
        IdMap = Map(tables);
    }

    private static Dictionary<string, string> Map(IReadOnlyDictionary<string, List<string>> schema)
    {
        var idMap = new Dictionary<string, string> { ["*"] = "__all__" };
        foreach (var (table, columns) in schema)
        {
            foreach (var column in columns)
            {
                var key = table.ToLowerInvariant() + "." + column.ToLowerInvariant();
                idMap[key] = "__" + key + "__";
            }
        }
        foreach (var table in schema.Keys)
            idMap[table.ToLowerInvariant()] = "__" + table.ToLowerInvariant() + "__";
        return idMap;
    }

    /// <summary>Builds the schema of a database described in tables.json (equivalent of get_schema).</summary>
    public static SqlSchema FromDatabase(SpiderDatabase db)
    {
        var tables = new Dictionary<string, List<string>>();
        for (var i = 0; i < db.TableNamesOriginal.Count; i++)
        {
            tables[db.TableNamesOriginal[i].ToLowerInvariant()] = db.ColumnNamesOriginal
                .Where(c => c.TableIndex == i)
                .Select(c => c.Name.ToLowerInvariant())
                .ToList();
        }
        return new SqlSchema(tables);
    }

    /// <summary>Builds the schema directly from a SQLite database file (equivalent of get_schema).</summary>
    public static SqlSchema FromSqlite(string dbPath)
    {
        var tables = new Dictionary<string, List<string>>();
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
        }.ToString();

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        conn.Open();

        var tableNames = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tableNames.Add(reader.GetString(0).ToLowerInvariant());
        }

        foreach (var table in tableNames)
        {
            var columns = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                columns.Add(reader.GetString(1).ToLowerInvariant());
            tables[table] = columns;
        }
        return new SqlSchema(tables);
    }

    /// <summary>Equivalent of get_schema_from_json
    public static SqlSchema FromJson(string path)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var tables = new Dictionary<string, List<string>>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var table = entry.GetProperty("table").GetString()!.ToLowerInvariant();
            tables[table] = entry.GetProperty("col_data").EnumerateArray()
                .Select(col => col.GetProperty("column_name").GetString()!.ToLowerInvariant())
                .ToList();
        }
        return new SqlSchema(tables);
    }
}

public static class SqlParser
{
    public static readonly string[] ClauseKeywords = ["select", "from", "where", "group", "order", "limit", "intersect", "union", "except"];
    public static readonly string[] JoinKeywords = ["join", "on", "as"];
    public static readonly string[] WhereOps = ["not", "between", "=", ">", "<", ">=", "<=", "!=", "in", "like", "is", "exists"];
    public static readonly string[] UnitOps = ["none", "-", "+", "*", "/"];
    public static readonly string[] AggOps = ["none", "max", "min", "count", "sum", "avg"];
    public static readonly string[] CondOps = ["and", "or"];
    public static readonly string[] SqlOps = ["intersect", "union", "except"];
    public static readonly string[] OrderOps = ["desc", "asc"];

    public const string TableTypeSql = "sql";
    public const string TableTypeTableUnit = "table_unit";

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new SqlParseException(message);
    }

    private static string Tok(IReadOnlyList<string> toks, int idx)
        => idx >= 0 && idx < toks.Count ? toks[idx] : throw new SqlParseException($"Unexpected end of tokens at index {idx}.");

    private static TValue Lookup<TValue>(IReadOnlyDictionary<string, TValue> dict, string key, string what)
        => dict.TryGetValue(key, out var value) ? value : throw new SqlParseException($"Unknown {what}: {key}");

    /// <summary>
    /// Parses a query into the structured form of process_sql.py.
    /// </summary>
    public static ParsedSql GetSql(SqlSchema schema, string query)
    {
        var toks = Tokenize(query);
        var tablesWithAlias = GetTablesWithAlias(schema.Tables, toks);
        return ParseSql(toks, 0, tablesWithAlias, schema).Sql;
    }

    /// <summary>
    /// Replacement of the NLTK based tokenizer: string values are kept as one token (with their
    /// quotes and case), everything else is lower-cased. Operators like !=, >=, &lt;= are merged.
    /// </summary>
    public static List<string> Tokenize(string query)
    {
        var text = query.Replace('\'', '"');
        Check(text.Count(c => c == '"') % 2 == 0, "Unexpected quote");

        var toks = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '"')
            {
                var end = text.IndexOf('"', i + 1);
                toks.Add(text.Substring(i, end - i + 1));
                i = end + 1;
            }
            else if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
                    i++;
                toks.Add(text[start..i].ToLowerInvariant());
            }
            else
            {
                toks.Add(c.ToString());
                i++;
            }
        }

        // merge !=, >=, <=
        for (var idx = toks.Count - 1; idx > 0; idx--)
        {
            if (toks[idx] == "=" && toks[idx - 1] is "!" or ">" or "<")
            {
                toks[idx - 1] += "=";
                toks.RemoveAt(idx);
            }
        }
        return toks;
    }

    /// <summary>Scan the index of 'as' and build the map for all alias.</summary>
    public static Dictionary<string, string> ScanAlias(IReadOnlyList<string> toks)
    {
        var alias = new Dictionary<string, string>();
        for (var idx = 0; idx < toks.Count; idx++)
        {
            if (toks[idx] == "as")
                alias[Tok(toks, idx + 1)] = Tok(toks, idx - 1);
        }
        return alias;
    }

    public static Dictionary<string, string> GetTablesWithAlias(IReadOnlyDictionary<string, List<string>> schema, IReadOnlyList<string> toks)
    {
        var tables = ScanAlias(toks);
        foreach (var key in schema.Keys)
        {
            Check(!tables.ContainsKey(key), $"Alias {key} has the same name in table");
            tables[key] = key;
        }
        return tables;
    }

    private static (int Idx, string ColId) ParseCol(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string>? defaultTables = null)
    {
        var tok = Tok(toks, startIdx);
        if (tok == "*")
            return (startIdx + 1, schema.IdMap[tok]);

        if (tok.Contains('.'))
        {
            var parts = tok.Split('.');
            Check(parts.Length == 2, $"Error col: {tok}");
            var key = Lookup(tablesWithAlias, parts[0], "alias") + "." + parts[1];
            return (startIdx + 1, Lookup(schema.IdMap, key, "column"));
        }

        Check(defaultTables is { Count: > 0 }, "Default tables should not be None or empty");

        foreach (var alias in defaultTables!)
        {
            var table = Lookup(tablesWithAlias, alias, "alias");
            if (schema.Tables.TryGetValue(table, out var columns) && columns.Contains(tok))
                return (startIdx + 1, Lookup(schema.IdMap, table + "." + tok, "column"));
        }

        throw new SqlParseException($"Error col: {tok}");
    }

    private static (int Idx, ColUnit Value) ParseColUnit(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string>? defaultTables = null)
    {
        var idx = startIdx;
        var len = toks.Count;
        var isBlock = false;
        var isDistinct = false;
        if (Tok(toks, idx) == "(")
        {
            isBlock = true;
            idx++;
        }

        if (AggOps.Contains(Tok(toks, idx)))
        {
            var aggId = Array.IndexOf(AggOps, toks[idx]);
            idx++;
            Check(idx < len && toks[idx] == "(", "Expected '(' after aggregate");
            idx++;
            if (Tok(toks, idx) == "distinct")
            {
                idx++;
                isDistinct = true;
            }
            (idx, var aggColId) = ParseCol(toks, idx, tablesWithAlias, schema, defaultTables);
            Check(idx < len && toks[idx] == ")", "Expected ')' after aggregate");
            idx++;
            return (idx, new ColUnit(aggId, aggColId, isDistinct));
        }

        if (Tok(toks, idx) == "distinct")
        {
            idx++;
            isDistinct = true;
        }
        var noneAgg = Array.IndexOf(AggOps, "none");
        (idx, var colId) = ParseCol(toks, idx, tablesWithAlias, schema, defaultTables);

        if (isBlock)
        {
            Check(Tok(toks, idx) == ")", "Expected ')'");
            idx++;
        }

        return (idx, new ColUnit(noneAgg, colId, isDistinct));
    }

    private static (int Idx, ValUnit Value) ParseValUnit(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string>? defaultTables = null)
    {
        var idx = startIdx;
        var len = toks.Count;
        var isBlock = false;
        if (Tok(toks, idx) == "(")
        {
            isBlock = true;
            idx++;
        }

        ColUnit? colUnit2 = null;
        var unitOp = Array.IndexOf(UnitOps, "none");

        (idx, var colUnit1) = ParseColUnit(toks, idx, tablesWithAlias, schema, defaultTables);
        if (idx < len && UnitOps.Contains(toks[idx]))
        {
            unitOp = Array.IndexOf(UnitOps, toks[idx]);
            idx++;
            (idx, colUnit2) = ParseColUnit(toks, idx, tablesWithAlias, schema, defaultTables);
        }

        if (isBlock)
        {
            Check(Tok(toks, idx) == ")", "Expected ')'");
            idx++;
        }

        return (idx, new ValUnit(unitOp, colUnit1, colUnit2));
    }

    private static (int Idx, string TableId, string TableName) ParseTableUnit(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema)
    {
        var idx = startIdx;
        var len = toks.Count;
        var key = Lookup(tablesWithAlias, Tok(toks, idx), "table");

        if (idx + 1 < len && toks[idx + 1] == "as")
            idx += 3;
        else
            idx += 1;

        return (idx, Lookup(schema.IdMap, key, "table"), key);
    }

    private static (int Idx, SqlValue Value) ParseValue(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string>? defaultTables = null)
    {
        var idx = startIdx;
        var len = toks.Count;

        var isBlock = false;
        if (Tok(toks, idx) == "(")
        {
            isBlock = true;
            idx++;
        }

        SqlValue val;
        if (Tok(toks, idx) == "select")
        {
            ParsedSql sub;
            (idx, sub) = ParseSql(toks, idx, tablesWithAlias, schema);
            val = new SubQueryValue(sub);
        }
        else if (toks[idx].Contains('"'))
        {
            val = new StringValue(toks[idx]);
            idx++;
        }
        else if (double.TryParse(toks[idx], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            val = new NumberValue(number);
            idx++;
        }
        else
        {
            var endIdx = idx;
            while (endIdx < len && toks[endIdx] != "," && toks[endIdx] != ")"
                   && toks[endIdx] != "and" && !ClauseKeywords.Contains(toks[endIdx]) && !JoinKeywords.Contains(toks[endIdx]))
                endIdx++;

            var slice = toks.Skip(startIdx).Take(endIdx - startIdx).ToList();
            var (_, colUnit) = ParseColUnit(slice, 0, tablesWithAlias, schema, defaultTables);
            val = new ColumnValue(colUnit);
            idx = endIdx;
        }

        if (isBlock)
        {
            Check(Tok(toks, idx) == ")", "Expected ')'");
            idx++;
        }

        return (idx, val);
    }

    private static (int Idx, List<ConditionItem> Conds) ParseCondition(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string>? defaultTables = null)
    {
        var idx = startIdx;
        var len = toks.Count;
        var conds = new List<ConditionItem>();

        while (idx < len)
        {
            (idx, var valUnit) = ParseValUnit(toks, idx, tablesWithAlias, schema, defaultTables);
            var notOp = false;
            if (Tok(toks, idx) == "not")
            {
                notOp = true;
                idx++;
            }

            Check(idx < len && WhereOps.Contains(toks[idx]), $"Error condition: idx: {idx}, tok: {(idx < len ? toks[idx] : "<end>")}");
            var opId = Array.IndexOf(WhereOps, toks[idx]);
            idx++;
            SqlValue val1;
            SqlValue? val2 = null;
            if (opId == Array.IndexOf(WhereOps, "between"))
            {
                (idx, val1) = ParseValue(toks, idx, tablesWithAlias, schema, defaultTables);
                Check(Tok(toks, idx) == "and", "Expected 'and' in between");
                idx++;
                (idx, val2) = ParseValue(toks, idx, tablesWithAlias, schema, defaultTables);
            }
            else
            {
                (idx, val1) = ParseValue(toks, idx, tablesWithAlias, schema, defaultTables);
            }

            conds.Add(new CondUnit(notOp, opId, valUnit, val1, val2));

            if (idx < len && (ClauseKeywords.Contains(toks[idx]) || toks[idx] is ")" or ";" || JoinKeywords.Contains(toks[idx])))
                break;

            if (idx < len && CondOps.Contains(toks[idx]))
            {
                conds.Add(new ConditionConnector(toks[idx]));
                idx++;
            }
        }

        return (idx, conds);
    }

    private static (int Idx, SelectClause Value) ParseSelect(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string>? defaultTables = null)
    {
        var idx = startIdx;
        var len = toks.Count;

        Check(Tok(toks, idx) == "select", "'select' not found");
        idx++;
        var isDistinct = false;
        if (idx < len && toks[idx] == "distinct")
        {
            idx++;
            isDistinct = true;
        }
        var items = new List<SelectItem>();

        while (idx < len && !ClauseKeywords.Contains(toks[idx]))
        {
            var aggId = Array.IndexOf(AggOps, "none");
            if (AggOps.Contains(toks[idx]))
            {
                aggId = Array.IndexOf(AggOps, toks[idx]);
                idx++;
            }
            (idx, var valUnit) = ParseValUnit(toks, idx, tablesWithAlias, schema, defaultTables);
            items.Add(new SelectItem(aggId, valUnit));
            if (idx < len && toks[idx] == ",")
                idx++;
        }

        return (idx, new SelectClause(isDistinct, items));
    }

    /// <summary>Assume in the from clause, all table units are combined with join.</summary>
    private static (int Idx, List<TableUnit> TableUnits, List<ConditionItem> Conds, List<string> DefaultTables) ParseFrom(
        IReadOnlyList<string> toks, int startIdx, IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema)
    {
        var len = toks.Count;
        var fromIdx = -1;
        for (var i = startIdx; i < len; i++)
        {
            if (toks[i] == "from")
            {
                fromIdx = i;
                break;
            }
        }
        Check(fromIdx >= 0, "'from' not found");

        var idx = fromIdx + 1;
        var defaultTables = new List<string>();
        var tableUnits = new List<TableUnit>();
        var conds = new List<ConditionItem>();

        while (idx < len)
        {
            var isBlock = false;
            if (toks[idx] == "(")
            {
                isBlock = true;
                idx++;
            }

            if (Tok(toks, idx) == "select")
            {
                (idx, var sql) = ParseSql(toks, idx, tablesWithAlias, schema);
                tableUnits.Add(new TableUnit(TableTypeSql, null, sql));
            }
            else
            {
                if (idx < len && toks[idx] == "join")
                    idx++;
                (idx, var tableId, var tableName) = ParseTableUnit(toks, idx, tablesWithAlias, schema);
                tableUnits.Add(new TableUnit(TableTypeTableUnit, tableId, null));
                defaultTables.Add(tableName);
            }
            if (idx < len && toks[idx] == "on")
            {
                idx++;
                (idx, var thisConds) = ParseCondition(toks, idx, tablesWithAlias, schema, defaultTables);
                if (conds.Count > 0)
                    conds.Add(new ConditionConnector("and"));
                conds.AddRange(thisConds);
            }

            if (isBlock)
            {
                Check(Tok(toks, idx) == ")", "Expected ')'");
                idx++;
            }
            if (idx < len && (ClauseKeywords.Contains(toks[idx]) || toks[idx] is ")" or ";"))
                break;
        }

        return (idx, tableUnits, conds, defaultTables);
    }

    private static (int Idx, List<ConditionItem> Conds) ParseWhere(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string> defaultTables)
    {
        if (startIdx >= toks.Count || toks[startIdx] != "where")
            return (startIdx, []);

        return ParseCondition(toks, startIdx + 1, tablesWithAlias, schema, defaultTables);
    }

    private static (int Idx, List<ColUnit> ColUnits) ParseGroupBy(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string> defaultTables)
    {
        var idx = startIdx;
        var len = toks.Count;
        var colUnits = new List<ColUnit>();

        if (idx >= len || toks[idx] != "group")
            return (idx, colUnits);

        idx++;
        Check(Tok(toks, idx) == "by", "Expected 'by' after 'group'");
        idx++;

        while (idx < len && !(ClauseKeywords.Contains(toks[idx]) || toks[idx] is ")" or ";"))
        {
            (idx, var colUnit) = ParseColUnit(toks, idx, tablesWithAlias, schema, defaultTables);
            colUnits.Add(colUnit);
            if (idx < len && toks[idx] == ",")
                idx++;
            else
                break;
        }

        return (idx, colUnits);
    }

    private static (int Idx, OrderByClause? Value) ParseOrderBy(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string> defaultTables)
    {
        var idx = startIdx;
        var len = toks.Count;
        var valUnits = new List<ValUnit>();
        var orderType = "asc";

        if (idx >= len || toks[idx] != "order")
            return (idx, null);

        idx++;
        Check(Tok(toks, idx) == "by", "Expected 'by' after 'order'");
        idx++;

        while (idx < len && !(ClauseKeywords.Contains(toks[idx]) || toks[idx] is ")" or ";"))
        {
            (idx, var valUnit) = ParseValUnit(toks, idx, tablesWithAlias, schema, defaultTables);
            valUnits.Add(valUnit);
            if (idx < len && OrderOps.Contains(toks[idx]))
            {
                orderType = toks[idx];
                idx++;
            }
            if (idx < len && toks[idx] == ",")
                idx++;
            else
                break;
        }

        return (idx, new OrderByClause(orderType, valUnits));
    }

    private static (int Idx, List<ConditionItem> Conds) ParseHaving(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema, IReadOnlyList<string> defaultTables)
    {
        if (startIdx >= toks.Count || toks[startIdx] != "having")
            return (startIdx, []);

        return ParseCondition(toks, startIdx + 1, tablesWithAlias, schema, defaultTables);
    }

    private static (int Idx, int? Limit) ParseLimit(IReadOnlyList<string> toks, int startIdx)
    {
        if (startIdx < toks.Count && toks[startIdx] == "limit")
        {
            var text = Tok(toks, startIdx + 1);
            Check(int.TryParse(text, out var limit), $"Invalid limit: {text}");
            return (startIdx + 2, limit);
        }

        return (startIdx, null);
    }

    private static int SkipSemicolon(IReadOnlyList<string> toks, int startIdx)
    {
        var idx = startIdx;
        while (idx < toks.Count && toks[idx] == ";")
            idx++;
        return idx;
    }

    private static (int Idx, ParsedSql Sql) ParseSql(IReadOnlyList<string> toks, int startIdx,
        IReadOnlyDictionary<string, string> tablesWithAlias, SqlSchema schema)
    {
        var isBlock = false; // indicate whether this is a block of sql/sub-sql
        var len = toks.Count;
        var idx = startIdx;

        if (Tok(toks, idx) == "(")
        {
            isBlock = true;
            idx++;
        }

        // parse from clause in order to get default tables
        var (fromEndIdx, tableUnits, fromConds, defaultTables) = ParseFrom(toks, startIdx, tablesWithAlias, schema);
        var from = new FromClause(tableUnits, fromConds);
        var (_, select) = ParseSelect(toks, idx, tablesWithAlias, schema, defaultTables);
        idx = fromEndIdx;
        (idx, var where) = ParseWhere(toks, idx, tablesWithAlias, schema, defaultTables);
        (idx, var groupBy) = ParseGroupBy(toks, idx, tablesWithAlias, schema, defaultTables);
        (idx, var having) = ParseHaving(toks, idx, tablesWithAlias, schema, defaultTables);
        (idx, var orderBy) = ParseOrderBy(toks, idx, tablesWithAlias, schema, defaultTables);
        (idx, var limit) = ParseLimit(toks, idx);

        idx = SkipSemicolon(toks, idx);
        if (isBlock)
        {
            Check(Tok(toks, idx) == ")", "Expected ')'");
            idx++;
        }
        idx = SkipSemicolon(toks, idx);

        // intersect/union/except clause
        ParsedSql? intersect = null, union = null, except = null;
        if (idx < len && SqlOps.Contains(toks[idx]))
        {
            var sqlOp = toks[idx];
            idx++;
            (idx, var iueSql) = ParseSql(toks, idx, tablesWithAlias, schema);
            switch (sqlOp)
            {
                case "intersect": intersect = iueSql; break;
                case "union": union = iueSql; break;
                default: except = iueSql; break;
            }
        }

        return (idx, new ParsedSql
        {
            From = from,
            Select = select,
            Where = where,
            GroupBy = groupBy,
            Having = having,
            OrderBy = orderBy,
            Limit = limit,
            Intersect = intersect,
            Union = union,
            Except = except
        });
    }
}
