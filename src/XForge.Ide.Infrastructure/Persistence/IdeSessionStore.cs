using System.Text.Json;
using XForge.Ide.Application.Services;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

/// <summary>
/// Implementação do store de sessão/preferências sobre SQLite (EF Core).
/// </summary>
public sealed class IdeSessionStore : IIdeSessionStore
{
    private readonly IIdeSessionRepository _sessions;
    private readonly IIdePreferenceRepository _preferences;

    public IdeSessionStore(IIdeSessionRepository sessions, IIdePreferenceRepository preferences)
    {
        _sessions = sessions;
        _preferences = preferences;
    }

    public async Task<IdeSessionState> GetSessionAsync(string projectKey, CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(projectKey, ct);
        if (session is null)
        {
            return IdeSessionState.Empty;
        }

        IReadOnlyList<string> tabs;
        try
        {
            tabs = JsonSerializer.Deserialize<string[]>(session.TabsJson) ?? Array.Empty<string>();
        }
        catch
        {
            tabs = Array.Empty<string>();
        }

        return new IdeSessionState(tabs, session.ActivePath, session.PanelHeight, session.PanelOpen, session.TerminalCount);
    }

    public Task SaveSessionAsync(string projectKey, IdeSessionState state, CancellationToken ct = default)
    {
        var session = new IdeSession
        {
            Key = projectKey,
            TabsJson = JsonSerializer.Serialize(state.Tabs),
            ActivePath = state.ActivePath,
            PanelHeight = state.PanelHeight,
            PanelOpen = state.PanelOpen,
            TerminalCount = state.TerminalCount,
        };
        return _sessions.SaveAsync(session, ct);
    }

    public Task<string?> GetPreferenceAsync(string key, CancellationToken ct = default)
        => _preferences.GetAsync(key, ct);

    public Task SetPreferenceAsync(string key, string json, CancellationToken ct = default)
        => _preferences.SetAsync(key, json, ct);
}
