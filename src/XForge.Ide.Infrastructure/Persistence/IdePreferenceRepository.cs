using Microsoft.EntityFrameworkCore;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

/// <summary>Leitura/gravação de preferências (chave → JSON).</summary>
public interface IIdePreferenceRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string json, CancellationToken ct = default);
}

public class IdePreferenceRepository : IIdePreferenceRepository
{
    private readonly IdeDbContext _db;

    public IdePreferenceRepository(IdeDbContext db) => _db = db;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var found = await _db.Preferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key, ct);
        return found?.ValueJson;
    }

    public async Task SetAsync(string key, string json, CancellationToken ct = default)
    {
        var existing = await _db.Preferences.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (existing is null)
        {
            _db.Preferences.Add(new IdePreference { Key = key, ValueJson = json });
        }
        else
        {
            existing.ValueJson = json;
        }
        await _db.SaveChangesAsync(ct);
    }
}
