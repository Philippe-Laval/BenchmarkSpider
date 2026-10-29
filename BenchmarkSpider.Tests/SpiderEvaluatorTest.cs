namespace BenchmarkSpider.Tests
{
    [TestClass]
    public sealed class SpiderEvaluatorTest
    {
        [TestMethod]
        public void Evaluate_DevGoldAgainstItself_IsPerfectExactMatch()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);
            var examples = SpiderParser.Load(SpiderParser.DevJsonPath);

            var items = examples.Select(e => (e.Query, e.Query, e.DbId));
            var result = SpiderEvaluator.Evaluate(items, dbs);

            Assert.AreEqual(examples.Count, result.Scores["all"].Count);
            Assert.AreEqual(1.0, result.Scores["all"].Exact, 1e-9);
            Assert.AreEqual(0, result.EvalErrorCount);
        }

        [TestMethod]
        public void Evaluate_DifferentWhere_IsNotExactMatch()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);
            var db = dbs.Values.First(d => d.TableNamesOriginal.Count > 0);
            var table = db.TableNamesOriginal[0];
            var col = db.GetColumns(0).First().Name;

            var items = new[]
            {
                ($"SELECT {col} FROM {table}", $"SELECT {col} FROM {table} WHERE {col} = 1", db.DbId)
            };
            var result = SpiderEvaluator.Evaluate(items, dbs);

            Assert.AreEqual(0.0, result.Scores["all"].Exact, 1e-9);
            Assert.AreEqual("easy", result.Entries[0].Hardness);
        }

        [TestMethod]
        public void Evaluate_ExecutionAccuracy_ConcertSinger()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);
            var dbDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SpiderTablesLoader.TablesJsonPath)!, "database"));
            Assert.IsTrue(Directory.Exists(dbDir), dbDir);

            var items = new[]
            {
                ("SELECT count(*) FROM singer", "SELECT count(*) FROM singer", "concert_singer"),
                ("SELECT name FROM singer", "SELECT count(*) FROM singer", "concert_singer"),
            };
            var result = SpiderEvaluator.Evaluate(items, dbs, null, dbDir);

            Assert.AreEqual(0.5, result.Scores["all"].Exec, 1e-9);
        }

        [TestMethod]
        public void Evaluate_InvalidPrediction_CountsAsError()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);
            var db = dbs.Values.First(d => d.TableNamesOriginal.Count > 0);
            var table = db.TableNamesOriginal[0];

            var items = new[] { ("SELECT nope FROM nowhere", $"SELECT count(*) FROM {table}", db.DbId) };
            var result = SpiderEvaluator.Evaluate(items, dbs);

            Assert.AreEqual(1, result.EvalErrorCount);
            Assert.AreEqual(0.0, result.Scores["all"].Exact, 1e-9);
        }
    }
}
