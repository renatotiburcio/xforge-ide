using Microsoft.EntityFrameworkCore;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

public class WizardRunRepository : IWizardRunRepository
{
    private readonly IdeDbContext _db;

    public WizardRunRepository(IdeDbContext db) => _db = db;

    public async Task AddAsync(WizardRun run, CancellationToken ct = default)
    {
        _db.WizardRuns.Add(run);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(WizardRun run, CancellationToken ct = default)
    {
        _db.WizardRuns.Update(run);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WizardRun>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => await _db.WizardRuns
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
}
