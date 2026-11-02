using BenchmarkSpider.Library.Parsers;
using System;
using System.Collections.Generic;
using System.Text;

namespace BenchmarkSpider.Library;

/// <summary>
/// Fournit des opérations utilitaires pour analyser les fichiers du jeu de données Spider, afficher des statistiques
/// par fichier JSON et vérifier la présence des fichiers de base de données associés.
/// </summary>
/// <remarks>Suppose une arborescence racine contenant les fichiers JSON d'exemples et un dossier
/// `database/<dbId>/` avec les fichiers `.sqlite` et, en option, les scripts de schéma SQL.</remarks>
public class SpiderStats
{
    /// <summary>
    /// Charge les exemples depuis un fichier JSON, affiche le nombre total d’exemples et de bases de données
    /// distinctes, puis signale les bases de données avec des fichiers manquants.
    /// </summary>
    /// <remarks>Les identifiants de base de données sont traités sans tenir compte de la casse, et la sortie
    /// est écrite sur la console.</remarks>
    /// <param name="rootFolderPath">Chemin du dossier racine contenant les fichiers attendus pour chaque base de données.</param>
    /// <param name="jsonPath">Chemin du fichier JSON contenant les exemples à analyser.</param>
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

    /// <summary>
    /// Affiche les statistiques pour les jeux de données `dev.json`, `train_spider.json` et `train_others.json` situés
    /// dans le dossier racine spécifié.
    /// </summary>
    /// <remarks>Le dossier doit contenir les fichiers `dev.json`, `train_spider.json` et
    /// `train_others.json`.</remarks>
    /// <param name="rootFolderPath">Chemin du dossier racine contenant les fichiers de jeux de données à analyser.</param>
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
