using Microsoft.EntityFrameworkCore;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

/// <summary>Persistência da sessão do editor (abas, arquivo ativo, painel).</summary>
public interface IIdeSessionRepository
{
    Task<IdeSession?> GetAsync(string key, CancellationToken ct = default);
    Task SaveAsync(IdeSession session, CancellationToken ct = default);
}

public class IdeSessionRepository : IIdeSessionRepository
{
    private readonly IdeDbContext _db;

    public IdeSessionRepository(IdeDbContext db) => _db = db;

    public Task<IdeSession?> GetAsync(string key, CancellationToken ct = default)
        => _db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);

    public async Task SaveAsync(IdeSession session, CancellationToken ct = default)
    {
        session.UpdatedAt = DateTime.UtcNow;
        var existing = await _db.Sessions.FirstOrDefaultAsync(s => s.Key == session.Key, ct);
        if (existing is null)
        {
            _db.Sessions.Add(session);
        }
        else
        {
            existing.TabsJson = session.TabsJson;
            existing.ActivePath = session.ActivePath;
            existing.PanelHeight = session.PanelHeight;
            existing.PanelOpen = session.PanelOpen;
            existing.TerminalCount = session.TerminalCount;
            existing.UpdatedAt = session.UpdatedAt;
        }
        await _db.SaveChangesAsync(ct);
    }
}
