# BenchmarkSpider.GetTables

C# port of Spider's `get_tables.py`. Scans a directory of SQLite databases and generates a Spider-style `tables.json` (tables, columns, types, primary keys, foreign keys).

## Usage

```
BenchmarkSpider.GetTables <input_dir> <output_file> <existing_tables_json>
```

All three parameters are required and positional. If fewer than three are given, a usage message is printed and the exit code is `1`.

| # | Parameter              | Description |
|---|------------------------|-------------|
| 1 | `input_dir`            | Directory containing one sub-directory per database. Each sub-directory `<db_id>` must contain `<db_id>.sqlite` (e.g. `spider\database\concert_singer\concert_singer.sqlite`). Sub-directories without this file are reported and skipped. |
| 2 | `output_file`          | Path of the JSON file to write (e.g. `C:\temp\output.json`). It contains an indented JSON array with one entry per database. |
| 3 | `existing_tables_json` | Path of an existing `tables.json`. For each database, the normalized `table_names` and `column_names` are inherited from it when the table and column counts match those read from SQLite. Otherwise, the database is reported as a problem. |

## What is generated

For each database, the schema is read directly from the SQLite file:

- `table_names_original`, `column_names_original`: names as stored in SQLite (the first column is the `*` wildcard).
- `column_types`: SQLite types mapped to `text`, `number`, `time`, `boolean` or `others`.
- `primary_keys`, `foreign_keys`: expressed as column indexes.
- `table_names`, `column_names`: inherited from `existing_tables_json` when compatible, otherwise generated (lowercase, `_` replaced by a space).

## Output

- Progress and problem databases are written to standard output.
- Exit code `0` on success, `1` if arguments are missing.

## Example

```
dotnet run --project BenchmarkSpider.GetTables -- "C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider.Library\spider\database" "C:\temp\output.json" "C:\CodeGithubPerso\BenchmarkSpider\BenchmarkSpider.Library\spider\tables.json"
```
