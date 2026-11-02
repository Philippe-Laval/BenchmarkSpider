using BenchmarkSpider.Library.Models;

namespace BenchmarkSpider.Library.Parsers;

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

    /// <summary>
    /// Associe une séquence d'exemples Spider aux données de référence chargées depuis un fichier.
    /// </summary>
    /// <remarks>Charge les données de référence depuis <paramref name="goldPath"/> puis appelle la surcharge
    /// qui prend directement les références en entrée.</remarks>
    /// <param name="examples">Séquence d'exemples Spider à comparer aux références.</param>
    /// <param name="goldPath">Chemin du fichier contenant les données de référence à charger.</param>
    /// <returns>Liste des résultats de correspondance entre les exemples et les références.</returns>
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

    /// <summary>
    /// Charge les entrées SpiderGold à partir des lignes d’un fichier.
    /// </summary>
    /// <param name="path">Chemin du fichier à lire.</param>
    /// <returns>Liste des entrées SpiderGold analysées.</returns>
    public static List<SpiderGoldEntry> Load(string path) => Parse(File.ReadLines(path));

    /// <summary>
    /// Charge et analyse de manière asynchrone les entrées Spider Gold à partir du fichier spécifié.
    /// </summary>
    /// <param name="path">Chemin du fichier à lire.</param>
    /// <param name="ct">Jeton utilisé pour surveiller les demandes d’annulation.</param>
    /// <returns>Tâche qui représente l’opération asynchrone et qui contient la liste des entrées Spider Gold analysées une fois
    /// l’opération terminée.</returns>
    public static async Task<List<SpiderGoldEntry>> LoadAsync(string path, CancellationToken ct = default)
        => Parse(await File.ReadAllLinesAsync(path, ct));

    /// <summary>
    /// Analyse une séquence de lignes et produit les entrées SpiderGold correspondantes.
    /// </summary>
    /// <remarks>Les lignes nulles, vides ou composées uniquement d’espaces sont ignorées.</remarks>
    /// <param name="lines">Séquence de lignes à analyser.</param>
    /// <returns>Liste des entrées créées à partir des lignes non vides, dans l’ordre d’origine.</returns>
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

    /// <summary>
    /// Analyse une ligne contenant une requête SQL et un identifiant de base de données séparés par une tabulation,
    /// puis crée une instance de <c>SpiderGoldEntry</c>.
    /// </summary>
    /// <remarks>La séparation est effectuée sur la dernière tabulation de la ligne.</remarks>
    /// <param name="line">Ligne à analyser, au format « SQL\tdb_id ».</param>
    /// <param name="lineNumber">Numéro de ligne utilisé dans les messages d’erreur pour faciliter le diagnostic.</param>
    /// <returns>Instance de <c>SpiderGoldEntry</c> initialisée avec la requête SQL et le <c>db_id</c> extraits.</returns>
    /// <exception cref="FormatException">Levée si la ligne ne contient pas de séparateur par tabulation entre la requête SQL et le <c>db_id</c>, ou si
    /// l’une des deux valeurs est vide après suppression des espaces.</exception>
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
