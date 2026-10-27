using Microsoft.EntityFrameworkCore;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

/// <summary>Persistência dos snapshots de interface (upsert por chave).</summary>
public class UiSnapshotRepository : IUiSnapshotStore
{
    private readonly IdeDbContext _db;

    public UiSnapshotRepository(IdeDbContext db) => _db = db;

    public Task<UiSnapshot?> GetAsync(string key, CancellationToken ct = default)
        => _db.UiSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);

    public async Task UpsertAsync(UiSnapshot snapshot, CancellationToken ct = default)
    {
        snapshot.UpdatedAt = DateTime.UtcNow;
        var existing = await _db.UiSnapshots.FirstOrDefaultAsync(s => s.Key == snapshot.Key, ct);
        if (existing is null)
        {
            _db.UiSnapshots.Add(snapshot);
        }
        else
        {
            existing.Route = snapshot.Route;
            existing.ProjectId = snapshot.ProjectId;
            existing.Json = snapshot.Json;
            existing.UpdatedAt = snapshot.UpdatedAt;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var snapshot = await _db.UiSnapshots.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (snapshot is not null)
        {
            _db.UiSnapshots.Remove(snapshot);
            await _db.SaveChangesAsync(ct);
        }
    }
}
