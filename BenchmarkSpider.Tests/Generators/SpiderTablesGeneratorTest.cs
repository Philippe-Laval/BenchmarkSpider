using BenchmarkSpider.Library.Generator;
using BenchmarkSpider.Library.Parsers;
using System;
using System.Collections.Generic;
using System.Text;

namespace BenchmarkSpider.Library.Tests.Generators
{
    [TestClass]
    public sealed class SpiderTablesGeneratorTest
    {
        [TestMethod]
        public void ParseLine_SplitsSqlAndDbId()
        {
            string tempPath = Path.GetTempPath();

            var inputDir = SpiderTablesGenerator.SpiderDatabasePath;
            var outputFile = Path.Combine(tempPath, "output.json");
            var exTabFile = SpiderTablesParser.TablesJsonPath;

            if (File.Exists(outputFile))
            {
                File.Delete(outputFile);
            }

            int result = SpiderTablesGenerator.GenerateTablesJson(inputDir, outputFile, exTabFile);
            Assert.AreEqual(0, result, "GenerateTablesJson should return 0 for success.");

            if (File.Exists(outputFile))
            {
                File.Delete(outputFile);
            }
        }
    }
}
