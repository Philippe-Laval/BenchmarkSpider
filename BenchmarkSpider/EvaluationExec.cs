using System.Globalization;

namespace BenchmarkSpider;

public partial class SpiderEvaluator
{
    /// <summary>
    /// Crée une clé unique pour un élément de valeur en fonction de ses colonnes et de son opérateur.
    /// </summary>
    /// <param name="v">Élément de valeur pour lequel créer la clé.</param>
    /// <returns>Chaîne représentant la clé unique de l'élément de valeur.</returns>
    private static string ExecKey(ValUnit v)
    {
        /// <summary>Fonction locale pour encoder un ColUnit en chaîne.</summary>
        static string C(ColUnit c) => $"({c.AggId},{c.ColId},{c.IsDistinct})";

        return v.ColUnit2 is null ? C(v.ColUnit1) : $"{v.UnitOp}|{C(v.ColUnit1)}|{C(v.ColUnit2)}";
    }

    /// <summary>
    /// Encode une valeur de cellule en chaîne préfixée selon son type.
    /// </summary>
    /// <remarks>Le format numérique utilise la culture invariante et le format aller-retour `R` pour
    /// préserver la précision lors de la sérialisation.</remarks>
    /// <param name="value">Valeur à encoder. Les valeurs `DBNull` produisent `N`, les tableaux d’octets sont encodés en Base64 avec le
    /// préfixe `b:`, les chaînes avec le préfixe `s:`, et les autres valeurs sont converties en nombre `double`
    /// invariant avec le préfixe `n:`.</param>
    /// <returns>Chaîne encodée représentant la valeur et son type logique.</returns>
    private static string ExecCell(object value) => value switch
    {
        DBNull => "N",
        byte[] b => "b:" + Convert.ToBase64String(b),
        string s => "s:" + s,
        _ => "n:" + Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Exécute une requête SQL sur la connexion SQLite spécifiée et retourne les lignes lues sous forme de chaînes.
    /// </summary>
    /// <param name="conn">Connexion SQLite utilisée pour créer et exécuter la commande.</param>
    /// <param name="sql">Texte de la requête SQL à exécuter.</param>
    /// <returns>Liste de lignes, où chaque ligne contient les valeurs de colonnes converties en chaînes dans l’ordre de lecture.</returns>
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

    /// <summary>
    /// Crée un dictionnaire mappant les clés des colonnes aux valeurs correspondantes pour chaque ligne de résultat.
    /// </summary>
    /// <param name="res">Liste de lignes, où chaque ligne contient les valeurs de colonnes sous forme de chaînes.</param>
    /// <param name="items">Liste des éléments de sélection correspondant aux colonnes.</param>
    /// <returns>Dictionnaire mappant les clés des colonnes aux listes de valeurs.</returns>
    private static Dictionary<string, List<string>> ResMap(List<List<string>> res, IReadOnlyList<SelectItem> items)
    {
        var map = new Dictionary<string, List<string>>();
        for (var idx = 0; idx < items.Count; idx++)
            map[ExecKey(items[idx].ValUnit)] = res.Select(r => r[idx]).ToList();
        return map;
    }

    /// <summary>
    /// Évalue si la requête prédite et la requête de référence produisent des résultats équivalents sur la base SQLite fournie.
    /// Equivalent of eval_exec_match: true when the result columns of prediction and gold match
    /// </summary>
    /// <remarks>Retourne false si l’exécution de la requête prédite échoue.</remarks>
    /// <param name="dbPath">Chemin vers le fichier de base de données SQLite utilisé pour l’évaluation.</param>
    /// <param name="pStr">Texte SQL de la requête prédite à exécuter.</param>
    /// <param name="gStr">Texte SQL de la requête de référence à exécuter.</param>
    /// <param name="pred">Représentation SQL analysée de la requête prédite, utilisée pour mapper les colonnes du résultat.</param>
    /// <param name="gold">Représentation SQL analysée de la requête de référence, utilisée pour mapper les colonnes du résultat.</param>
    /// <returns>true si les résultats mappés ont le même nombre d’entrées et des séquences de valeurs identiques pour chaque clé
    /// ; sinon, false.</returns>
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