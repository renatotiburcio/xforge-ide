using XForge.Ide.Application.Dto;

namespace XForge.Ide.Application.Services;

/// <summary>Um script de migração gerado (DDL + comandos EF Core).</summary>
public sealed record MigrationScript(
    string Provider,
    string Label,
    string FileName,
    string Sql,
    string Commands);

/// <summary>
/// Gera o DDL (CREATE TABLE) e os comandos `dotnet ef` a partir do dicionário,
/// para os provedores suportados (SQLite, PostgreSQL, MySQL).
/// </summary>
public sealed class MigrationService
{
    public IReadOnlyList<(string Id, string Label)> Providers { get; } = new[]
    {
        ("Sqlite", "SQLite"),
        ("PostgreSql", "PostgreSQL"),
        ("MySql", "MySQL"),
    };

    public async Task<IReadOnlyList<MigrationScript>> GenerateAsync(
        ErDiagramDto diagram,
        string ns,
        string contextName,
        CancellationToken ct = default)
    {
        var scripts = new List<MigrationScript>();
        foreach (var (id, label) in Providers)
        {
            var sql = id switch
            {
                "Sqlite" => Sqlite(diagram),
                "PostgreSql" => PostgreSql(diagram),
                _ => MySql(diagram),
            };
            var commands = $"""
                dotnet tool install --global dotnet-ef
                dotnet add package Microsoft.EntityFrameworkCore.Design
                dotnet ef migrations add InitialCreate --project src/{ns}.Infrastructure --startup-project src/{ns}.Api --context {contextName}
                dotnet ef database update --project src/{ns}.Infrastructure --startup-project src/{ns}.Api --context {contextName}
                """;
            scripts.Add(new MigrationScript(id, label, $"InitialCreate.{id}.sql", sql, commands));
        }
        await Task.CompletedTask;
        return scripts;
    }

    // ------------------------------------------------------------------ SQLite

    private static string Sqlite(ErDiagramDto d)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- SQLite · gerado pelo dicionário da XForge IDE");
        foreach (var n in d.Nodes)
        {
            sb.AppendLine($"CREATE TABLE \"{n.TableName}\" (");
            var cols = n.Fields.Select(f =>
                $"    \"{f.Name}\" {MapType("Sqlite", f)} {(f.Required ? "NOT NULL" : "NULL")}");
            var pk = n.Fields.Where(f => f.PrimaryKey).Select(f => $"\"{f.Name}\"").ToArray();
            if (pk.Length == 0)
            {
                pk = new[] { "\"Id\"" };
            }
            sb.AppendLine(string.Join(",\n", cols));
            sb.AppendLine($",\n    CONSTRAINT \"PK_{n.TableName}\" PRIMARY KEY ({string.Join(", ", pk)})");
            sb.AppendLine(");");
            sb.AppendLine();
        }
        foreach (var e in d.Edges)
        {
            var fromNode = d.Nodes.FirstOrDefault(n => n.Name == e.From);
            var toNode = d.Nodes.FirstOrDefault(n => n.Name == e.To);
            if (fromNode is null || toNode is null)
            {
                continue;
            }
            var pk = toNode.Fields.FirstOrDefault(f => f.PrimaryKey)?.Name ?? "Id";
            if (e.Cardinality is "N:1" or "1:1")
            {
                sb.AppendLine($"ALTER TABLE \"{fromNode.TableName}\" ADD COLUMN \"{toNode.TableName}Id\" TEXT;");
            }
            else if (e.Cardinality is "1:N" or "N:N")
            {
                sb.AppendLine($"CREATE TABLE \"{fromNode.TableName}_{toNode.TableName}\" (");
                sb.AppendLine($"    \"{fromNode.TableName}Id\" TEXT NOT NULL,");
                sb.AppendLine($"    \"{toNode.TableName}Id\" TEXT NOT NULL,");
                sb.AppendLine($"    PRIMARY KEY (\"{fromNode.TableName}Id\", \"{toNode.TableName}Id\"));");
            }
            sb.AppendLine($"CREATE INDEX \"IX_{fromNode.TableName}_{toNode.TableName}Id\" ON \"{fromNode.TableName}\" (\"{toNode.TableName}Id\");");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // -------------------------------------------------------------- PostgreSQL

    private static string PostgreSql(ErDiagramDto d)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- PostgreSQL · gerado pelo dicionário da XForge IDE");
        foreach (var n in d.Nodes)
        {
            sb.AppendLine($"CREATE TABLE \"{n.TableName}\" (");
            var cols = n.Fields.Select(f =>
                $"    \"{f.Name}\" {MapType("PostgreSql", f)} {(f.Required ? "NOT NULL" : "NULL")}");
            var pk = n.Fields.Where(f => f.PrimaryKey).Select(f => $"\"{f.Name}\"").ToArray();
            if (pk.Length == 0)
            {
                pk = new[] { "\"Id\"" };
            }
            sb.AppendLine(string.Join(",\n", cols));
            sb.AppendLine($",\n    CONSTRAINT \"PK_{n.TableName}\" PRIMARY KEY ({string.Join(", ", pk)})");
            sb.AppendLine(");");
            sb.AppendLine();
        }
        foreach (var e in d.Edges)
        {
            var fromNode = d.Nodes.FirstOrDefault(n => n.Name == e.From);
            var toNode = d.Nodes.FirstOrDefault(n => n.Name == e.To);
            if (fromNode is null || toNode is null)
            {
                continue;
            }
            var pk = toNode.Fields.FirstOrDefault(f => f.PrimaryKey)?.Name ?? "Id";
            if (e.Cardinality is "N:1" or "1:1")
            {
                sb.AppendLine($"ALTER TABLE \"{fromNode.TableName}\" ADD COLUMN \"{toNode.TableName}Id\" uuid;");
            }
            else if (e.Cardinality is "1:N" or "N:N")
            {
                sb.AppendLine($"CREATE TABLE \"{fromNode.TableName}_{toNode.TableName}\" (");
                sb.AppendLine($"    \"{fromNode.TableName}Id\" uuid NOT NULL,");
                sb.AppendLine($"    \"{toNode.TableName}Id\" uuid NOT NULL,");
                sb.AppendLine($"    PRIMARY KEY (\"{fromNode.TableName}Id\", \"{toNode.TableName}Id\"));");
            }
            sb.AppendLine($"CREATE INDEX \"IX_{fromNode.TableName}_{toNode.TableName}Id\" ON \"{fromNode.TableName}\" (\"{toNode.TableName}Id\");");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // -------------------------------------------------------------------- MySQL

    private static string MySql(ErDiagramDto d)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- MySQL · gerado pelo dicionário da XForge IDE");
        foreach (var n in d.Nodes)
        {
            sb.AppendLine($"CREATE TABLE `{n.TableName}` (");
            var cols = n.Fields.Select(f =>
                $"    `{f.Name}` {MapType("MySql", f)} {(f.Required ? "NOT NULL" : "NULL")}");
            var pk = n.Fields.Where(f => f.PrimaryKey).Select(f => $"`{f.Name}`").ToArray();
            if (pk.Length == 0)
            {
                pk = new[] { "`Id`" };
            }
            sb.AppendLine(string.Join(",\n", cols));
            sb.AppendLine($",\n    PRIMARY KEY ({string.Join(", ", pk)})");
            sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
            sb.AppendLine();
        }
        foreach (var e in d.Edges)
        {
            var fromNode = d.Nodes.FirstOrDefault(n => n.Name == e.From);
            var toNode = d.Nodes.FirstOrDefault(n => n.Name == e.To);
            if (fromNode is null || toNode is null)
            {
                continue;
            }
            var pk = toNode.Fields.FirstOrDefault(f => f.PrimaryKey)?.Name ?? "Id";
            if (e.Cardinality is "N:1" or "1:1")
            {
                sb.AppendLine($"ALTER TABLE `{fromNode.TableName}` ADD COLUMN `{toNode.TableName}Id` char(36) NULL;");
            }
            else if (e.Cardinality is "1:N" or "N:N")
            {
                sb.AppendLine($"CREATE TABLE `{fromNode.TableName}_{toNode.TableName}` (");
                sb.AppendLine($"    `{fromNode.TableName}Id` char(36) NOT NULL,");
                sb.AppendLine($"    `{toNode.TableName}Id` char(36) NOT NULL,");
                sb.AppendLine($"    PRIMARY KEY (`{fromNode.TableName}Id`, `{toNode.TableName}Id`)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
            }
            sb.AppendLine($"CREATE INDEX `IX_{fromNode.TableName}_{toNode.TableName}Id` ON `{fromNode.TableName}` (`{toNode.TableName}Id`);");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Mapeia o tipo CLR do campo para o tipo do banco.</summary>
    private static string MapType(string provider, ErFieldDto f) => (provider, f.ClrType) switch
    {
        ("Sqlite", "string") => "TEXT",
        ("Sqlite", "int") => "INTEGER",
        ("Sqlite", "long") => "INTEGER",
        ("Sqlite", "bool") => "INTEGER",
        ("Sqlite", "DateTime") => "TEXT",
        ("Sqlite", "decimal") or ("Sqlite", "double") => "REAL",
        ("Sqlite", "Guid") => "TEXT",
        ("Sqlite", "byte[]") => "BLOB",
        ("PostgreSql", "string") => "text",
        ("PostgreSql", "int") => "integer",
        ("PostgreSql", "long") => "bigint",
        ("PostgreSql", "bool") => "boolean",
        ("PostgreSql", "DateTime") => "timestamp with time zone",
        ("PostgreSql", "decimal") => "numeric",
        ("PostgreSql", "double") => "double precision",
        ("PostgreSql", "Guid") => "uuid",
        ("PostgreSql", "byte[]") => "bytea",
        ("MySql", "string") => "varchar(500)",
        ("MySql", "int") => "int",
        ("MySql", "long") => "bigint",
        ("MySql", "bool") => "bit(1)",
        ("MySql", "DateTime") => "datetime(6)",
        ("MySql", "decimal") => "decimal(18,2)",
        ("MySql", "double") => "double",
        ("MySql", "Guid") => "char(36)",
        ("MySql", "byte[]") => "varbinary(255)",
        _ => "TEXT",
    };
}
