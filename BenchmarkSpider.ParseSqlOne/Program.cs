using System.Text.Json;
using BenchmarkSpider.Library;
using BenchmarkSpider.Library.Parsers;

// Port of : https://github.com/taoyds/spider/blob/master/preprocess/parse_sql_one.py

namespace BenchmarkSpider.ParseSqlOne
{
    internal class Program
    {
        static int Main(string[] args)
        {
            var sql = args.Length > 0 ? args[0] : "SELECT name ,  country ,  age FROM singer ORDER BY age DESC";
            var dbId = args.Length > 1 ? args[1] : "concert_singer";
            var tableFile = args.Length > 2 ? args[2] : SpiderTablesParser.TablesJsonPath;

            var dbs = SpiderTablesParser.LoadByDbId(tableFile);
            if (!dbs.TryGetValue(dbId, out SpiderDatabase? db))
            {
                Console.Error.WriteLine($"Unknown db_id: {dbId}");
                return 2;
            }

            SqlSchema schema = SqlSchema.GetSchemasFromJson(db);
            ParsedSql sqlLabel = SqlParser.GetSql(schema, sql);

            var options = new JsonSerializerOptions {
                WriteIndented = true, 
                IncludeFields = true
            };
            Console.WriteLine(JsonSerializer.Serialize(sqlLabel, sqlLabel.GetType(), options));
            return 0;
        }
    }
}
