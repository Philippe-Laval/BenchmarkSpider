using System;
using System.Collections.Generic;
using System.Text;

namespace BenchmarkSpider.Library;

public class SpiderStats
{

    public static void PrintStats(string rootFolderPath, string jsonPath)
    {
        var examples = SpiderParser.Load(jsonPath);
        var dbIds = new HashSet<string>(examples.Select(e => e.DbId.ToLower()));
        Console.WriteLine($"Stats for {jsonPath}:");
        Console.WriteLine($"- Number of examples: {examples.Count}");
        Console.WriteLine($"- Number of distinct databases: {dbIds.Count}");
        foreach (var dbId in dbIds)
        {
            Console.WriteLine($"\tDatabase: {dbId}");

            if(!CheckFilesForDatabase(rootFolderPath, dbId))
            {
                Console.WriteLine($"\t\tOne or more files are missing for database {dbId}");
            }
        }
    }

    public static void PrintAllStats(string rootFolderPath)
    {
        PrintStats(rootFolderPath, Path.Combine(rootFolderPath, "dev.json"));
        PrintStats(rootFolderPath, Path.Combine(rootFolderPath, "train_spider.json"));
        PrintStats(rootFolderPath, Path.Combine(rootFolderPath, "train_others.json"));
    }

    public static bool CheckFilesForDatabase(string rootFolderPath, string dbId, bool lookAtSchema = false)
    {
        var (sqlitePath, schemaPath, schemaPath2) = GetFullDatabasePath(rootFolderPath, dbId);
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

    public static (string sqlitePath, string schemaPath, string schemaPath2) GetFullDatabasePath(string rootFolderPath, string dbId)
    {
        string sqlitePath = Path.Combine(rootFolderPath, "database", dbId, $"{dbId}.sqlite");
        string schemaPath = Path.Combine(rootFolderPath, "database", dbId, $"schema.sql");
        string schemaPath2 = Path.Combine(rootFolderPath, "database", dbId, $"{dbId}.sql");
        return (sqlitePath, schemaPath, schemaPath2);
    }

}
