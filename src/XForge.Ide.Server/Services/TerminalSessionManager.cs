using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using XForge.Ide.Server.Hubs;

namespace XForge.Ide.Server.Services;

/// <summary>
/// Mantém uma sessão ConPTY por painel de terminal (clientId = id da aba no navegador).
/// Múltiplos clientes SignalR podem assistir a mesma sessão (hub group por clientId).
/// </summary>
public sealed class TerminalSessionManager
{
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new(StringComparer.Ordinal);
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHubContext<TerminalHub> _hub;

    public TerminalSessionManager(ILoggerFactory loggerFactory, IHubContext<TerminalHub> hub)
    {
        _loggerFactory = loggerFactory;
        _hub = hub;
    }

    public TerminalSession GetOrCreate(string clientId, string workingDirectory)
    {
        return _sessions.GetOrAdd(clientId, id =>
        {
            var session = new TerminalSession(
                id,
                workingDirectory,
                _loggerFactory.CreateLogger<TerminalSession>(),
                _hub);
            session.Start();
            return session;
        });
    }

    public void Write(string clientId, string data)
    {
        if (_sessions.TryGetValue(clientId, out var s))
        {
            s.Write(data);
        }
    }

    public void Resize(string clientId, int cols, int rows)
    {
        if (_sessions.TryGetValue(clientId, out var s))
        {
            s.Resize(cols, rows);
        }
    }

    public void DisposeSession(string clientId)
    {
        if (_sessions.TryRemove(clientId, out var s))
        {
            s.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
    }

    public Task DisposeAllAsync()
    {
        foreach (var kv in _sessions)
        {
            _sessions.TryRemove(kv.Key, out _);
            kv.Value.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        return Task.CompletedTask;
    }
}
