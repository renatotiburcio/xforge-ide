using XForge.Ide.Application.Services;

namespace XForge.Ide.Server.Endpoints;

/// <summary>
/// API REST de arquivos — escopo restrito aos diretórios de projetos registrados
/// (regra de segurança: nunca acesso arbitrário ao filesystem).
/// </summary>
public static class FileEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/files").WithTags("Files");

        group.MapGet("/", async (string path, FileSystemService files, HttpContext http) =>
        {
            if (!await files.IsInsideWorkspaceAsync(path))
            {
                return Results.Json(new { error = "Caminho fora do workspace da IDE." }, statusCode: 403);
            }
            var nodes = await files.ListAsync(path);
            return Results.Ok(nodes);
        });

        group.MapGet("/content", async (string path, FileSystemService files) =>
        {
            if (!await files.IsInsideWorkspaceAsync(path))
            {
                return Results.Json(new { error = "Caminho fora do workspace da IDE." }, statusCode: 403);
            }
            var content = await files.ReadAsync(path);
            return content is null ? Results.NotFound() : Results.Text(content, "text/plain");
        });

        group.MapPut("/content", async (SaveFileRequest request, FileSystemService files) =>
        {
            if (!await files.IsInsideWorkspaceAsync(request.Path))
            {
                return Results.Json(new { error = "Caminho fora do workspace da IDE." }, statusCode: 403);
            }
            await files.WriteAsync(request.Path, request.Content);
            return Results.NoContent();
        });
    }
}

public sealed record SaveFileRequest(string Path, string Content);
