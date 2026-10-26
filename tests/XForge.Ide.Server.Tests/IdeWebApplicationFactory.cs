using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using XForge.Ide.Infrastructure.Persistence;

namespace XForge.Ide.Server.Tests;

/// <summary>
/// Factory de integração com banco SQLite isolado por teste/classe.
/// </summary>
public sealed class IdeWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        "ide-server-tests-" + Guid.NewGuid().ToString("N")[..8] + ".db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Ide:DatabasePath", _databasePath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
            // Algum processo externo pode manter o arquivo — não falha o teste por isso.
        }
    }
}
