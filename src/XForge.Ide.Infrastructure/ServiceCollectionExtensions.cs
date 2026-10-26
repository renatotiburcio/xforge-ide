using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Infrastructure.Introspection;
using XForge.Ide.Infrastructure.Persistence;

namespace XForge.Ide.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra o banco da IDE (SQLite) e repositórios.
    /// </summary>
    /// <param name="databasePath">Caminho absoluto do arquivo .db.</param>
    public static IServiceCollection AddIdeInfrastructure(this IServiceCollection services, string databasePath)
    {
        var directory = System.IO.Path.GetDirectoryName(databasePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("Caminho do banco da IDE inválido: " + databasePath);
        }
        Directory.CreateDirectory(directory);

        services.AddDbContext<IdeDbContext>(opt => opt
            .UseSqlite($"Data Source={databasePath}")
            .EnableSensitiveDataLogging(false));

        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IDictionaryRepository, DictionaryRepository>();
        services.AddScoped<IWizardRunRepository, WizardRunRepository>();
        services.AddScoped<IDatabaseIntrospector, SqliteIntrospector>();
        services.AddScoped<IIdeSessionRepository, IdeSessionRepository>();
        services.AddScoped<IIdePreferenceRepository, IdePreferenceRepository>();
        services.AddScoped<XForge.Ide.Application.Services.IIdeSessionStore, IdeSessionStore>();
        return services;
    }
}
