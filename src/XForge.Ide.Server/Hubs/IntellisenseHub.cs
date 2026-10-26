using Microsoft.AspNetCore.SignalR;
using XForge.Ide.Server.Services;

namespace XForge.Ide.Server.Hubs;

/// <summary>Requisição de completion/hover/diagnóstico vinda do Monaco.</summary>
public sealed class IntellisenseRequest
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
}

/// <summary>
/// Ponte SignalR do IntelliSense: o Monaco chama estes métodos a cada tecla
/// (debounce no cliente) e recebe completions, hover e diagnósticos do Roslyn.
/// </summary>
public sealed class IntellisenseHub : Hub
{
    private readonly RoslynIntellisenseService _roslyn;

    public IntellisenseHub(RoslynIntellisenseService roslyn) => _roslyn = roslyn;

    public Task<IReadOnlyList<CompletionItem>> CompletionsAsync(IntellisenseRequest request)
        => _roslyn.CompletionsAsync(
            request.ProjectRoot,
            request.FilePath,
            request.Content,
            request.Line,
            request.Column,
            Context.ConnectionAborted);

    public Task<IReadOnlyList<DiagnosticItem>> DiagnosticsAsync(IntellisenseRequest request)
        => _roslyn.DiagnosticsAsync(
            request.ProjectRoot,
            request.FilePath,
            request.Content,
            Context.ConnectionAborted);

    public Task<HoverInfo?> HoverAsync(IntellisenseRequest request)
        => _roslyn.HoverAsync(
            request.ProjectRoot,
            request.FilePath,
            request.Content,
            request.Line,
            request.Column,
            Context.ConnectionAborted);

    /// <summary>Invalida o cache do projeto (chamado ao salvar um arquivo).</summary>
    public Task InvalidateAsync(string projectRoot)
        => _roslyn.InvalidateAsync(projectRoot);
}
