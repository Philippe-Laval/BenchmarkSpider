namespace BenchmarkSpider.Tests
{
    [TestClass]
    public sealed class SpiderGoldParserTest
    {
        [TestMethod]
        public void ParseLine_SplitsSqlAndDbId()
        {
            var e = SpiderGoldParser.ParseLine("SELECT count(*) FROM singer\tconcert_singer");

            Assert.AreEqual("SELECT count(*) FROM singer", e.Sql);
            Assert.AreEqual("concert_singer", e.DbId);
        }

        [TestMethod]
        public void Parse_SkipsBlankLines()
        {
            var entries = SpiderGoldParser.Parse(["SELECT 1\tdb1", "", "  ", "SELECT 2\tdb2"]);

            Assert.HasCount(2, entries);
            Assert.AreEqual("db2", entries[1].DbId);
        }

        [TestMethod]
        public void ParseLine_NoTab_Throws()
        {
            Assert.ThrowsExactly<FormatException>(() => SpiderGoldParser.ParseLine("SELECT 1"));
        }

        [TestMethod]
        public void Load_DevGold_MatchesDevJson()
        {
            var gold = SpiderGoldParser.Load(Path.Combine(SpiderParser.SpiderDirectory, "dev_gold.sql"));
            var examples = SpiderParser.Load(SpiderParser.DevJsonPath);

            Assert.HasCount(examples.Count, gold);
            for (var i = 0; i < gold.Count; i++)
                Assert.AreEqual(examples[i].DbId, gold[i].DbId, $"Index {i}");
        }

        [TestMethod]
        public void MatchGold_IgnoresCaseWhitespaceAndSemicolon()
        {
            var examples = new List<SpiderExample>
            {
                new() { DbId = "db1", Query = "select  a FROM t;" },
                new() { DbId = "db1", Query = "SELECT missing" }
            };
            var gold = new List<SpiderGoldEntry> { new("SELECT a FROM t", "db1") };

            var matches = SpiderGoldParser.MatchGold(examples, gold);

            Assert.HasCount(2, matches);
            Assert.AreSame(gold[0], matches[0].Gold);
            Assert.IsNull(matches[1].Gold);
        }

        [TestMethod]
        public void MatchGold_TrainFiles_EveryExampleHasGold()
        {
            var examples = SpiderParser.Load(SpiderParser.TrainSpiderJsonPath)
                .Concat(SpiderParser.Load(SpiderParser.TrainOthersJsonPath))
                .ToList();

            var matches = SpiderGoldParser.MatchGold(examples, Path.Combine(SpiderParser.SpiderDirectory, "train_gold.sql"));

            Assert.HasCount(8659, matches);
            Assert.IsEmpty(matches.Where(m => m.Gold is null).Take(5).Select(m => m.Example.Query).ToList());
        }

        [TestMethod]
        public void Load_TrainGold_ParsesAllLines()
        {
            var gold = SpiderGoldParser.Load(Path.Combine(SpiderParser.SpiderDirectory, "train_gold.sql"));

            Assert.HasCount(8659, gold);
            Assert.IsTrue(gold.All(g => g.Sql.Length > 0 && g.DbId.Length > 0));
        }
    }
}
