using BenchmarkBird.Library.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchmarkBird.Library.Parsers
{
    /// <summary>Charge les jeux de données BIRD depuis des fichiers JSON.</summary>
    public class BirdParser
    {
        public const string LibraryRootPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkBird.Library";
        public const string DevBirdJsonPath = LibraryRootPath + @"\bird\dev\dev.json";
        public const string TrainOthersJsonPath = LibraryRootPath + @"\bird\dev\dev_tied_append.json";
        public const string TrainBirdJsonPath = LibraryRootPath + @"\bird\train\train.json";


        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        /// <summary>
        /// Charge une liste de BirdExample depuis un fichier JSON.
        /// </summary>
        /// <remarks>Utilise les options de sérialisation définies dans Options.</remarks>
        /// <param name="path">Chemin du fichier JSON à lire.</param>
        /// <returns>La liste des BirdExample désérialisés, ou une liste vide si le contenu JSON est null.</returns>
        public static List<BirdExample> Load(string path)
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<List<BirdExample>>(stream, Options) ?? [];
        }

        /// <summary>
        /// Charge de manière asynchrone une liste de <c>BirdExample</c> depuis un fichier JSON.
        /// </summary>
        /// <param name="path">Chemin du fichier JSON à lire.</param>
        /// <param name="ct">Jeton utilisé pour propager une demande d’annulation.</param>
        /// <returns>Une tâche qui représente l’opération asynchrone et dont le résultat est la liste désérialisée, ou une liste
        /// vide si le contenu JSON est nul.</returns>
        public static async Task<List<BirdExample>> LoadAsync(string path, CancellationToken ct = default)
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<BirdExample>>(stream, Options, ct) ?? [];
        }
    }
}
