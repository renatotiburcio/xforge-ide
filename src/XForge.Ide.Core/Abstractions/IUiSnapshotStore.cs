using XForge.Ide.Core.Entities;

namespace XForge.Ide.Core.Abstractions;

/// <summary>
/// Persistência dos snapshots de interface (contexto de trabalho por rota).
/// </summary>
public interface IUiSnapshotStore
{
    Task<UiSnapshot?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Cria ou atualiza o snapshot da chave (upsert).</summary>
    Task UpsertAsync(UiSnapshot snapshot, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
