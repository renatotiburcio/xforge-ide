using Microsoft.EntityFrameworkCore;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

/// <summary>Persistência dos rascunhos do editor (upsert por caminho).</summary>
public class EditorDraftRepository : IEditorDraftStore
{
    private readonly IdeDbContext _db;

    public EditorDraftRepository(IdeDbContext db) => _db = db;

    public Task<EditorDraft?> GetAsync(string path, CancellationToken ct = default)
        => _db.EditorDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.Path == path, ct);

    public async Task UpsertAsync(EditorDraft draft, CancellationToken ct = default)
    {
        draft.UpdatedAt = DateTime.UtcNow;
        var existing = await _db.EditorDrafts.FirstOrDefaultAsync(d => d.Path == draft.Path, ct);
        if (existing is null)
        {
            _db.EditorDrafts.Add(draft);
        }
        else
        {
            existing.Content = draft.Content;
            existing.UpdatedAt = draft.UpdatedAt;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var draft = await _db.EditorDrafts.FirstOrDefaultAsync(d => d.Path == path, ct);
        if (draft is not null)
        {
            _db.EditorDrafts.Remove(draft);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var drafts = await _db.EditorDrafts
            .Where(d => d.Path.StartsWith(prefix))
            .ToListAsync(ct);
        if (drafts.Count == 0)
        {
            return 0;
        }
        _db.EditorDrafts.RemoveRange(drafts);
        await _db.SaveChangesAsync(ct);
        return drafts.Count;
    }
}
