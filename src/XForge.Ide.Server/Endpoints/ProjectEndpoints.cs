using XForge.Ide.Application.Dto;
using XForge.Ide.Application.Services;

namespace XForge.Ide.Server.Endpoints;

/// <summary>
/// API REST de projetos (consumida por clientes externos — Electron host, MCP, testes).
/// A UI Blazor usa ProjectService via injeção direta (InteractiveServer).
/// </summary>
public static class ProjectEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", async (ProjectService projects) =>
            Results.Ok(await projects.ListAsync()));

        group.MapGet("/{id:guid}", async (Guid id, ProjectService projects) =>
        {
            var p = await projects.GetAsync(id);
            return p is null ? Results.NotFound() : Results.Ok(p);
        });

        group.MapPost("/", async (CreateProjectRequest request, ProjectService projects) =>
        {
            var result = await projects.CreateAsync(request.Name, request.Path, request.TemplateKind, request.Description);
            return result.IsSuccess ? Results.Created($"/api/projects/{result.Value.Id}", result.Value) : Results.BadRequest(new { error = result.Error });
        });

        group.MapPost("/{id:guid}/open", async (Guid id, ProjectService projects) =>
        {
            var result = await projects.OpenAsync(id);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ProjectService projects) =>
        {
            var result = await projects.DeleteAsync(id);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
        });
    }
}
