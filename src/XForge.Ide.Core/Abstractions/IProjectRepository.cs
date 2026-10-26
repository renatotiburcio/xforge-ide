using XForge.Ide.Core.Entities;

namespace XForge.Ide.Core.Abstractions;

/// <summary>Persistência de projetos da IDE.</summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default);
    Task<Project?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Project?> GetByPathAsync(string path, CancellationToken ct = default);
    Task AddAsync(Project project, CancellationToken ct = default);
    Task UpdateAsync(Project project, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
