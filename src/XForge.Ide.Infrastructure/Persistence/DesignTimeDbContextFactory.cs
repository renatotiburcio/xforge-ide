using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace XForge.Ide.Infrastructure.Persistence;

/// <summary>
/// Fábrica usada pelas ferramentas de design (dotnet-ef migrations).
/// </summary>
public class IdeDbContextFactory : IDesignTimeDbContextFactory<IdeDbContext>
{
    public IdeDbContext CreateDbContext(string[] args)
    {
        var dbPath = ResolveDatabasePath();
        var options = new DbContextOptionsBuilder<IdeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new IdeDbContext(options);
    }

    public static string ResolveDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "XForge", "ide", "ide.db");
    }
}
