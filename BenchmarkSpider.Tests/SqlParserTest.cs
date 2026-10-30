using System.Text.Json;
using System.Text.Json.Nodes;

namespace BenchmarkSpider.Tests
{
    [TestClass]
    public sealed class SqlParserTest
    {
        private static SqlSchema Schema() => new(new Dictionary<string, List<string>>
        {
            ["singer"] = ["singer_id", "name", "age", "country"],
            ["concert"] = ["concert_id", "singer_id", "year"]
        });

        [TestMethod]
        public void Tokenize_KeepsStringsAndMergesOperators()
        {
            var toks = SqlParser.Tokenize("SELECT count(*) FROM singer WHERE age>=18 AND name != 'Joe Doe' ;");

            CollectionAssert.AreEqual(
                new[] { "select", "count", "(", "*", ")", "from", "singer", "where", "age", ">=", "18", "and", "name", "!=", "\"Joe Doe\"", ";" },
                toks);
        }

        [TestMethod]
        public void Tokenize_KeepsMinusInNumbers()
        {
            var toks = SqlParser.Tokenize("SELECT name FROM airports WHERE elevation BETWEEN -50 AND 50");

            CollectionAssert.AreEqual(
                new[] { "select", "name", "from", "airports", "where", "elevation", "between", "-50", "and", "50" },
                toks);
        }

        [TestMethod]
        public void Tokenize_MinusAfterOperandIsSubtraction()
        {
            CollectionAssert.AreEqual(
                new[] { "a", "-", "1" },
                SqlParser.Tokenize("a -1"));

            CollectionAssert.AreEqual(
                new[] { "select", "age", "-", "1", "from", "singer" },
                SqlParser.Tokenize("SELECT age -1 FROM singer"));

            CollectionAssert.AreEqual(
                new[] { "(", "a", ")", "-", "1" },
                SqlParser.Tokenize("(a) -1"));
        }

        [TestMethod]
        public void GetSql_SimpleSelectWithWhereOrderLimit()
        {
            ParsedSql sql = SqlParser.GetSql(Schema(), "SELECT name FROM singer WHERE age > 30 ORDER BY age DESC LIMIT 3");

            Assert.HasCount(1, sql.Select.Items);
            Assert.AreEqual("__singer.name__", sql.Select.Items[0].ValUnit.ColUnit1.ColId);
            Assert.AreEqual("__singer__", sql.From.TableUnits.Single().TableId);
            var cond = (CondUnit)sql.Where.Single();
            Assert.AreEqual(Array.IndexOf(SqlParser.WhereOps, ">"), cond.OpId);
            Assert.AreEqual(30d, ((NumberValue?)cond.Val1)?.Value);
            Assert.AreEqual("desc", sql.OrderBy!.OrderType);
            Assert.AreEqual(3, sql.Limit);
        }

        [TestMethod]
        public void GetSql_JoinWithAliasAndGroupBy()
        {
            var sql = SqlParser.GetSql(Schema(),
                "SELECT T1.name , count(*) FROM singer AS T1 JOIN concert AS T2 ON T1.singer_id = T2.singer_id GROUP BY T1.name");

            Assert.HasCount(2, sql.From.TableUnits);
            Assert.HasCount(1, sql.From.Conds);
            Assert.AreEqual("__singer.name__", sql.GroupBy.Single().ColId);
            Assert.AreEqual(Array.IndexOf(SqlParser.AggOps, "count"), sql.Select.Items[1].AggId);
        }

        [TestMethod]
        public void GetSql_SubQueryAndSetOperation()
        {
            var sql = SqlParser.GetSql(Schema(),
                "SELECT name FROM singer WHERE singer_id NOT IN (SELECT singer_id FROM concert) UNION SELECT name FROM singer");

            var cond = (CondUnit)sql.Where.Single();
            Assert.IsTrue(cond.NotOp);
            Assert.IsInstanceOfType<SubQueryValue>(cond.Val1);
            Assert.IsNotNull(sql.Union);
            Assert.IsNull(sql.Intersect);
        }

        [TestMethod]
        public void GetSql_UnknownColumn_Throws()
        {
            Assert.ThrowsExactly<SqlParseException>(() => SqlParser.GetSql(Schema(), "SELECT nope FROM singer"));
        }

        [TestMethod]
        public void GetSql_UnbalancedQuote_Throws()
        {
            Assert.ThrowsExactly<SqlParseException>(() => SqlParser.GetSql(Schema(), "SELECT name FROM singer WHERE name = \"a"));
        }

        // ---- comparison against the "sql" field produced by Spider in dev.json ----

        [TestMethod]
        public void GetSql_DevJson_MatchesSpiderParsedSql()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);
            List<SpiderExample> examples = SpiderParser.Load(SpiderParser.DevJsonPath);
            var failures = new List<string>();

            foreach (var example in examples)
            {
                try
                {
                    var db = dbs[example.DbId];
                    var parsed = SqlParser.GetSql(SqlSchema.FromDatabase(db), example.Query);
                    var actual = JsonSerializer.SerializeToElement(ToJson(parsed, IdResolver(db)));
                    var expected = JsonSerializer.SerializeToElement(example.Sql);
                    if (!expected.SameJson(actual))
                        failures.Add($"{example.DbId}: {example.Query}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{example.DbId}: {example.Query} -> {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.IsLessThanOrEqualTo(10, failures.Count,
                $"{failures.Count}/{examples.Count} mismatches:\n" + string.Join("\n", failures.Take(15)));
        }

        [TestMethod]
        public void GetSql_TrainSpiderJson_MatchesSpiderParsedSql()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);
            List<SpiderExample> examples = SpiderParser.Load(SpiderParser.TrainSpiderJsonPath);
            var failures = new List<string>();

            foreach (var example in examples)
            {
                try
                {
                    var db = dbs[example.DbId];
                    var parsed = SqlParser.GetSql(SqlSchema.FromDatabase(db), example.Query);
                    var actual = JsonSerializer.SerializeToElement(ToJson(parsed, IdResolver(db)));
                    var expected = JsonSerializer.SerializeToElement(example.Sql);
                    if (!expected.SameJson(actual))
                        failures.Add($"{example.DbId}: {example.Query}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{example.DbId}: {example.Query} -> {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.IsLessThanOrEqualTo(10, failures.Count,
                $"{failures.Count}/{examples.Count} mismatches:\n" + string.Join("\n", failures.Take(15)));
        }

        [TestMethod]
        public void GetSql_TrainOthersJson_MatchesSpiderParsedSql()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);
            List<SpiderExample> examples = SpiderParser.Load(SpiderParser.TrainOthersJsonPath);
            var failures = new List<string>();

            foreach (var example in examples)
            {
                try
                {
                    var db = dbs[example.DbId];
                    var parsed = SqlParser.GetSql(SqlSchema.FromDatabase(db), example.Query);
                    var actual = JsonSerializer.SerializeToElement(ToJson(parsed, IdResolver(db)));
                    var expected = JsonSerializer.SerializeToElement(example.Sql);
                    if (!expected.SameJson(actual))
                        failures.Add($"{example.DbId}: {example.Query}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{example.DbId}: {example.Query} -> {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.IsLessThanOrEqualTo(10, failures.Count,
                $"{failures.Count}/{examples.Count} mismatches:\n" + string.Join("\n", failures.Take(15)));
        }


        private static Func<string, int> IdResolver(SpiderDatabase db)
        {
            var map = new Dictionary<string, int> { ["__all__"] = 0 };
            for (var i = 0; i < db.TableNamesOriginal.Count; i++)
                map["__" + db.TableNamesOriginal[i].ToLowerInvariant() + "__"] = i;
            for (var i = 0; i < db.ColumnNamesOriginal.Count; i++)
            {
                var c = db.ColumnNamesOriginal[i];
                if (c.TableIndex >= 0)
                    map["__" + db.TableNamesOriginal[c.TableIndex].ToLowerInvariant() + "." + c.Name.ToLowerInvariant() + "__"] = i;
            }
            return id => map[id];
        }

        private static JsonNode? ToJson(ParsedSql sql, Func<string, int> id)
        {
            JsonNode? ColUnitJson(ColUnit c) => new JsonArray(c.AggId, id(c.ColId), c.IsDistinct);
            JsonNode ValUnitJson(ValUnit v) => new JsonArray(v.UnitOp, ColUnitJson(v.ColUnit1), v.ColUnit2 is null ? null : ColUnitJson(v.ColUnit2));
            JsonNode? ValueJson(SqlValue? v) => v switch
            {
                null => null,
                NumberValue n => JsonValue.Create(n.Value),
                StringValue s => JsonValue.Create(s.Value),
                SubQueryValue q => ToJson(q.Sql, id),
                ColumnValue c => ColUnitJson(c.ColUnit),
                _ => throw new NotSupportedException()
            };
            JsonArray CondJson(IReadOnlyList<ConditionItem> conds)
            {
                var arr = new JsonArray();
                foreach (var item in conds)
                {
                    if (item is CondUnit cu)
                        arr.Add(new JsonArray(cu.NotOp, cu.OpId, ValUnitJson(cu.ValUnit), ValueJson(cu.Val1), ValueJson(cu.Val2)));
                    else
                        arr.Add(((ConditionConnector)item).Operator);
                }
                return arr;
            }

            var tableUnits = new JsonArray();
            foreach (var t in sql.From.TableUnits)
                tableUnits.Add(t.SubQuery is null
                    ? new JsonArray(t.TableType, id(t.TableId!))
                    : new JsonArray(t.TableType, ToJson(t.SubQuery, id)));

            var select = new JsonArray();
            var items = new JsonArray();
            foreach (var s in sql.Select.Items)
                items.Add(new JsonArray(s.AggId, ValUnitJson(s.ValUnit)));
            select.Add(sql.Select.IsDistinct);
            select.Add(items);

            var groupBy = new JsonArray();
            foreach (var g in sql.GroupBy)
                groupBy.Add(ColUnitJson(g));

            JsonNode orderBy = new JsonArray();
            if (sql.OrderBy is not null)
            {
                var units = new JsonArray();
                foreach (var v in sql.OrderBy.ValUnits)
                    units.Add(ValUnitJson(v));
                orderBy = new JsonArray(sql.OrderBy.OrderType, units);
            }

            return new JsonObject
            {
                ["from"] = new JsonObject { ["table_units"] = tableUnits, ["conds"] = CondJson(sql.From.Conds) },
                ["select"] = select,
                ["where"] = CondJson(sql.Where),
                ["groupBy"] = groupBy,
                ["having"] = CondJson(sql.Having),
                ["orderBy"] = orderBy,
                ["limit"] = sql.Limit,
                ["intersect"] = sql.Intersect is null ? null : ToJson(sql.Intersect, id),
                ["union"] = sql.Union is null ? null : ToJson(sql.Union, id),
                ["except"] = sql.Except is null ? null : ToJson(sql.Except, id)
            };
        }

        // Round trip through SpiderSql turns missing members into JsonValueKind.Undefined; nothing to normalise
        // beyond that, so this is the identity (kept as a seam for future differences).
        private static JsonElement Normalize(JsonElement e) => e;


        [TestMethod]
        public void TestGetSql_DevGold()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);

            List<SpiderGoldEntry> goldList = SpiderGoldParser.Load(Path.Combine(SpiderParser.SpiderDirectory, "dev_gold.sql"));
            foreach (var gold in goldList)
            {
                var db = dbs[gold.DbId];
                var parsed = SqlParser.GetSql(SqlSchema.FromDatabase(db), gold.Sql);
                Assert.IsNotNull(parsed, $"Failed to parse: {gold.Sql} for db {gold.DbId}");
            }
        }

        [TestMethod]
        public void TestGetSql_TrainGold()
        {
            var dbs = SpiderTablesLoader.LoadByDbId(SpiderTablesLoader.TablesJsonPath);

            List<SpiderGoldEntry> goldList = SpiderGoldParser.Load(Path.Combine(SpiderParser.SpiderDirectory, "train_gold.sql"));
            var failures = new List<string>();

            foreach (var gold in goldList)
            {
                try
                {
                    var db = dbs[gold.DbId];
                    var parsed = SqlParser.GetSql(SqlSchema.FromDatabase(db), gold.Sql);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Unknown table: ref_company_types"))
                        continue; // known issue with Spider train_gold.sql

                    failures.Add($"{gold.DbId}: {gold.Sql} -> {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (failures.Count > 0)
            {
                Assert.Fail($"Failures:\n{string.Join("\n", failures)}");
            }
        }

    }
}
