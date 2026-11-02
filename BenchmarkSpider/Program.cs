using BenchmarkSpider.Library;
    
namespace BenchmarkSpider
{
    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                const string rootFolderPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider.Library\spider";

                SpiderStats.PrintAllStats(rootFolderPath);
                return 0;
            }

            var options = new Dictionary<string, string>();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is "--gold" or "--pred" or "--db" or "--table" or "--etype" && i + 1 < args.Length)
                    options[args[i][2..]] = args[++i];
                else
                {
                    Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
                    Console.Error.WriteLine("Usage: --gold <file> --pred <file> --db <dir> --table <tables.json> --etype <all|exec|match>");
                    return 2;
                }
            }

            foreach (var key in new[] { "gold", "pred", "db", "table", "etype" })
            {
                if (!options.ContainsKey(key))
                {
                    Console.Error.WriteLine($"Missing argument --{key}");
                    return 2;
                }
            }

            var etype = options["etype"];
            if (etype is not ("all" or "exec" or "match"))
            {
                Console.Error.WriteLine("Unknown evaluation method");
                return 2;
            }

            var result = SpiderEvaluator.Evaluate(options["gold"], options["pred"], options["table"], options["db"], Console.WriteLine);
            Console.Write(SpiderEvaluator.FormatScores(result, etype));
            return 0;
        }
    }
}
