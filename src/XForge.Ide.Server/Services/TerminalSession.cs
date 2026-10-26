using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using XForge.Ide.Server.Hubs;

namespace XForge.Ide.Server.Services;

/// <summary>
/// Sessão de terminal durável: ConPTY + fan-out da saída para os viewers (SignalR).
/// O fan-out usa IHubContext (singleton) porque instâncias de hub são descartadas
/// ao fim de cada invocação — capturar `Clients` do hub causaria ObjectDisposedException.
/// </summary>
public sealed class TerminalSession : IAsyncDisposable
{
    private readonly ILogger _log;
    private readonly IHubContext<TerminalHub> _hub;
    private readonly object _writeLock = new();
    private ConPtySession? _pty;
    private readonly List<Action<string>> _viewers = new();

    public string ClientId { get; }
    public string WorkingDirectory { get; }

    public TerminalSession(string clientId, string workingDirectory, ILogger log, IHubContext<TerminalHub> hub)
    {
        ClientId = clientId;
        WorkingDirectory = workingDirectory;
        _log = log;
        _hub = hub;
    }

    /// <summary>Cria o PTY (idempotente). Chamado pelo manager na primeira aba.</summary>
    public void Start()
    {
        lock (_writeLock)
        {
            if (_pty is not null)
            {
                return;
            }
            _pty = new ConPtySession(WorkingDirectory, _log);
        }

        _pty.StartAsync(data =>
        {
            List<Action<string>> snapshot;
            lock (_writeLock)
            {
                snapshot = new List<Action<string>>(_viewers);
            }
            foreach (var viewer in snapshot)
            {
                try
                {
                    viewer(data);
                }
                catch
                {
                    // viewer desconectou
                }
            }
        }).GetAwaiter().GetResult();
    }

    public bool IsRunning => _pty is not null && !_pty.Exited.IsCompleted;

    /// <summary>Registra uma conexão SignalR para receber a saída. Retorna handle para remover.</summary>
    public Action AddViewer()
    {
        // envia para o grupo desta sessão usando o hub context (sobrevive à invocação)
        Action<string> onData = data =>
        {
            _ = _hub.Clients.Group($"term-{ClientId}").SendAsync("Data", data);
        };

        lock (_writeLock)
        {
            _viewers.Add(onData);
        }
        return () =>
        {
            lock (_writeLock)
            {
                _viewers.Remove(onData);
            }
        };
    }

    public void Write(string data)
    {
        lock (_writeLock)
        {
            _pty?.Write(data);
        }
    }

    public void Resize(int cols, int rows)
    {
        lock (_writeLock)
        {
            _pty?.Resize(cols, rows);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pty is not null)
        {
            await _pty.DisposeAsync();
            _pty = null;
        }
        lock (_writeLock)
        {
            _viewers.Clear();
        }
    }
}
