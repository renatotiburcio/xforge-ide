using Microsoft.AspNetCore.SignalR;
using XForge.Ide.Application.Services;
using XForge.Ide.Server.Services;

namespace XForge.Ide.Server.Hubs;

/// <summary>
/// Terminal integrado: ponte SignalR entre o xterm.js do navegador e a sessão ConPTY.
/// Cada aba de terminal do cliente usa um <c>clientId</c> próprio (id da aba).
/// </summary>
public sealed class TerminalHub : Hub
{
    private readonly TerminalSessionManager _sessions;
    private readonly FileSystemService _files;
    private readonly ILogger<TerminalHub> _log;
    private readonly Dictionary<string, Action> _viewerHandles = new();

    public TerminalHub(TerminalSessionManager sessions, FileSystemService files, ILogger<TerminalHub> log)
    {
        _sessions = sessions;
        _files = files;
        _log = log;
    }

    private string? _clientId;

    /// <summary>Entra em uma sessão (criando o PTY) e passa a receber a saída.</summary>
    public async Task Join(string clientId, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("clientId obrigatório", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) ||
            !await _files.IsInsideWorkspaceAsync(workingDirectory))
        {
            throw new HubException("Diretório do terminal fora do workspace autorizado.");
        }

        _clientId = clientId;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"term-{clientId}");

        var session = _sessions.GetOrCreate(clientId, workingDirectory);
        var handle = session.AddViewer();
        _viewerHandles[clientId] = handle;
        _log.LogInformation("Terminal {Client}: sessão iniciada em {Cwd}", clientId, workingDirectory);
    }

    /// <summary>Envia teclas digitadas no terminal para o PTY.</summary>
    public void Input(string clientId, string data)
    {
        if (data.Length > 0)
        {
            _sessions.Write(clientId, data);
        }
    }

    /// <summary>Redimensiona o PTY conforme o tamanho real do painel xterm.</summary>
    public void Resize(string clientId, int cols, int rows)
    {
        if (cols > 0 && rows > 0)
        {
            _sessions.Resize(clientId, cols, rows);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var (clientId, handle) in _viewerHandles)
        {
            handle();
            // último viewer saindo → encerra o processo órfão
            _sessions.DisposeSession(clientId);
        }
        _viewerHandles.Clear();

        await base.OnDisconnectedAsync(exception);
    }
}
