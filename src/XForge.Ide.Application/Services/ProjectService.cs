using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using XForge.Ide.Core.Results;
using Dto = XForge.Ide.Application.Dto;

namespace XForge.Ide.Application.Services;

/// <summary>Casos de uso de projetos da IDE.</summary>
public class ProjectService
{
    private readonly IProjectRepository _projects;

    public ProjectService(IProjectRepository projects) => _projects = projects;

    public async Task<IReadOnlyList<Dto.ProjectDto>> ListAsync(CancellationToken ct = default)
    {
        var items = await _projects.ListAsync(ct);
        return items
            .OrderByDescending(p => p.LastOpenedAt ?? p.CreatedAt)
            .Select(Dto.ProjectDto.From)
            .ToList();
    }

    public async Task<Dto.ProjectDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _projects.GetAsync(id, ct);
        return p is null ? null : Dto.ProjectDto.From(p);
    }

    public async Task<Core.Results.Result<Dto.ProjectDto>> CreateAsync(
        string name, string path, string? templateKind, string? description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Core.Results.Result<Dto.ProjectDto>.Failure("Nome do projeto é obrigatório.");

        if (string.IsNullOrWhiteSpace(path))
            return Core.Results.Result<Dto.ProjectDto>.Failure("Caminho do projeto é obrigatório.");

        var fullPath = System.IO.Path.GetFullPath(path);
        if (!System.IO.Path.IsPathRooted(fullPath))
            return Core.Results.Result<Dto.ProjectDto>.Failure("Caminho deve ser absoluto.");

        // Recusa caminhos inexistentes: cria-los geraria projetos vazios (registros orfaos).
        if (!Directory.Exists(fullPath))
            return Core.Results.Result<Dto.ProjectDto>.Failure(
                $"Diretório não encontrado: '{fullPath}'. Crie a pasta ou selecione um diretório existente.");

        var existing = await _projects.GetByPathAsync(fullPath, ct);
        if (existing is not null)
            return Core.Results.Result<Dto.ProjectDto>.Failure(
                $"Já existe um projeto registrado neste caminho: '{existing.Name}'.");

        try
        {
            Directory.CreateDirectory(fullPath);
        }
        catch (Exception ex)
        {
            return Core.Results.Result<Dto.ProjectDto>.Failure($"Não foi possível criar o diretório: {ex.Message}");
        }

        var project = new Project
        {
            Name = name.Trim(),
            Path = fullPath,
            TemplateKind = string.IsNullOrWhiteSpace(templateKind) ? "clean-architecture" : templateKind!,
            Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim(),
            LastOpenedAt = DateTime.UtcNow,
        };

        await _projects.AddAsync(project, ct);
        return Core.Results.Result<Dto.ProjectDto>.Success(Dto.ProjectDto.From(project));
    }

    public async Task<Core.Results.Result<Dto.ProjectDto>> OpenAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _projects.GetAsync(id, ct);
        if (p is null)
            return Core.Results.Result<Dto.ProjectDto>.Failure("Projeto não encontrado.");
        if (!Directory.Exists(p.Path))
            return Core.Results.Result<Dto.ProjectDto>.Failure($"Diretório do projeto não existe: {p.Path}");

        p.LastOpenedAt = DateTime.UtcNow;
        await _projects.UpdateAsync(p, ct);
        return Core.Results.Result<Dto.ProjectDto>.Success(Dto.ProjectDto.From(p));
    }

    public async Task<Core.Results.Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _projects.GetAsync(id, ct);
        if (p is null)
            return Core.Results.Result.Failure("Projeto não encontrado.");

        await _projects.DeleteAsync(id, ct);
        return Core.Results.Result.Success();
    }
}
