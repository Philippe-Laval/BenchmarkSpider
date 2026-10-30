namespace BenchmarkSpider;

/// <summary>One line of dev_gold.sql / train_gold.sql: "SQL&lt;TAB&gt;db_id".</summary>
public record SpiderGoldEntry(string Sql, string DbId);

/// <summary>An example paired with its gold entry (null when none was found).</summary>
public record SpiderGoldMatch(SpiderExample Example, SpiderGoldEntry? Gold);

public static class SpiderGoldParser
{
    /// <summary>
    /// Finds, for each example, its gold SQL. train_gold.sql is not in the same order as
    /// train_others.json + train_spider.json, so entries are matched on (db_id, normalized query).
    /// Results are returned in the order of <paramref name="examples"/>.
    /// </summary>
    public static List<SpiderGoldMatch> MatchGold(IEnumerable<SpiderExample> examples, IEnumerable<SpiderGoldEntry> gold)
    {
        var lookup = new Dictionary<(string, string), SpiderGoldEntry>();
        foreach (var g in gold)
            lookup.TryAdd(Key(g.DbId, g.Sql), g);

        return examples
            .Select(e => new SpiderGoldMatch(e, lookup.GetValueOrDefault(Key(e.DbId, e.Query))))
            .ToList();
    }

    public static List<SpiderGoldMatch> MatchGold(IEnumerable<SpiderExample> examples, string goldPath)
        => MatchGold(examples, Load(goldPath));

    /// <summary>
    /// Construit une clé canonique composée de l’identifiant de base de données 
    /// et d’une version normalisée de la requête SQL.
    /// </summary>
    /// <param name="dbId">Identifiant de la base de données à inclure dans la clé.</param>
    /// <param name="sql">Requête SQL à normaliser en supprimant les espaces superflus, le point-virgule final et la casse.</param>
    /// <returns>Tuple contenant l’identifiant de base de données d’origine et la requête SQL normalisée en minuscules.</returns>
    private static (string, string) Key(string dbId, string sql)
        => (dbId, string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd(';', ' ')
            .ToLowerInvariant());

    public static List<SpiderGoldEntry> Load(string path) => Parse(File.ReadLines(path));

    public static async Task<List<SpiderGoldEntry>> LoadAsync(string path, CancellationToken ct = default)
        => Parse(await File.ReadAllLinesAsync(path, ct));

    public static List<SpiderGoldEntry> Parse(IEnumerable<string> lines)
    {
        var result = new List<SpiderGoldEntry>();
        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            result.Add(ParseLine(line, lineNumber));
        }
        return result;
    }

    public static SpiderGoldEntry ParseLine(string line, int lineNumber = 0)
    {
        var index = line.LastIndexOf('\t');
        if (index < 0)
            throw new FormatException($"Line {lineNumber}: missing tab separator between SQL and db_id.");

        var sql = line[..index].Trim();
        var dbId = line[(index + 1)..].Trim();
        if (sql.Length == 0 || dbId.Length == 0)
            throw new FormatException($"Line {lineNumber}: empty SQL or db_id.");

        return new SpiderGoldEntry(sql, dbId);
    }
}
