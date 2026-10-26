using System.Diagnostics;
using System.Text.Json;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using XForge.Ide.Core.Enums;
using XForge.Ide.Core.Results;
using XForge.Ide.Templates;
using Dto = XForge.Ide.Application.Dto;

namespace XForge.Ide.Application.Services;

/// <summary>
/// Wizard "Novo Aplicativo": gera a solution Clean Architecture completa
/// (Domain/Application/Infrastructure/Api/Client + testes, Identity opcional),
/// valida com dotnet build e registra o projeto na IDE.
/// </summary>
public class ScaffoldingService
{
    private readonly NewAppGenerator _generator = new();
    private readonly IWizardRunRepository _runs;
    private readonly IProjectRepository _projects;

    public ScaffoldingService(IWizardRunRepository runs, IProjectRepository projects)
    {
        _runs = runs;
        _projects = projects;
    }

    public async Task<Result<WizardResultDto>> GenerateNewAppAsync(NewAppWizardRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Result<WizardResultDto>.Failure("Nome do aplicativo é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            return Result<WizardResultDto>.Failure("Diretório de saída é obrigatório.");

        var ns = SanitizeNamespace(request.DisplayName);
        if (ns.Length == 0)
            return Result<WizardResultDto>.Failure("Nome inválido para namespace (use letras e números).");

        var options = new NewAppOptions
        {
            DisplayName = request.DisplayName.Trim(),
            RootNamespace = ns,
            OutputPath = request.OutputPath.Trim(),
            Provider = request.Provider,
            ConnectionString = string.IsNullOrWhiteSpace(request.ConnectionString)
                ? string.Empty
                : request.ConnectionString.Trim(),
            UseIdentity = request.UseIdentity,
            ValidateBuild = request.ValidateBuild,
            Description = request.Description,
        };

        var run = new WizardRun
        {
            ProjectId = request.ProjectId ?? Guid.Empty,
            WizardKind = "NewApp",
            InputsJson = JsonSerializer.Serialize(new
            {
                request.DisplayName, ns, request.OutputPath,
                Provider = request.Provider.ToString(), request.UseIdentity, request.ValidateBuild,
            }),
        };
        await _runs.AddAsync(run, ct);

        try
        {
            var solutionPath = _generator.WriteToDisk(options);

            var log = new List<string> { "Arquivos gerados em " + solutionPath };

            if (options.ValidateBuild)
            {
                var buildResult = await RunDotnetBuildAsync(solutionPath, log, ct);
                run.OutputJson = JsonSerializer.Serialize(new { solutionPath, buildSucceeded = buildResult, log });
                await _runs.UpdateAsync(run, ct);

                if (!buildResult)
                {
                    return Result<WizardResultDto>.Failure(
                        "Solution gerada, mas o build de validação falhou. Veja o log do wizard.");
                }
            }
            else
            {
                run.OutputJson = JsonSerializer.Serialize(new { solutionPath, buildSucceeded = (bool?)null, log });
                await _runs.UpdateAsync(run, ct);
            }

            // Registra o projeto na IDE (idempotente por caminho)
            var project = await _projects.GetByPathAsync(solutionPath, ct);
            if (project is null)
            {
                project = new Project
                {
                    Name = options.DisplayName,
                    Path = solutionPath,
                    TemplateKind = "clean-architecture",
                    Description = request.Description ?? $"Gerado pelo wizard New App ({options.Provider}).",
                    LastOpenedAt = DateTime.UtcNow,
                };
                await _projects.AddAsync(project, ct);
            }
            else
            {
                project.LastOpenedAt = DateTime.UtcNow;
                await _projects.UpdateAsync(project, ct);
            }

            return Result<WizardResultDto>.Success(new WizardResultDto(
                solutionPath, project.Id, BuildSucceeded: options.ValidateBuild, Log: log));
        }
        catch (Exception ex)
        {
            run.OutputJson = JsonSerializer.Serialize(new { error = ex.Message });
            await _runs.UpdateAsync(run, ct);
            return Result<WizardResultDto>.Failure("Falha na geração: " + ex.Message);
        }
    }

    /// <summary>Executa dotnet build na solution gerada e anexa o output ao log.</summary>
    private static async Task<bool> RunDotnetBuildAsync(string solutionPath, List<string> log, CancellationToken ct)
    {
        log.Add("Executando dotnet build (validação)...");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build -v q --nologo",
            WorkingDirectory = solutionPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var output = await outputTask;
        var error = await errorTask;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(8))
        {
            log.Add("  " + line.Trim());
        }
        if (process.ExitCode != 0)
        {
            foreach (var line in error.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(8))
            {
                log.Add("  [erro] " + line.Trim());
            }
        }

        log.Add(process.ExitCode == 0 ? "Build OK." : $"Build falhou (exit {process.ExitCode}).");
        return process.ExitCode == 0;
    }

    internal static string SanitizeNamespace(string name)
    {
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        var parts = new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ns = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return char.IsDigit(ns.FirstOrDefault()) ? "App" + ns : ns;
    }
}

/// <summary>Requisição do wizard New App.</summary>
public sealed record NewAppWizardRequest(
    Guid? ProjectId,
    string DisplayName,
    string OutputPath,
    ProviderKind Provider,
    string? ConnectionString,
    bool UseIdentity,
    bool ValidateBuild,
    string? Description);

/// <summary>Resultado do wizard.</summary>
public sealed record WizardResultDto(string SolutionPath, Guid ProjectId, bool? BuildSucceeded, IReadOnlyList<string> Log);
