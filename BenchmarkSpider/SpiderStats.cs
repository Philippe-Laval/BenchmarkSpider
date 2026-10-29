using System;
using System.Collections.Generic;
using System.Text;

namespace BenchmarkSpider;

public class SpiderStats
{
    const string RootFolderPath = @"C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider\spider";

    public static void PrintStats(string jsonPath)
    {
        var examples = SpiderParser.Load(jsonPath);
        var dbIds = new HashSet<string>(examples.Select(e => e.DbId.ToLower()));
        Console.WriteLine($"Stats for {jsonPath}:");
        Console.WriteLine($"- Number of examples: {examples.Count}");
        Console.WriteLine($"- Number of distinct databases: {dbIds.Count}");
        foreach (var dbId in dbIds)
        {
            Console.WriteLine($"\tDatabase: {dbId}");

            if(!CheckFilesForDatabase(dbId))
            {
                Console.WriteLine($"\t\tOne or more files are missing for database {dbId}");
            }
        }
    }

    public static void PrintAllStats()
    {
        PrintStats(Path.Combine(RootFolderPath, "dev.json"));
        PrintStats(Path.Combine(RootFolderPath, "train_spider.json"));
        PrintStats(Path.Combine(RootFolderPath, "train_others.json"));
    }

    public static bool CheckFilesForDatabase(string dbId, bool lookAtSchema = false)
    {
        var (sqlitePath, schemaPath, schemaPath2) = GetFullDatabasePath(dbId);
        if (!File.Exists(sqlitePath))
        {
            Console.WriteLine($"SQLite file not found for database {dbId} at path: {sqlitePath}");
            return false;
        }

        if (lookAtSchema)
        {
            if (!File.Exists(schemaPath) && !File.Exists(schemaPath2))
            {
                Console.WriteLine($"Schema file not found for database {dbId} at paths: {schemaPath} and {schemaPath2}");
                return false;
            }
        }

        return true;
    }

    public static (string sqlitePath, string schemaPath, string schemaPath2) GetFullDatabasePath(string dbId)
    {
        string sqlitePath = Path.Combine(RootFolderPath, "database", dbId, $"{dbId}.sqlite");
        string schemaPath = Path.Combine(RootFolderPath, "database", dbId, $"schema.sql");
        string schemaPath2 = Path.Combine(RootFolderPath, "database", dbId, $"{dbId}.sql");
        return (sqlitePath, schemaPath, schemaPath2);
    }

}
