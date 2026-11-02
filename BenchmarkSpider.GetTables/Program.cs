using BenchmarkSpider.Library.Generator;
using BenchmarkSpider.Library.Parsers;
using Microsoft.Data.Sqlite;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

// "C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider.Library\spider\database" "C:\temp\output.json" "C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider.Library\spider\tables.json"

namespace BenchmarkSpider.GetTables
{
    internal class Program
    {
        /// <summary>
        /// Traite des bases SQLite dans les sous-répertoires d’un dossier d’entrée, fusionne les métadonnées de tables
        /// avec un fichier existant si elles sont compatibles, puis écrit le schéma consolidé au format JSON.
        /// </summary>
        /// <remarks>Les noms de tables et de colonnes du fichier existant sont repris uniquement si les
        /// comptages correspondent et respectent les contraintes minimales attendues.</remarks>
        /// <param name="args">Arguments de ligne de commande : chemin du dossier racine contenant les sous-dossiers de bases, chemin du
        /// fichier JSON de sortie et chemin du fichier tables JSON existant à réutiliser.</param>
        /// <returns>0 si le traitement se termine correctement ; 1 si le nombre d’arguments requis est insuffisant.</returns>
        static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: BenchmarkSpider.GetTables [dir includes many subdirs containing database.sqlite files] [output file name e.g. output.json] [existing tables.json file to be inherited]");
                return 1;
            }

            var inputDir = args[0];
            var outputFile = args[1];
            var exTabFile = args[2];

            var result = SpiderTablesGenerator.GenerateTablesJson(inputDir, outputFile, exTabFile);
            return result;
        }
    }
}
