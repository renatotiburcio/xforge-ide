using System.Text.Json;
using Microsoft.JSInterop;
using XForge.Ide.Application.Dto;

namespace XForge.Ide.App.Services;

/// <summary>Estado de UI compartilhado da IDE (projeto atual, mensagens de status).</summary>
public class IdeUiState
{
    private const string StorageKey = "xf-current-project";
    private readonly IJSRuntime _js;

    public IdeUiState(IJSRuntime js) => _js = js;

    public ProjectDto? CurrentProject { get; private set; }
    public string StatusText { get; private set; } = "Pronto";

    /// <summary>Arquivo ativo no editor (usado pelo Diff "não salvas × disco").</summary>
    public string? ActiveFilePath { get; private set; }

    /// <summary>Conteúdo pendente (não salvo) do arquivo ativo.</summary>
    public string? PendingContent { get; private set; }

    public event Action? Changed;

    /// <summary>Registra o arquivo ativo e seu buffer pendente (chamado pelo editor).</summary>
    public void SetActiveFile(string? path, string? content)
    {
        ActiveFilePath = path;
        PendingContent = content;
    }

    public void SetProject(ProjectDto? project)
    {
        CurrentProject = project;
        StatusText = project is null ? "Nenhum projeto aberto" : $"Projeto: {project.Name}";
        Changed?.Invoke();
    }

    public void SetStatus(string status)
    {
        StatusText = status;
        Changed?.Invoke();
    }

    /// <summary>Persiste o projeto atual no localStorage (sobrevive a navegações/circuitos).</summary>
    public async Task PersistAsync()
    {
        try
        {
            if (CurrentProject is null)
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
                return;
            }
            await _js.InvokeVoidAsync(
                "localStorage.setItem", StorageKey, JsonSerializer.Serialize(CurrentProject));
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>
    /// Restaura o último projeto do localStorage e o recarrega do banco (dados frescos).
    /// Chamado na inicialização do circuito (IdeShell OnAfterRenderAsync).
    /// </summary>
    public async Task<bool> TryRestoreAsync(Func<Guid, Task<ProjectDto?>> loadById, CancellationToken ct = default)
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            var saved = JsonSerializer.Deserialize<ProjectDto>(json);
            if (saved is null || saved.Id == Guid.Empty)
            {
                return false;
            }

            var fresh = await loadById(saved.Id);
            if (fresh is null)
            {
                return false;
            }

            SetProject(fresh);
            return true;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // JSRuntime indisponível no pré-render
            return false;
        }
    }
}
