using Microsoft.EntityFrameworkCore;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

public class ProjectRepository : IProjectRepository
{
    private readonly IdeDbContext _db;

    public ProjectRepository(IdeDbContext db) => _db = db;

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
        => await _db.Projects.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    public Task<Project?> GetAsync(Guid id, CancellationToken ct = default)
        => _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Project?> GetByPathAsync(string path, CancellationToken ct = default)
        => _db.Projects.FirstOrDefaultAsync(p => p.Path == path, ct);

    public async Task AddAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Update(project);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var count = await _db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        if (count == 0)
            throw new InvalidOperationException("Projeto não encontrado para exclusão.");
    }
}
