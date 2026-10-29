using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchmarkSpider;

public static class SpiderParser
{
    public const string DevJsonPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider\spider\dev.json";
    public const string TrainSpiderJsonPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider\spider\train_spider.json";
    public const string TrainOthersJsonPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider\spider\train_others.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static string SpiderDirectory { get; } = Path.GetDirectoryName(DevJsonPath)!;

    public static List<SpiderExample> Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<SpiderExample>>(stream, Options) ?? [];
    }

    public static async Task<List<SpiderExample>> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<SpiderExample>>(stream, Options, ct) ?? [];
    }
}
