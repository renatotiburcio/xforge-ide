namespace XForge.Ide.Core.Abstractions;

/// <summary>Coluna inferida de um banco real.</summary>
public sealed record InferredColumn(
    string Name,
    string DbType,
    bool Required,
    bool IsPrimaryKey,
    int Ordinal);

/// <summary>Tabela inferida de um banco real.</summary>
public sealed record InferredTable(
    string Name,
    IReadOnlyList<InferredColumn> Columns,
    IReadOnlyList<(string Name, bool IsUnique, IReadOnlyList<string> Columns)> Indexes);

/// <summary>
/// Inferência reversa de banco de dados → dicionário (sucessor do import do Dictionary Editor).
/// </summary>
public interface IDatabaseIntrospector
{
    /// <summary>Provider do introspector (ex.: "Sqlite").</summary>
    string Provider { get; }

    Task<IReadOnlyList<InferredTable>> InferAsync(string connectionString, CancellationToken ct = default);
}
