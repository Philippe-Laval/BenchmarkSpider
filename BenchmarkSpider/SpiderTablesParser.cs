using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchmarkSpider;

/// <summary>A column entry of tables.json: [tableIndex, "name"]. tableIndex is -1 for the "*" column.</summary>
[JsonConverter(typeof(SpiderColumnNameConverter))]
public record SpiderColumnName(int TableIndex, string Name);

/// <summary>A primary key: a single column index, or several for a composite key.</summary>
[JsonConverter(typeof(SpiderPrimaryKeyConverter))]
public record SpiderPrimaryKey(IReadOnlyList<int> ColumnIndexes);

/// <summary>A foreign key: [columnIndex, referencedColumnIndex] (indices in column_names).</summary>
[JsonConverter(typeof(SpiderForeignKeyConverter))]
public record SpiderForeignKey(int ColumnIndex, int ReferencedColumnIndex);

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

public static class SpiderTablesLoader
{
    public static string TablesJsonPath => Path.Combine(SpiderParser.SpiderDirectory, "tables.json");

    public static List<SpiderDatabase> Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<SpiderDatabase>>(stream) ?? [];
    }

    public static async Task<List<SpiderDatabase>> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<SpiderDatabase>>(stream, cancellationToken: ct) ?? [];
    }

    public static Dictionary<string, SpiderDatabase> LoadByDbId(string path)
        => Load(path).ToDictionary(d => d.DbId);
}

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
