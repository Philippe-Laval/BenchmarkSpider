using System.Globalization;

namespace BenchmarkSpider;

public partial class SpiderEvaluator
{
    private static string ExecKey(ValUnit v)
    {
        static string C(ColUnit c) => $"({c.AggId},{c.ColId},{c.IsDistinct})";
        return v.ColUnit2 is null ? C(v.ColUnit1) : $"{v.UnitOp}|{C(v.ColUnit1)}|{C(v.ColUnit2)}";
    }

    private static string ExecCell(object value) => value switch
    {
        DBNull => "N",
        byte[] b => "b:" + Convert.ToBase64String(b),
        string s => "s:" + s,
        _ => "n:" + Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
    };

    private static List<List<string>> ExecQuery(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var rows = new List<List<string>>();
        while (reader.Read())
        {
            var row = new List<string>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
                row.Add(ExecCell(reader.GetValue(i)));
            rows.Add(row);
        }
        return rows;
    }
    private static Dictionary<string, List<string>> ResMap(List<List<string>> res, IReadOnlyList<SelectItem> items)
    {
        var map = new Dictionary<string, List<string>>();
        for (var idx = 0; idx < items.Count; idx++)
            map[ExecKey(items[idx].ValUnit)] = res.Select(r => r[idx]).ToList();
        return map;
    }

    /// <summary>Equivalent of eval_exec_match: true when the result columns of prediction and gold match.</summary>
    public static bool EvalExecMatch(string dbPath, string pStr, string gStr, ParsedSql pred, ParsedSql gold)
    {
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
        }.ToString();
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        conn.Open();

        List<List<string>> pRes;
        try
        {
            pRes = ExecQuery(conn, pStr);
        }
        catch (Exception)
        {
            return false;
        }
        var gRes = ExecQuery(conn, gStr);

        var pMap = ResMap(pRes, pred.Select.Items);
        var gMap = ResMap(gRes, gold.Select.Items);
        return pMap.Count == gMap.Count
            && pMap.All(kv => gMap.TryGetValue(kv.Key, out var other) && kv.Value.SequenceEqual(other));
    }
}