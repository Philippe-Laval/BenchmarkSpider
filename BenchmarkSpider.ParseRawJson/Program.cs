using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkSpider.Library;
using BenchmarkSpider.Library.Parsers;

// Port of : https://github.com/taoyds/spider/blob/master/preprocess/parse_raw_json.py


namespace BenchmarkSpider.ParseRawJson
{
    internal class Program
    {
        /// <summary>
        /// Port de parse_raw_json.py : ajoute à chaque exemple du fichier SQL (train.json) le champ "sql"
        /// (SQL analysé) et écrit le résultat dans un fichier JSON trié et indenté.
        /// </summary>
        /// <param name="args">[sql_path] [table_file] [output_file], tous optionnels.</param>
        static int Main(string[] args)
        {
            var sqlPath = args.Length > 0 ? args[0] : SpiderParser.TrainSpiderJsonPath;
            var tableFile = args.Length > 1 ? args[1] : SpiderTablesParser.TablesJsonPath;
            var outputFile = args.Length > 2 ? args[2] : "dev_new.json";

            var dbs = SpiderTablesParser.LoadByDbId(tableFile);
            var schemas = dbs.ToDictionary(kv => kv.Key, kv => SqlSchema.GetSchemasFromJson(kv.Value));

            var sqlData = JsonNode.Parse(File.ReadAllText(sqlPath))!.AsArray();

            var serializerOptions = new JsonSerializerOptions { IncludeFields = true };
            var sqlDataNew = new JsonArray();
            foreach (var item in sqlData)
            {
                string? dbId = null;
                string? sql = null;
                try
                {
                    var data = item!.AsObject();
                    dbId = data["db_id"]!.GetValue<string>();
                    sql = data["query"]!.GetValue<string>();
                    var schema = schemas[dbId];
                    var sqlLabel = SqlParser.GetSql(schema, sql);
                    data["sql"] = JsonSerializer.SerializeToNode(sqlLabel, sqlLabel.GetType(), serializerOptions);
                    sqlDataNew.Add(data.DeepClone());
                }
                catch (Exception)
                {
                    Console.WriteLine("db_id: " + dbId);
                    Console.WriteLine("sql: " + sql);
                }
            }

            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(outputFile, SortKeys(sqlDataNew)!.ToJsonString(writeOptions));
            return 0;
        }

        /// <summary>Trie récursivement les clés des objets (équivalent de sort_keys=True).</summary>
        private static JsonNode? SortKeys(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var sorted = new JsonObject();
                    foreach (var kv in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                        sorted.Add(kv.Key, SortKeys(kv.Value?.DeepClone()));
                    return sorted;
                case JsonArray arr:
                    var array = new JsonArray();
                    foreach (var child in arr)
                        array.Add(SortKeys(child?.DeepClone()));
                    return array;
                default:
                    return node;
            }
        }
    }
}
