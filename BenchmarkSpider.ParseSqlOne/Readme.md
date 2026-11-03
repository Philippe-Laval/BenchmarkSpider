# BenchmarkSpider.ParseSqlOne

C# port of Spider's `parse_sql_one.py`. Parses one SQL query against the schema of a Spider database and prints the resulting structured SQL as indented JSON.

## Usage

```
BenchmarkSpider.ParseSqlOne [sql] [db_id] [tables_json]
```

All parameters are positional and optional. To set a parameter, all the ones before it must be provided.

| # | Parameter     | Default                                                       | Description |
|---|---------------|---------------------------------------------------------------|-------------|
| 1 | `sql`         | `SELECT name ,  country ,  age FROM singer ORDER BY age DESC` | SQL query to parse. Quote it if it contains spaces. |
| 2 | `db_id`       | `concert_singer`                                              | Id of the Spider database whose schema is used to resolve tables and columns. |
| 3 | `tables_json` | `SpiderTablesParser.TablesJsonPath` (the `tables.json` of the Spider directory) | Path to the Spider `tables.json` file that contains the `db_id` schema. |

## Output

- On success, the parsed SQL (`ParsedSql`) is written to standard output as indented JSON, and the exit code is `0`.
- If `db_id` is not found in `tables_json`, `Unknown db_id: <db_id>` is written to standard error, and the exit code is `2`.

## Examples

Run with all defaults:

```
dotnet run --project BenchmarkSpider.ParseSqlOne
```

Parse a custom query on a given database:

```
dotnet run --project BenchmarkSpider.ParseSqlOne -- "SELECT count(*) FROM singer" concert_singer
```

Use a specific `tables.json`:

```
dotnet run --project BenchmarkSpider.ParseSqlOne -- "SELECT count(*) FROM singer" concert_singer C:\data\spider\tables.json
```
