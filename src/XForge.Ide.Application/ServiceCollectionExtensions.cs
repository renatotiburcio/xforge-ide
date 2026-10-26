using Microsoft.Extensions.DependencyInjection;
using XForge.Ide.Application.Services;
using XForge.Ide.Application.Services.Ai;

namespace XForge.Ide.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdeApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectService>();
        services.AddScoped<DictionaryService>();
        services.AddScoped<ScaffoldingService>();
        services.AddScoped<FileSystemService>();
        services.AddScoped<SearchService>();

        // Migrations (DDL por provedor a partir do dicionário)
        services.AddScoped<MigrationService>();

        // Assistente de IA (workspace-aware)
        services.AddSingleton<AiSettingsStore>();
        services.AddSingleton<AiClientFactory>();
        services.AddScoped<AiAssistantService>();
        services.AddScoped<WorkspaceContextBuilder>();
        services.AddHttpClient("ai", c =>
        {
            c.Timeout = TimeSpan.FromMinutes(3);
        });

        return services;
    }
}
