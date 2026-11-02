using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchmarkSpider.Library.Parsers;

/// <summary>A column entry of tables.json: [tableIndex, "name"]. tableIndex is -1 for the "*" column.</summary>
[JsonConverter(typeof(SpiderColumnNameConverter))]
public record SpiderColumnName(int TableIndex, string Name);

/// <summary>A primary key: a single column index, or several for a composite key.</summary>
[JsonConverter(typeof(SpiderPrimaryKeyConverter))]
public record SpiderPrimaryKey(IReadOnlyList<int> ColumnIndexes);

/// <summary>A foreign key: [columnIndex, referencedColumnIndex] (indices in column_names).</summary>
[JsonConverter(typeof(SpiderForeignKeyConverter))]
public record SpiderForeignKey(int ColumnIndex, int ReferencedColumnIndex);

/// <summary>
/// Représente le schéma d’une base de données Spider, incluant l’identifiant, les tables, les colonnes, leurs types et
/// les relations de clés primaires et étrangères.
/// Represente une entrée du fichier "tables.json" du jeu de données Spider.
/// </summary>
/// <remarks>Les index stockés dans les clés et les définitions de colonnes référencent les collections associées
/// afin de relier les tables et de reconstruire les noms de colonnes qualifiés.</remarks>
public class SpiderDatabase
{
    /// <summary>
    /// database id
    /// </summary>
    [JsonPropertyName("db_id")]
    public string DbId { get; set; } = string.Empty;

    /// <summary>
    /// original table names stored in the database.
    /// </summary>
    [JsonPropertyName("table_names_original")]
    public List<string> TableNamesOriginal { get; set; } = [];

    /// <summary>
    /// cleaned and normalized table names
    /// </summary>
    [JsonPropertyName("table_names")]
    public List<string> TableNames { get; set; } = [];

    /// <summary>
    /// original column names stored in the database.
    /// </summary>
    [JsonPropertyName("column_names_original")]
    public List<SpiderColumnName> ColumnNamesOriginal { get; set; } = [];

    /// <summary>
    /// cleaned and normalized column names
    /// </summary>
    [JsonPropertyName("column_names")]
    public List<SpiderColumnName> ColumnNames { get; set; } = [];

    /// <summary>
    /// data type of each column
    /// </summary>
    [JsonPropertyName("column_types")]
    public List<string> ColumnTypes { get; set; } = [];

    /// <summary>
    /// foreign keys in the database. [3, 8] means column indices in the column_names.
    /// These two columns are foreign keys of two different tables.
    /// </summary>
    [JsonPropertyName("foreign_keys")]
    public List<SpiderForeignKey> ForeignKeys { get; set; } = [];

    /// <summary>
    /// primary keys in the database. Each number is the index of column_names.
    /// </summary>
    [JsonPropertyName("primary_keys")]
    public List<SpiderPrimaryKey> PrimaryKeys { get; set; } = [];

    /// <summary>Original columns of the given table (by index in table_names), without the "*" column.</summary>
    public IEnumerable<SpiderColumnName> GetColumns(int tableIndex)
        => ColumnNamesOriginal.Where(c => c.TableIndex == tableIndex);

    /// <summary>"table.column" for a column index, or "*" for the wildcard column.</summary>
    public string GetQualifiedColumnName(int columnIndex)
    {
        var column = ColumnNamesOriginal[columnIndex];
        return column.TableIndex < 0
            ? column.Name
            : $"{TableNamesOriginal[column.TableIndex]}.{column.Name}";
    }
}

/// <summary>
/// Fournit des méthodes utilitaires pour charger les métadonnées des bases Spider 
/// à partir d’un fichier JSON "tables.json".
/// </summary>
/// <remarks>Inclut le chargement synchrone et asynchrone, ainsi qu’une projection en dictionnaire indexé par
/// identifiant de base de données.</remarks>
public static class SpiderTablesParser
{
    public static string TablesJsonPath => Path.Combine(SpiderParser.SpiderDirectory, "tables.json");

    /// <summary>
    /// Charge une liste de bases de données SPIDER depuis un fichier JSON (tables.json).
    /// </summary>
    /// <param name="path">Chemin du fichier JSON à lire "tables.json".</param>
    /// <returns>Liste des bases de données désérialisées à partir du fichier, ou une liste vide si le contenu est nul.</returns>
    public static List<SpiderDatabase> Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<SpiderDatabase>>(stream) ?? [];
    }

    /// <summary>
    /// Charge de manière asynchrone une liste de bases de données SPIDER depuis un fichier JSON (tables.json).
    /// </summary>
    /// <param name="path">Chemin du fichier JSON à lire "tables.json".</param>
    /// <param name="ct">Jeton permettant d’annuler l’opération de lecture et de désérialisation.</param>
    /// <returns>Une tâche qui représente l’opération asynchrone et dont le résultat est la liste des objets
    /// <c>SpiderDatabase</c> désérialisés, ou une liste vide si le contenu JSON est nul.</returns>
    public static async Task<List<SpiderDatabase>> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<SpiderDatabase>>(stream, cancellationToken: ct) ?? [];
    }

    /// <summary>
    /// Charge les définitions de bases de données Spider depuis le chemin spécifié 
    /// et les indexe par identifiant de base de données.
    /// </summary>
    /// <remarks>L’opération échoue si plusieurs éléments chargés partagent le même <c>DbId</c>.</remarks>
    /// <param name="path">Chemin du fichier source à charger.</param>
    /// <returns>Dictionnaire des bases de données chargées, où la clé est la valeur de <c>DbId</c> et la valeur est l'instance
    /// <c>SpiderDatabase</c> correspondante.</returns>
    public static Dictionary<string, SpiderDatabase> LoadByDbId(string path)
        => Load(path).ToDictionary(d => d.DbId);

    public static bool CompareFiles(string path1, string path2)
    {
        var db1 = Load(path1);
        var db2 = Load(path2);

        if (db1.Count != db2.Count)
            return false;

        var ids1 = db1.Select(d => d.DbId).ToHashSet();
        var ids2 = db2.Select(d => d.DbId).ToHashSet();
        if (ids1.Count != db1.Count || ids2.Count != db2.Count || !ids1.SetEquals(ids2))
            return false;

        var byId2 = db2.ToDictionary(d => d.DbId);
        return db1.All(d => AreEqual(d, byId2[d.DbId]));
    }

    private static bool AreEqual(SpiderDatabase a, SpiderDatabase b)
        => a.TableNamesOriginal.SequenceEqual(b.TableNamesOriginal)
        && a.TableNames.SequenceEqual(b.TableNames)
        && a.ColumnNamesOriginal.SequenceEqual(b.ColumnNamesOriginal)
        && a.ColumnNames.SequenceEqual(b.ColumnNames)
        && a.ColumnTypes.SequenceEqual(b.ColumnTypes)
        && a.ForeignKeys.SequenceEqual(b.ForeignKeys)
        && a.PrimaryKeys.Count == b.PrimaryKeys.Count
        && a.PrimaryKeys.Zip(b.PrimaryKeys).All(p => p.First.ColumnIndexes.SequenceEqual(p.Second.ColumnIndexes));
}

/// <summary>
/// Convertisseur JSON pour SpiderColumnName, sérialisé sous forme de tableau JSON `[tableIndex, name]`.
/// </summary>
/// <remarks>Lit et écrit uniquement le format de tableau à deux éléments, avec l’index de table en premier et le
/// nom de colonne en second.</remarks>
internal sealed class SpiderColumnNameConverter : JsonConverter<SpiderColumnName>
{
    public override SpiderColumnName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected [tableIndex, name].");
        reader.Read();
        var table = reader.GetInt32();
        reader.Read();
        var name = reader.GetString() ?? string.Empty;
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Expected end of column array.");
        return new SpiderColumnName(table, name);
    }

    public override void Write(Utf8JsonWriter writer, SpiderColumnName value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.TableIndex);
        writer.WriteStringValue(value.Name);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Convertisseur JSON pour <see cref="SpiderForeignKey"/> utilisant une représentation en tableau à deux entiers.
/// </summary>
/// <remarks>Lit une clé étrangère depuis le format <c>[columnIndex, referencedColumnIndex]</c> et lève une <see
/// cref="JsonException"/> si le tableau ne contient pas exactement deux valeurs. Écrit la clé étrangère dans le même
/// format.</remarks>
internal sealed class SpiderForeignKeyConverter : JsonConverter<SpiderForeignKey>
{
    public override SpiderForeignKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<int[]>(ref reader, options);
        if (values is not { Length: 2 })
            throw new JsonException("Expected [columnIndex, referencedColumnIndex].");
        return new SpiderForeignKey(values[0], values[1]);
    }

    public override void Write(Utf8JsonWriter writer, SpiderForeignKey value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new[] { value.ColumnIndex, value.ReferencedColumnIndex }, options);
}

/// <summary>
/// Convertisseur JSON pour <see cref="SpiderPrimaryKey"/> utilisant une représentation en entier ou tableau d'entiers.
/// </summary>
/// <remarks>Lit une clé primaire depuis le format <c>number</c> ou <c>[number, ...]</c> et lève une <see cref="JsonException"/> si le format est invalide. Écrit la clé primaire dans le même format.</remarks>
internal sealed class SpiderPrimaryKeyConverter : JsonConverter<SpiderPrimaryKey>
{
    public override SpiderPrimaryKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new SpiderPrimaryKey([reader.GetInt32()]);

        var values = JsonSerializer.Deserialize<int[]>(ref reader, options) ?? [];
        return new SpiderPrimaryKey(values);
    }

    public override void Write(Utf8JsonWriter writer, SpiderPrimaryKey value, JsonSerializerOptions options)
    {
        if (value.ColumnIndexes.Count == 1)
            writer.WriteNumberValue(value.ColumnIndexes[0]);
        else
            JsonSerializer.Serialize(writer, value.ColumnIndexes, options);
    }
}
