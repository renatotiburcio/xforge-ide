using XForge.Ide.Core.Entities;

namespace XForge.Ide.Core.Abstractions;

/// <summary>Persistência de execuções de wizard.</summary>
public interface IWizardRunRepository
{
    Task AddAsync(WizardRun run, CancellationToken ct = default);
    Task UpdateAsync(WizardRun run, CancellationToken ct = default);
    Task<IReadOnlyList<WizardRun>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
}
