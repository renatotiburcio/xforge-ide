using XForge.Ide.Core.Entities;

namespace XForge.Ide.Application.Dto;

/// <summary>DTO de projeto para a UI/API.</summary>
public record ProjectDto(
    Guid Id,
    string Name,
    string Path,
    string TemplateKind,
    string? Description,
    DateTime CreatedAt,
    DateTime? LastOpenedAt)
{
    public static ProjectDto From(Project p) => new(
        p.Id, p.Name, p.Path, p.TemplateKind, p.Description, p.CreatedAt, p.LastOpenedAt);
}

/// <summary>Requisição de criação de projeto.</summary>
public record CreateProjectRequest(
    string Name,
    string Path,
    string? TemplateKind,
    string? Description);
