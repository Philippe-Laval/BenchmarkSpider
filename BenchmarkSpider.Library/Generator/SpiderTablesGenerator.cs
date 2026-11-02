using BenchmarkSpider.Library.Parsers;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BenchmarkSpider.Library.Generator
{
    public class SpiderTablesGenerator
    {
        public static string SpiderDatabasePath => Path.Combine(Path.GetDirectoryName(SpiderTablesParser.TablesJsonPath)!, "database");

        public static int GenerateTablesJson(string inputDir, string outputFile, string exTabFile)
        {
            if (!Directory.Exists(inputDir))
            {
                Console.Error.WriteLine($"Input directory does not exist: {inputDir}");
                return 1;
            }
            if (!File.Exists(exTabFile))
            {
                Console.Error.WriteLine($"Existing tables JSON file does not exist: {exTabFile}");
                return 1;
            }

            var dirs = Directory.GetDirectories(inputDir).Select(d => Path.GetFileName(d)).Order(StringComparer.Ordinal).ToList();
            string DbPath(string df) => Path.Combine(inputDir, df, df + ".sqlite");
            var allFs = dirs.Where(df => File.Exists(DbPath(df))).ToList();

            var exTabs = SpiderTablesParser.Load(exTabFile).Where(t => allFs.Contains(t.DbId)).ToDictionary(t => t.DbId);
            Console.WriteLine("precessed file num: " + exTabs.Count);

            foreach (var d in dirs.Where(df => !File.Exists(DbPath(df))))
                Console.WriteLine("no sqlite file found in: " + d);

            var tables = new List<SpiderDatabase>();
            foreach (var df in allFs)
            {
                Console.WriteLine("\nreading new db: " + df);
                var table = DumpDbJsonSchema(DbPath(df), df);

                if (exTabs.TryGetValue(df, out var ex)
                    && ex.TableNames.Count == table.TableNames.Count
                    && ex.ColumnNames.Count == table.ColumnNames.Count
                    && ex.TableNames.Count != 0
                    && ex.ColumnNames.Count > 1)
                {
                    table.TableNames = ex.TableNames;
                    table.ColumnNames = ex.ColumnNames;
                }
                else
                {
                    Console.WriteLine("\n----------------------------------problem db: " + df);
                }
                tables.Add(table);
            }
            Console.WriteLine("final db num: " + tables.Count);

            var array = new JsonArray();
            foreach (var t in tables)
            {
                var node = JsonSerializer.SerializeToNode(t)!.AsObject();
                var sorted = new JsonObject();
                foreach (var kv in node.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sorted.Add(kv.Key, kv.Value?.DeepClone());
                array.Add(sorted);
            }
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(outputFile, array.ToJsonString(options));
            return 0;
        }

        /// <summary>
        /// Lit le schéma d’une base SQLite et construit un objet <c>SpiderDatabase</c> avec les tables, colonnes,
        /// types, clés primaires et clés étrangères.
        /// </summary>
        /// <remarks>Ajoute une entrée de colonne générique "*" avant d’extraire le schéma, puis résout
        /// les références de clés étrangères après lecture des métadonnées de toutes les tables.</remarks>
        /// <param name="dbPath">Chemin vers le fichier de base de données SQLite à lire en mode lecture seule.</param>
        /// <param name="dbId">Identifiant de base de données à affecter à <c>DbId</c> dans l’objet retourné.</param>
        /// <returns>Un objet <c>SpiderDatabase</c> renseigné à partir du schéma de la base, avec les noms originaux et
        /// normalisés des tables et colonnes, ainsi que les relations de clés.</returns>
        private static SpiderDatabase DumpDbJsonSchema(string dbPath, string dbId)
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            var data = new SpiderDatabase { DbId = dbId };
            data.ColumnNamesOriginal.Add(new SpiderColumnName(-1, "*"));
            data.ColumnNames.Add(new SpiderColumnName(-1, "*"));
            data.ColumnTypes.Add("text");

            var tableNames = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    tableNames.Add(reader.GetString(0));
            }

            var fkHolder = new List<(string Table, string Col, string RefTable, string? RefCol)>();
            for (var i = 0; i < tableNames.Count; i++)
            {
                var tableName = tableNames[i];
                data.TableNamesOriginal.Add(tableName);
                data.TableNames.Add(tableName.ToLowerInvariant().Replace('_', ' '));

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pragma_foreign_key_list($t)";
                    cmd.Parameters.AddWithValue("$t", tableName);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        fkHolder.Add((tableName, reader.GetString(3), reader.GetString(2), reader.IsDBNull(4) ? null : reader.GetString(4)));
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pragma_table_info($t)";
                    cmd.Parameters.AddWithValue("$t", tableName);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var colName = reader.GetString(1);
                        data.ColumnNamesOriginal.Add(new SpiderColumnName(i, colName));
                        data.ColumnNames.Add(new SpiderColumnName(i, colName.ToLowerInvariant().Replace('_', ' ')));
                        data.ColumnTypes.Add(MapColumnType(reader.GetString(2).ToLowerInvariant()));
                        if (reader.GetInt32(5) == 1)
                            data.PrimaryKeys.Add(new SpiderPrimaryKey([data.ColumnNames.Count - 1]));
                    }
                }
            }

            data.ForeignKeys = ConvertFkIndex(data, fkHolder);
            return data;
        }

        /// <summary>
        /// Mappe un type de colonne source vers une catégorie de type normalisée.
        /// </summary>
        /// <remarks>La classification repose sur des recherches de sous-chaînes et une chaîne vide est
        /// classée comme « text ».</remarks>
        /// <param name="colType">Type de colonne à évaluer.</param>
        /// <returns>« text », « number », « time », « boolean » ou « others » selon les correspondances de sous-chaînes.</returns>
        private static string MapColumnType(string colType)
        {
            if (colType.Contains("char") || colType == "" || colType.Contains("text") || colType.Contains("var"))
                return "text";
            if (colType.Contains("int") || colType.Contains("numeric") || colType.Contains("decimal") || colType.Contains("number")
                || colType.Contains("id") || colType.Contains("real") || colType.Contains("double") || colType.Contains("float"))
                return "number";
            if (colType.Contains("date") || colType.Contains("time") || colType.Contains("year"))
                return "time";
            if (colType.Contains("boolean"))
                return "boolean";
            return "others";
        }

        /// <summary>
        /// Convertit une liste de définitions de clés étrangères en références de colonnes indexées pour la base
        /// Spider.
        /// </summary>
        /// <remarks>Ajoute uniquement les relations dont les index de colonnes source et référencée sont
        /// tous deux résolus et strictement supérieurs à 0.</remarks>
        /// <param name="data">Base Spider contenant les noms de tables et de colonnes d’origine utilisés pour résoudre les index.</param>
        /// <param name="fks">Définitions de clés étrangères sous forme de tuples (table, colonne, table référencée, colonne référencée).</param>
        /// <returns>Liste des clés étrangères résolues en paires d’index de colonnes source et référencée.</returns>
        /// <exception cref="InvalidOperationException">Levée lorsqu’un nom de table source ou référencée ne peut pas être trouvé dans les tables d’origine.</exception>
        private static List<SpiderForeignKey> ConvertFkIndex(SpiderDatabase data, List<(string Table, string Col, string RefTable, string? RefCol)> fks)
        {
            var result = new List<SpiderForeignKey>();
            foreach (var (tn, col, refTn, refCol) in fks)
            {
                var tid = data.TableNamesOriginal.IndexOf(tn);
                var refTid = data.TableNamesOriginal.IndexOf(refTn);
                if (tid < 0 || refTid < 0)
                    throw new InvalidOperationException("finding tab name: " + tn + " " + refTn + " in " + string.Join(", ", data.TableNamesOriginal));

                int? refCid = null, cid = null;
                for (var i = 0; i < data.ColumnNamesOriginal.Count; i++)
                {
                    var (tabId, colOrg) = data.ColumnNamesOriginal[i];
                    if (tabId == refTid && refCol == colOrg)
                        refCid = i;
                    else if (tid == tabId && col == colOrg)
                        cid = i;
                }
                if (refCid is > 0 && cid is > 0)
                    result.Add(new SpiderForeignKey(cid.Value, refCid.Value));
            }
            return result;
        }

    }
}
