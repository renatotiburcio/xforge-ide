using Microsoft.Data.Sqlite;
using XForge.Ide.Core.Abstractions;

namespace XForge.Ide.Infrastructure.Introspection;

/// <summary>
/// Inferência reversa de bancos SQLite (sqlite_master + PRAGMA) → dicionário.
/// </summary>
public class SqliteIntrospector : IDatabaseIntrospector
{
    public string Provider => "Sqlite";

    public async Task<IReadOnlyList<InferredTable>> InferAsync(string connectionString, CancellationToken ct = default)
    {
        var tables = new List<InferredTable>();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        var tableNames = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT name FROM sqlite_master
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'
                ORDER BY name
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        foreach (var table in tableNames)
        {
            var columns = new List<InferredColumn>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info('{table.Replace("'", "''")}')";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var name = reader.GetString(1);
                    var dbType = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2);
                    var notNull = !reader.IsDBNull(3) && reader.GetInt64(3) != 0;
                    var isPk = !reader.IsDBNull(5) && reader.GetInt64(5) > 0;
                    columns.Add(new InferredColumn(name, string.IsNullOrWhiteSpace(dbType) ? "TEXT" : dbType, notNull, isPk, columns.Count));
                }
            }

            var indexes = new List<(string, bool, IReadOnlyList<string>)>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA index_list('{table.Replace("'", "''")}')";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                var indexRows = new List<(string Name, bool Unique)>();
                while (await reader.ReadAsync(ct))
                {
                    var indexName = reader.GetString(1);
                    var unique = !reader.IsDBNull(2) && reader.GetInt64(2) != 0;
                    indexRows.Add((indexName, unique));
                }

                foreach (var (indexName, unique) in indexRows)
                {
                    var indexColumns = new List<string>();
                    await using var infoCmd = connection.CreateCommand();
                    infoCmd.CommandText = $"PRAGMA index_info('{indexName.Replace("'", "''")}')";
                    await using var infoReader = await infoCmd.ExecuteReaderAsync(ct);
                    while (await infoReader.ReadAsync(ct))
                    {
                        if (!infoReader.IsDBNull(2))
                        {
                            indexColumns.Add(infoReader.GetString(2));
                        }
                    }
                    indexes.Add((indexName, unique, indexColumns));
                }
            }

            tables.Add(new InferredTable(table, columns, indexes));
        }

        return tables;
    }
}
