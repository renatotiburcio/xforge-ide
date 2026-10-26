using System.Diagnostics;
using System.Text;

namespace XForge.Ide.Application.Services;

/// <summary>
/// Alvos resolvidos de um projeto aberto: projeto de Infrastructure (onde vivem as
/// migrations) e projeto de Api (startup, que carrega a connection string).
/// </summary>
public sealed record MigTarget(
    string Root,
    string InfraProject,
    string ApiProject,
    string ContextName,
    bool FromConvention)
{
    /// <summary>Projeto de Infrastructure relativo à raiz (barras normais).</summary>
    public string InfraRelative => ToRelative(Root, InfraProject);

    /// <summary>Projeto de Api relativo à raiz (barras normais).</summary>
    public string ApiRelative => ToRelative(Root, ApiProject);

    private static string ToRelative(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
        try
        {
            var full = Path.GetFullPath(path);
            var rel = Path.GetRelativePath(Path.GetFullPath(root), full);
            return rel.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return path.Replace('\\', '/');
        }
    }
}

/// <summary>Estado detectado do projeto para o fluxo de migrations.</summary>
public sealed record MigState(
    bool ToolInstalled,
    string ToolVersion,
    string ToolMessage,
    bool DesignReferenced,
    bool HasMigration,
    string? MigrationPath,
    bool DatabaseExists,
    string? DatabasePath,
    DateTime? DatabaseWrittenAt)
{
    public bool Ready => ToolInstalled && DesignReferenced && HasMigration && DatabaseExists;
}

/// <summary>Informações exibidas ao trocar de provider (pacote + connection string).</summary>
public sealed record MigProviderInfo(
    string Id,
    string Label,
    string Package,
    string ConnectionString,
    string Note);

/// <summary>
/// Descobre os projetos por convenção e detecta o estado do fluxo de migrations.
/// Não executa nada destrutivo — somente leitura de disco e `dotnet ef --version`.
/// </summary>
public static class MigrationRunner
{
    /// <summary>Provedores suportados pelo gerador (mesma ordem do MigrationService).</summary>
    public static IReadOnlyList<MigProviderInfo> Providers { get; } = new[]
    {
        new MigProviderInfo(
            "Sqlite",
            "SQLite",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Data Source=app.db",
            "Banco em arquivo local — detectável pela IDE (procura *.db na pasta do projeto)."),
        new MigProviderInfo(
            "PostgreSql",
            "PostgreSQL",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "Host=localhost;Port=5432;Database={app};Username=postgres;Password=postgres",
            "Servidor externo — a IDE não consegue verificar o banco localmente."),
        new MigProviderInfo(
            "MySql",
            "MySQL",
            "MySql.EntityFrameworkCore",
            "Server=localhost;Port=3306;Database={app};User=root;Password=",
            "Servidor externo — a IDE não consegue verificar o banco localmente."),
    };

    /// <summary>Retorna as informações do provider pelo id (Sqlite por padrão).</summary>
    public static MigProviderInfo Provider(string? id)
        => Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? Providers[0];

    /// <summary>
    /// Resolve a raiz do projeto (aceita o caminho do .sln ou do diretório).
    /// </summary>
    public static string? ResolveRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        try
        {
            var full = Path.GetFullPath(path.Trim());
            if (File.Exists(full))
            {
                return Path.GetDirectoryName(full);
            }
            return Directory.Exists(full) ? full : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Descobre os projetos por convenção: pasta <c>src/</c>, projeto com sufixo
    /// <c>.Infrastructure</c> (migrations) e <c>.Api</c> (startup).
    /// </summary>
    public static MigTarget? Discover(string? rootPath)
    {
        var root = ResolveRoot(rootPath);
        if (root is null)
        {
            return null;
        }

        var ns = GuessNamespace(root);
        var infra = FindProject(root, ".Infrastructure");
        var api = FindProject(root, ".Api");
        if (infra is null || api is null)
        {
            return null;
        }

        return new MigTarget(root, infra, api, ns + "DbContext", true);
    }

    /// <summary>Monta o alvo a partir de escolhas manuais do usuário (caminhos relativos à raiz).</summary>
    public static MigTarget Manual(string? rootPath, string? infraProject, string? apiProject, string contextName)
    {
        var root = ResolveRoot(rootPath) ?? ".";
        return new MigTarget(
            root,
            ResolveAgainst(root, infraProject),
            ResolveAgainst(root, apiProject),
            string.IsNullOrWhiteSpace(contextName) ? GuessNamespace(root) + "DbContext" : contextName,
            false);
    }

    /// <summary>Resolve um caminho relativo à raiz do projeto (aceita já absoluto).</summary>
    private static string ResolveAgainst(string root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
        try
        {
            var trimmed = path.Trim().Replace('/', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(root, trimmed));
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    /// <summary>Todos os .csproj do projeto (para o seletor manual), relativos à raiz.</summary>
    public static IReadOnlyList<string> ListProjects(string? rootPath)
    {
        var root = ResolveRoot(rootPath);
        if (root is null)
        {
            return Array.Empty<string>();
        }
        try
        {
            return Directory
                .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !IsInBuildFolder(p))
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Testa se o dotnet-ef está instalado (global) e captura a versão.</summary>
    public static (bool Installed, string Version, string Message) CheckTool() => RunCapture("ef --version");

    /// <summary>Detecta o estado completo do fluxo para o alvo informado.</summary>
    public static MigState Detect(MigTarget? target)
    {
        if (target is null)
        {
            return new MigState(false, string.Empty, "Projeto não resolvido.", false, false, null, false, null, null);
        }

        var tool = CheckTool();

        var design = false;
        var hasMigration = false;
        string? migrationPath = null;

        if (!string.IsNullOrWhiteSpace(target.InfraProject))
        {
            design = HasPackageReference(target.InfraProject, "Microsoft.EntityFrameworkCore.Design");

            var migrationsDir = Path.Combine(Path.GetDirectoryName(target.InfraProject) ?? ".", "Migrations");
            if (Directory.Exists(migrationsDir))
            {
                var generated = SafeEnumerate(migrationsDir, "*.cs")
                    .Where(f => !f.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (generated.Length > 0)
                {
                    hasMigration = true;
                    migrationPath = migrationsDir;
                }
            }
        }

        var (dbExists, dbPath, dbWritten) = FindDatabase(target.Root);

        return new MigState(
            tool.Installed,
            tool.Version,
            tool.Message,
            design,
            hasMigration,
            migrationPath,
            dbExists,
            dbPath,
            dbWritten);
    }

    /// <summary>Pacote do provider já referenciado no .csproj?</summary>
    public static bool HasProviderPackage(MigTarget? target, string package)
        => target is not null
           && !string.IsNullOrWhiteSpace(target.InfraProject)
           && HasPackageReference(target.InfraProject, package);

    /// <summary>
    /// Procura o arquivo de banco SQLite dentro do projeto (ignora bin/obj).
    /// </summary>
    public static (bool Exists, string? Path, DateTime? WrittenAt) FindDatabase(string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return (false, null, null);
            }
            var candidates = SafeEnumerate(root, "*.db")
                .Concat(SafeEnumerate(root, "*.sqlite"))
                .Concat(SafeEnumerate(root, "*.sqlite3"))
                .ToArray();
            if (candidates.Length == 0)
            {
                return (false, null, null);
            }
            var best = candidates
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .First();
            return (true, best.FullName, best.LastWriteTime);
        }
        catch (Exception)
        {
            return (false, null, null);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string? FindProject(string root, string suffix)
    {
        foreach (var dir in new[] { Path.Combine(root, "src"), root })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }
            string? found = null;
            foreach (var sub in SafeEnumerateDirs(dir))
            {
                var name = Path.GetFileName(sub);
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    found = sub;
                    break;
                }
            }
            if (found is null)
            {
                continue;
            }
            var csproj = Directory
                .EnumerateFiles(found, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (csproj is not null)
            {
                return Path.GetFullPath(csproj);
            }
        }
        return null;
    }

    private static string GuessNamespace(string root)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        var clean = new string((name ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return clean.Length == 0 ? "App" : clean;
    }

    private static bool HasPackageReference(string csprojPath, string package)
    {
        try
        {
            if (!File.Exists(csprojPath))
            {
                return false;
            }
            var text = File.ReadAllText(csprojPath);
            return text.Contains(package, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsInBuildFolder(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p =>
            p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SafeEnumerate(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .Where(p => !IsInBuildFolder(p))
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateDirs(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir).ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static (bool Installed, string Version, string Message) RunCapture(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return (false, string.Empty, "Falha ao iniciar dotnet.");
            }

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(120_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // melhor esforço
                }
                return (false, string.Empty, "Tempo excedido ao verificar.");
            }

            var stdout = outTask.Result.Trim();
            var stderr = errTask.Result.Trim();
            return process.ExitCode == 0
                ? (true, stdout, stdout)
                : (false, string.Empty, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }
}
