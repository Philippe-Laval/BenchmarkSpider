using System.Text.Json;

namespace BenchmarkSpider.Tests
{
    [TestClass]
    public sealed class SpiderModelTest
    {
        private const string SampleJson = """
        [
          {
            "db_id": "concert_singer",
            "query": "SELECT count(*) FROM singer",
            "query_toks": ["SELECT", "count", "(", "*", ")", "FROM", "singer"],
            "query_toks_no_value": ["select", "count", "(", "*", ")", "from", "singer"],
            "question": "How many singers do we have?",
            "question_toks": ["How", "many", "singers", "do", "we", "have", "?"],
            "sql": {
              "from": { "table_units": [["table_unit", 1]], "conds": [] },
              "select": [false, [[3, [0, [0, 0, false], null]]]],
              "where": [],
              "groupBy": [],
              "having": [],
              "orderBy": [],
              "limit": 5,
              "intersect": null,
              "union": null,
              "except": {
                "from": { "table_units": [["table_unit", 2]], "conds": [] },
                "select": [false, []],
                "where": [],
                "groupBy": [],
                "having": [],
                "orderBy": [],
                "limit": null,
                "intersect": null,
                "union": null,
                "except": null
              }
            }
          }
        ]
        """;

        private static List<SpiderExample> LoadFromString(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), $"spider_{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json);
            try
            {
                return SpiderLoader.Load(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Load_SampleJson_MapsAllFields()
        {
            var examples = LoadFromString(SampleJson);

            Assert.HasCount(1, examples);
            var e = examples[0];
            Assert.AreEqual("concert_singer", e.DbId);
            Assert.AreEqual("SELECT count(*) FROM singer", e.Query);
            Assert.HasCount(7, e.QueryToks);
            Assert.IsNotNull(e.QueryToksNoValue);
            Assert.AreEqual("select", e.QueryToksNoValue[0]);
            Assert.AreEqual("How many singers do we have?", e.Question);
            Assert.HasCount(7, e.QuestionToks);
            Assert.AreEqual("?", e.QuestionToks[^1]);
        }

        [TestMethod]
        public void Load_SampleJson_MapsParsedSql()
        {
            var sql = LoadFromString(SampleJson)[0].Sql;

            Assert.IsNotNull(sql);
            Assert.IsNotNull(sql.From);
            Assert.AreEqual(JsonValueKind.Array, sql.From.TableUnits.ValueKind);
            Assert.AreEqual("table_unit", sql.From.TableUnits[0][0].GetString());
            Assert.AreEqual(1, sql.From.TableUnits[0][1].GetInt32());
            Assert.IsFalse(sql.Select[0].GetBoolean());
            Assert.AreEqual(3, sql.Select[1][0][0].GetInt32());
            Assert.AreEqual(5, sql.Limit);
            Assert.IsNull(sql.Intersect);
            Assert.IsNull(sql.Union);
        }

        [TestMethod]
        public void Load_SampleJson_MapsNestedSqlAndNullLimit()
        {
            var sql = LoadFromString(SampleJson)[0].Sql;

            Assert.IsNotNull(sql?.Except);
            Assert.IsNull(sql.Except.Limit);
            Assert.AreEqual(2, sql.Except.From!.TableUnits[0][1].GetInt32());
        }

        [TestMethod]
        public void Load_EmptyArray_ReturnsEmptyList()
        {
            Assert.IsEmpty(LoadFromString("[]"));
        }

        [TestMethod]
        public void Load_MissingOptionalFields_DoesNotThrow()
        {
            var examples = LoadFromString("""[{ "db_id": "x", "query": "q", "question": "?" }]""");

            Assert.HasCount(1, examples);
            Assert.IsNull(examples[0].QueryToksNoValue);
            Assert.IsNull(examples[0].Sql);
        }

        [TestMethod]
        public void Load_InvalidJson_Throws()
        {
            Assert.ThrowsExactly<JsonException>(() => LoadFromString("{ not json"));
        }

        [TestMethod]
        public async Task LoadAsync_SampleJson_MatchesSyncLoad()
        {
            var path = Path.Combine(Path.GetTempPath(), $"spider_{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(path, SampleJson);
            try
            {
                var examples = await SpiderLoader.LoadAsync(path);
                Assert.HasCount(1, examples);
                Assert.AreEqual("concert_singer", examples[0].DbId);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static void AssertDatasetIsValid(List<SpiderExample> examples)
        {
            Assert.IsNotEmpty(examples);
            foreach (var e in examples)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.DbId));
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.Query));
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.Question));
                Assert.IsNotEmpty(e.QueryToks, e.Query);
                Assert.IsNotEmpty(e.QuestionToks, e.Question);
                Assert.IsNotNull(e.Sql, e.Query);
                Assert.IsNotNull(e.Sql.From, e.Query);
                Assert.AreEqual(JsonValueKind.Array, e.Sql.Select.ValueKind, e.Query);
            }
        }

        [TestMethod]
        public void TestLoad_DevJson()
        {
            var examples = SpiderLoader.Load(SpiderLoader.DevJsonPath);

            // Il y a 1034 exemples dans dev.json.
            Assert.HasCount(1034, examples);
            AssertDatasetIsValid(examples);
            Assert.AreEqual("concert_singer", examples[0].DbId);
            Assert.AreEqual("SELECT count(*) FROM singer", examples[0].Query);
            Assert.AreEqual("How many singers do we have?", examples[0].Question);
        }

        [TestMethod]
        public void TestLoad_TrainSpiderJson()
        {
            var examples = SpiderLoader.Load(SpiderLoader.TrainSpiderJsonPath);

            // Il y a 7000 exemples dans train_spider.json, mais certains sont des doublons de train_others.json.
            Assert.HasCount(7000, examples);
            AssertDatasetIsValid(examples);
        }

        [TestMethod]
        public void TestLoad_TrainOthersJson()
        {
            var examples = SpiderLoader.Load(SpiderLoader.TrainOthersJsonPath);

            // Il y a 1659 exemples dans train_others.json, mais certains sont des doublons de train_spider.json.
            // Le nombre exact d'exemples uniques est donc inférieur à 1659.
            Assert.HasCount(1659, examples);
            AssertDatasetIsValid(examples);
        }

        [TestMethod]
        public void TestLoad_DevJson_QueriesMatchGoldSql()
        {
            var examples = SpiderLoader.Load(SpiderLoader.DevJsonPath);
            var gold = File.ReadAllLines(Path.Combine(SpiderLoader.SpiderDirectory, "dev_gold.sql"))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split('\t')[0].Trim())
                .ToList();

            Assert.HasCount(examples.Count, gold);

            for (var i = 0; i < examples.Count; i++)
            {
                Assert.AreEqual(
                    string.Join(' ', gold[i].Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                    string.Join(' ', examples[i].Query.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                    $"Index {i}");
            }
        }
    }
}
