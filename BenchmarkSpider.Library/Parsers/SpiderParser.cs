using BenchmarkSpider.Library.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchmarkSpider.Library.Parsers;

/// <summary>
/// Fournit des méthodes utilitaires pour charger des jeux de données Spider depuis des fichiers JSON.
/// </summary>
/// <remarks>Expose des chemins de fichiers connus pour les jeux de données de développement et d’entraînement,
/// ainsi qu’un répertoire racine. La désérialisation JSON est configurée pour ignorer la casse des noms de propriétés
/// et accepter les nombres fournis sous forme de chaînes.</remarks>
public static class SpiderParser
{
    public const string LibraryRootPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider.Library";
    public const string DevJsonPath = LibraryRootPath + @"\spider\dev.json";
    public const string TrainSpiderJsonPath = LibraryRootPath + @"\spider\train_spider.json";
    public const string TrainOthersJsonPath = LibraryRootPath + @"\spider\train_others.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// Obtient le chemin du répertoire "spider" contenant les fichiers JSON et les bases de données.
    /// </summary>
    public static string SpiderDirectory { get; } = Path.GetDirectoryName(DevJsonPath)!;

    /// <summary>
    /// Charge une liste de `SpiderExample` à partir d’un fichier JSON.
    /// </summary>
    /// <param name="path">Chemin du fichier JSON à lire.</param>
    /// <returns>Liste de `SpiderExample` désérialisée depuis le fichier, ou liste vide si la désérialisation retourne `null`.</returns>
    public static List<SpiderExample> Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<SpiderExample>>(stream, Options) ?? [];
    }

    /// <summary>
    /// Charge de manière asynchrone une collection de `SpiderExample` depuis un fichier JSON.
    /// </summary>
    /// <param name="path">Chemin du fichier JSON à lire.</param>
    /// <param name="ct">Jeton utilisé pour annuler l’opération asynchrone.</param>
    /// <returns>Une liste de `SpiderExample` désérialisée depuis le fichier, ou une liste vide si le contenu JSON est `null`.</returns>
    public static async Task<List<SpiderExample>> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<SpiderExample>>(stream, Options, ct) ?? [];
    }
}
