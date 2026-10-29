using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchmarkSpider;

public class SpiderExample
{
    /// <summary>
    /// The database id to which this question is addressed
    /// </summary>
    [JsonPropertyName("db_id")]
    public string DbId { get; set; } = string.Empty;

    /// <summary>
    /// The SQL query corresponding to the question.
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// The SQL query tokens corresponding to the question.
    /// </summary>
    [JsonPropertyName("query_toks")]
    public List<string> QueryToks { get; set; } = [];

    /// <summary>
    /// The SQL query tokens corresponding to the question without the value.
    /// </summary>
    [JsonPropertyName("query_toks_no_value")]
    public List<string>? QueryToksNoValue { get; set; }

    /// <summary>
    /// The natural language question
    /// </summary>
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// The natural language question tokens
    /// </summary>
    [JsonPropertyName("question_toks")]
    public List<string> QuestionToks { get; set; } = [];

    /// <summary>
    /// Parsed results of this SQL query using process_sql.py. 
    /// Please refer to parsed_sql_examples.sql in thepreprocess directory for the detailed documentation.
    /// </summary>
    [JsonPropertyName("sql")]
    public SpiderSql? Sql { get; set; }
}

/// <summary>
/// Parsed SQL (process_sql.py). Heterogeneous arrays (mixed bool/int/array/object) are kept as JsonElement.
/// </summary>
public class SpiderSql
{
    [JsonPropertyName("from")]
    public SpiderFrom? From { get; set; }

    /// <summary>[isDistinct, [[aggId, valUnit], ...]]</summary>
    [JsonPropertyName("select")]
    public JsonElement Select { get; set; }

    [JsonPropertyName("where")]
    public JsonElement Where { get; set; }

    [JsonPropertyName("groupBy")]
    public JsonElement GroupBy { get; set; }

    [JsonPropertyName("having")]
    public JsonElement Having { get; set; }

    /// <summary>["asc"|"desc", [valUnit, ...]]</summary>
    [JsonPropertyName("orderBy")]
    public JsonElement OrderBy { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("intersect")]
    public SpiderSql? Intersect { get; set; }

    [JsonPropertyName("union")]
    public SpiderSql? Union { get; set; }

    [JsonPropertyName("except")]
    public SpiderSql? Except { get; set; }
}

public class SpiderFrom
{
    /// <summary>[["table_unit", tableId] | ["sql", SpiderSql], ...]</summary>
    [JsonPropertyName("table_units")]
    public JsonElement TableUnits { get; set; }

    [JsonPropertyName("conds")]
    public JsonElement Conds { get; set; }
}

public static class SpiderLoader
{
    public const string DevJsonPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider\spider\dev.json";
    public const string TrainSpiderJsonPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider\spider\train_spider.json";
    public const string TrainOthersJsonPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider\spider\train_others.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static string SpiderDirectory { get; } = Path.GetDirectoryName(DevJsonPath)!;

    public static List<SpiderExample> Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<SpiderExample>>(stream, Options) ?? [];
    }

    public static async Task<List<SpiderExample>> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<SpiderExample>>(stream, Options, ct) ?? [];
    }
}
