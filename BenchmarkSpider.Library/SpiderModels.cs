using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchmarkSpider.Library;

/// <summary>
/// Représente un exemple du jeu de données Spider associant une question en langage naturel à sa requête SQL,
/// ses jetons et sa structure SQL analysée.
/// Utilisé pour les fichiers JSON du jeu de données Spider, tels que dev.json, train_spider.json et train_others.json.
/// </summary>
/// <remarks>Utilisé comme modèle de sérialisation et de désérialisation JSON 
/// pour les annotations de questions SQL.</remarks>
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
/// Représente une requête SQL au format Spider, incluant les clauses principales et les opérations ensemblistes
/// imbriquées.
/// Parsed SQL (process_sql.py). Heterogeneous arrays (mixed bool/int/array/object) are kept as JsonElement.
/// </summary>
/// <remarks>Le type mappe directement la structure JSON source : plusieurs clauses sont conservées en
/// JsonElement, et les opérations INTERSECT, UNION et EXCEPT sont représentées récursivement par SpiderSql.</remarks>
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

/// <summary>
/// Représente la clause FROM d’une requête Spider, incluant les unités de table et les conditions associées.
/// </summary>
/// <remarks>Le modèle conserve les champs `table_units` et `conds` sous forme de `JsonElement` pour prendre en
/// charge la structure JSON hétérogène du schéma Spider.</remarks>
public class SpiderFrom
{
    /// <summary>[["table_unit", tableId] | ["sql", SpiderSql], ...]</summary>
    [JsonPropertyName("table_units")]
    public JsonElement TableUnits { get; set; }

    [JsonPropertyName("conds")]
    public JsonElement Conds { get; set; }
}
