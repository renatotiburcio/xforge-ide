using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Xunit;

namespace XForge.Ide.E2E.Tests;

/// <summary>
/// E2E automatizado: sobe a IDE em banco/porta efêmeros e executa o gate real de
/// UI/acessibilidade (`scripts/qa-ui-a11y.mjs`) contra ela.
/// </summary>
public class UiGateE2ETests
{
    [Fact]
    public async Task Ui_gate_passes_against_running_server()
    {
        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Não foi possível localizar a raiz do repositório ide/.");

        var serverDll = Path.Combine(repoRoot, "src", "XForge.Ide.Server", "bin", "Debug", "net10.0", "XForge.Ide.Server.dll");
        var gateScript = Path.Combine(repoRoot, "scripts", "qa-ui-a11y.mjs");

        File.Exists(serverDll).Should().BeTrue($"Servidor compilado ausente em {serverDll}");
        File.Exists(gateScript).Should().BeTrue($"Gate de UI ausente em {gateScript}");

        var port = GetFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var databasePath = Path.Combine(Path.GetTempPath(), "ide-e2e-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        var diagnosticsPath = Path.Combine(Path.GetTempPath(), "ide-e2e-gate-" + Guid.NewGuid().ToString("N")[..8] + ".log");

        using var server = StartServer(serverDll, baseUrl, databasePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await WaitForServerAsync(baseUrl, timeout.Token);

            var gate = StartGate(gateScript, baseUrl, repoRoot);
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            gate.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            gate.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            gate.BeginOutputReadLine();
            gate.BeginErrorReadLine();

            try
            {
                await gate.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!gate.WaitForExit(2000))
                {
                    gate.Kill(entireProcessTree: true);
                }

                Assert.Fail("Gate de UI excedeu o tempo limite.\n" +
                    $"exit={gate.ExitCode} hasExited={gate.HasExited}\n" +
                    $"diagnóstico completo: {diagnosticsPath}\n" +
                    Truncate(stdout + "\n" + stderr, 6000));
            }

            if (gate.ExitCode != 0)
            {
                File.WriteAllText(diagnosticsPath, stdout + "\n" + stderr);
                Assert.Fail("Gate de UI falhou:\n" +
                    $"diagnóstico completo: {diagnosticsPath}\n" +
                    Truncate(stdout + "\n" + stderr, 6000));
            }
        }
        finally
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
            }

            try
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
            }
            catch (IOException)
            {
                // Ignorado: o processo foi encerrado e o arquivo pode ficar preso em SO.
            }
        }
    }

    private static Process StartServer(string serverDll, string baseUrl, string databasePath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(serverDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(serverDll);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(baseUrl);
        startInfo.EnvironmentVariables["Ide__DatabasePath"] = databasePath;
        startInfo.EnvironmentVariables["DOTNET_ENVIRONMENT"] = "Development";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o servidor da IDE.");

        return process;
    }

    private static Process StartGate(string gateScript, string baseUrl, string repoRoot)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(gateScript);
        startInfo.EnvironmentVariables["IDE_URL"] = baseUrl;

        // Em máquinas de desenvolvimento, o Chrome costuma estar sobrecarregado.
        // O Edge é mais previsível para o gate headless.
        var edgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "Edge", "Application", "msedge.exe");
        if (File.Exists(edgePath))
        {
            startInfo.EnvironmentVariables["CHROME_PATH"] = edgePath;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o gate de UI.");

        return process;
    }

    private static async Task WaitForServerAsync(string baseUrl, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        for (var attempt = 0; attempt < 60; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var response = await client.GetAsync(baseUrl + "/home", ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // servidor ainda iniciando
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // timeout do HttpClient; tenta novamente
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        throw new InvalidOperationException($"Servidor não respondeu em {baseUrl}/home.");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "XForge.Ide.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "\n… (saída truncada)";
}
