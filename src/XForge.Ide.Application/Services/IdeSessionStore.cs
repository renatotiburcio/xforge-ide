namespace XForge.Ide.Application.Services;

/// <summary>Estado da sessão do editor (persistido em SQLite por projeto).</summary>
public sealed record IdeSessionState(
    IReadOnlyList<string> Tabs,
    string? ActivePath,
    double PanelHeight,
    bool PanelOpen,
    int TerminalCount)
{
    public static readonly IdeSessionState Empty = new(
        Array.Empty<string>(), null, 240, false, 0);
}

/// <summary>
/// Persistência da sessão e das preferências do editor no banco da IDE (SQLite),
/// para que o estado sobreviva a reinícios e acompanhe o usuário entre máquinas.
/// </summary>
public interface IIdeSessionStore
{
    /// <summary>Chave = caminho do projeto (ou "." para o estado global).</summary>
    Task<IdeSessionState> GetSessionAsync(string projectKey, CancellationToken ct = default);

    Task SaveSessionAsync(string projectKey, IdeSessionState state, CancellationToken ct = default);

    Task<string?> GetPreferenceAsync(string key, CancellationToken ct = default);

    Task SetPreferenceAsync(string key, string json, CancellationToken ct = default);
}
