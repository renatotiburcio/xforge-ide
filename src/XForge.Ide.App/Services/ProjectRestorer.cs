using XForge.Ide.Application.Dto;

namespace XForge.Ide.App.Services;

/// <summary>
/// Helper para páginas que dependem do projeto atual.
/// Resolve a corrida de render: o shell restaura o projeto do localStorage em
/// OnAfterRenderAsync (após a primeira render), que pode ser depois do OnInitialized
/// da página. Este helper restaura na própria página e reage a mudanças.
/// </summary>
public static class ProjectRestorer
{
    /// <summary>
    /// Restaura o projeto atual (localStorage → banco). Retorna true se um projeto
    /// ficou disponível.
    /// </summary>
    public static async Task<bool> TryRestoreAsync(
        IdeUiState ui, Func<Guid, Task<ProjectDto?>> loadById)
    {
        if (ui.CurrentProject is not null)
        {
            return true;
        }
        return await ui.TryRestoreAsync(loadById);
    }

    /// <summary>
    /// Assina mudanças de projeto. Retorna o action a desinscrever (IDisposable).
    /// </summary>
    public static void Watch(IdeUiState ui, Action onChange)
        => ui.Changed += onChange;
}
