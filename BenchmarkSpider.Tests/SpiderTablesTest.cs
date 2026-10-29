namespace BenchmarkSpider.Tests
{
    [TestClass]
    public sealed class SpiderTablesTest
    {
        private const string Sample = """
        [
          {
            "db_id": "world_1",
            "table_names_original": ["city", "country"],
            "table_names": ["city", "country"],
            "column_names_original": [[-1, "*"], [0, "ID"], [0, "CountryCode"], [1, "Code"], [1, "Name"]],
            "column_names": [[-1, "*"], [0, "id"], [0, "country code"], [1, "code"], [1, "name"]],
            "column_types": ["text", "number", "text", "text", "text"],
            "foreign_keys": [[2, 3]],
            "primary_keys": [1, 3, [3, 4]]
          }
        ]
        """;

        private static List<SpiderDatabase> LoadSample()
        {
            var path = Path.Combine(Path.GetTempPath(), $"tables_{Guid.NewGuid():N}.json");
            File.WriteAllText(path, Sample);
            try { return SpiderTablesLoader.Load(path); }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void Load_Sample_MapsAllFields()
        {
            var db = LoadSample().Single();

            Assert.AreEqual("world_1", db.DbId);
            CollectionAssert.AreEqual(new[] { "city", "country" }, db.TableNamesOriginal);
            Assert.HasCount(5, db.ColumnNamesOriginal);
            Assert.AreEqual(new SpiderColumnName(-1, "*"), db.ColumnNamesOriginal[0]);
            Assert.AreEqual(new SpiderColumnName(0, "country code"), db.ColumnNames[2]);
            Assert.AreEqual("number", db.ColumnTypes[1]);
            Assert.AreEqual(new SpiderForeignKey(2, 3), db.ForeignKeys.Single());
        }

        [TestMethod]
        public void Load_Sample_HandlesSingleAndCompositePrimaryKeys()
        {
            var pks = LoadSample().Single().PrimaryKeys;

            Assert.HasCount(3, pks);
            CollectionAssert.AreEqual(new[] { 1 }, pks[0].ColumnIndexes.ToArray());
            CollectionAssert.AreEqual(new[] { 3, 4 }, pks[2].ColumnIndexes.ToArray());
        }

        [TestMethod]
        public void Helpers_ReturnColumnsAndQualifiedNames()
        {
            var db = LoadSample().Single();

            CollectionAssert.AreEqual(new[] { "ID", "CountryCode" }, db.GetColumns(0).Select(c => c.Name).ToArray());
            Assert.AreEqual("*", db.GetQualifiedColumnName(0));
            Assert.AreEqual("country.Code", db.GetQualifiedColumnName(3));
        }

        [TestMethod]
        public void Load_TablesJson_IsConsistent()
        {
            var dbs = SpiderTablesLoader.Load(SpiderTablesLoader.TablesJsonPath);

            Assert.HasCount(166, dbs);
            Assert.HasCount(dbs.Count, dbs.Select(d => d.DbId).Distinct().ToList());
            foreach (var db in dbs)
            {
                Assert.HasCount(db.TableNamesOriginal.Count, db.TableNames, db.DbId);
                Assert.HasCount(db.ColumnNamesOriginal.Count, db.ColumnNames, db.DbId);
                Assert.HasCount(db.ColumnNamesOriginal.Count, db.ColumnTypes, db.DbId);
                var max = db.ColumnNamesOriginal.Count;
                Assert.IsTrue(db.ColumnNamesOriginal.All(c => c.TableIndex < db.TableNamesOriginal.Count), db.DbId);
                Assert.IsTrue(db.ForeignKeys.All(f => f.ColumnIndex < max && f.ReferencedColumnIndex < max), db.DbId);
                Assert.IsTrue(db.PrimaryKeys.SelectMany(p => p.ColumnIndexes).All(i => i > 0 && i < max), db.DbId);
            }
        }

        [TestMethod]
        public void Load_TablesJson_CoversAllDbIdsOfExamples()
        {
            var known = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);
            var examples = SpiderParser.Load(SpiderParser.DevJsonPath)
                .Concat(SpiderParser.Load(SpiderParser.TrainSpiderJsonPath))
                .Concat(SpiderParser.Load(SpiderParser.TrainOthersJsonPath));

            var missing = examples.Select(e => e.DbId).Distinct().Where(id => !known.ContainsKey(id)).ToList();

            Assert.IsEmpty(missing, string.Join(", ", missing));
        }
    }
}
