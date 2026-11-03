using BenchmarkBird.Library.Parsers;

namespace BenchmarkBird.Library.Tests.Parsers
{
    [TestClass]
    public sealed class BirdParserTest
    {
        private const string SampleJson = """
            [
              {
                "question_id": 1,
                "db_id": "db1",
                "question": "How many?",
                "evidence": "some evidence",
                "SQL": "SELECT COUNT(*) FROM t",
                "difficulty": "simple"
              },
              {
                "question_id": "2",
                "db_id": "db2",
                "question": "Which?",
                "evidence": "",
                "SQL": "SELECT 1",
                "difficulty": "challenging"
              }
            ]
            """;

        private static string WriteTempFile(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public void Load_ValidJson_ReturnsExamples()
        {
            var path = WriteTempFile(SampleJson);
            try
            {
                var result = BirdParser.Load(path);

                Assert.HasCount(2, result);
                Assert.AreEqual(1, result[0].QuestionId);
                Assert.AreEqual("db1", result[0].DbId);
                Assert.AreEqual("How many?", result[0].Question);
                Assert.AreEqual("some evidence", result[0].Evidence);
                Assert.AreEqual("SELECT COUNT(*) FROM t", result[0].Sql);
                Assert.AreEqual("simple", result[0].Difficulty);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Load_NumberAsString_IsParsed()
        {
            var path = WriteTempFile(SampleJson);
            try
            {
                var result = BirdParser.Load(path);

                Assert.AreEqual(2, result[1].QuestionId);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Load_NullJson_ReturnsEmptyList()
        {
            var path = WriteTempFile("null");
            try
            {
                Assert.IsEmpty(BirdParser.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Load_MissingFile_Throws()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

            Assert.Throws<FileNotFoundException>(() => BirdParser.Load(path));
        }

        [TestMethod]
        public async Task LoadAsync_ValidJson_ReturnsExamples()
        {
            var path = WriteTempFile(SampleJson);
            try
            {
                var result = await BirdParser.LoadAsync(path);

                Assert.HasCount(2, result);
                Assert.AreEqual("db2", result[1].DbId);
                Assert.AreEqual("challenging", result[1].Difficulty);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task LoadAsync_NullJson_ReturnsEmptyList()
        {
            var path = WriteTempFile("null");
            try
            {
                Assert.IsEmpty(await BirdParser.LoadAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task LoadAsync_CancelledToken_Throws()
        {
            var path = WriteTempFile(SampleJson);
            try
            {
                using var cts = new CancellationTokenSource();
                await cts.CancelAsync();

                await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await BirdParser.LoadAsync(path, cts.Token));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
