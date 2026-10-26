using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace XForge.Ide.Application.Services;

/// <summary>Uma tarefa de build/run em execucao.</summary>
public sealed class BuildTask
{
    public required string Id { get; init; }
    public required string Command { get; init; }
    public required string WorkingDirectory { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public bool IsRunning { get; set; } = true;
    public int? ExitCode { get; set; }
}

/// <summary>
/// Executa dotnet build/run/test/watch dos projetos gerados e transmite a saida
/// em tempo real (barramento de eventos consumido pela UI Blazor e pelo painel).
/// </summary>
public sealed class BuildRunService
{
    private readonly ILogger<BuildRunService> _log;
    private readonly ConcurrentDictionary<string, (BuildTask Task, Process Process, CancellationTokenSource Cts)> _running = new();

    public BuildRunService(ILogger<BuildRunService> log) => _log = log;

    public IReadOnlyList<BuildTask> Running => _running.Values.Select(v => v.Task).ToList();

    public bool IsBusy => !_running.IsEmpty;

    /// <summary>Disparado a cada linha de saida (stdout/err). Tag = "out" ou "err".</summary>
    public event Action<string, string, string>? LineReceived;

    /// <summary>Disparado quando a tarefa termina (id, codigo de saida).</summary>
    public event Action<string, int>? Finished;

    /// <summary>
    /// Inicia um processo filho e transmite a saida. Retorna a tarefa criada.
    /// </summary>
    public async Task<BuildTask> RunAsync(
        string command,
        string workingDirectory,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        if (!Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException($"Diretorio nao encontrado: {workingDirectory}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var task = new BuildTask { Id = id, Command = command, WorkingDirectory = workingDirectory };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                LineReceived?.Invoke(id, "out", e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                LineReceived?.Invoke(id, "err", e.Data);
            }
        };
        process.Exited += (_, _) => OnExited(id, process);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Falha ao iniciar: dotnet {command}");
        }

        _running[id] = (task, process, cts);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _log.LogInformation("Build/Run {Id}: dotnet {Command} em {Cwd}", id, command, workingDirectory);
        LineReceived?.Invoke(id, "out", $"> dotnet {command}   ({workingDirectory})");
        await Task.CompletedTask;
        return task;
    }

    private void OnExited(string id, Process process)
    {
        if (_running.TryRemove(id, out var entry))
        {
            entry.Task.IsRunning = false;
            entry.Task.ExitCode = process.ExitCode;
            try
            {
                entry.Cts.Dispose();
            }
            catch
            {
            }
            LineReceived?.Invoke(id, "out", $"-- concluido com codigo {process.ExitCode} --");
            Finished?.Invoke(id, process.ExitCode);
        }
    }

    /// <summary>Interrompe uma tarefa em execucao (e a arvore de processos).</summary>
    public void Stop(string id)
    {
        if (!_running.TryGetValue(id, out var entry))
        {
            return;
        }
        try
        {
            if (!entry.Process.HasExited)
            {
                entry.Process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha ao interromper {Id}", id);
        }
        OnExited(id, entry.Process);
    }

    public void StopAll()
    {
        foreach (var id in _running.Keys.ToArray())
        {
            Stop(id);
        }
    }
}
