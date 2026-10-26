using XForge.Ide.Application.Dto;
using XForge.Ide.Application.Services;
using XForge.Ide.Core.Enums;

namespace XForge.Ide.Server.Endpoints;

/// <summary>
/// API REST de wizards (consumida por clientes externos — Electron, MCP, testes E2E).
/// </summary>
public static class WizardEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/wizards").WithTags("Wizards");

        group.MapPost("/new-app", async (NewAppWizardApiRequest request, ScaffoldingService scaffolding) =>
        {
            var result = await scaffolding.GenerateNewAppAsync(new NewAppWizardRequest(
                request.ProjectId,
                request.DisplayName,
                request.OutputPath,
                Enum.TryParse<ProviderKind>(request.Provider, ignoreCase: true, out var provider)
                    ? provider
                    : ProviderKind.Sqlite,
                request.ConnectionString,
                request.UseIdentity,
                request.ValidateBuild,
                request.Description));

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        });
    }
}

public sealed record NewAppWizardApiRequest(
    Guid? ProjectId,
    string DisplayName,
    string OutputPath,
    string Provider,
    string? ConnectionString,
    bool UseIdentity,
    bool ValidateBuild,
    string? Description);
