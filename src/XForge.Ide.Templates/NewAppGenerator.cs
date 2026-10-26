using System.Text;
using XForge.Ide.Templates.Templates;

namespace XForge.Ide.Templates;

/// <summary>
/// Gera a solu??o Clean Architecture completa (Domain/Application/Infrastructure/Api/Client
/// + testes) a partir das op??es do wizard. Templates em C# tipado (decis?o M-13).
/// </summary>
public sealed class NewAppGenerator
{
    /// <summary>Monta a lista completa de arquivos (caminho relativo + conte?do).</summary>
    public IReadOnlyList<GeneratedFile> Generate(NewAppOptions options)
    {
        var ns = options.RootNamespace;
        var files = new List<GeneratedFile>
        {
            // Raiz da solution
            new($"{ns}.sln", ProjectFilesTemplates.Solution(options, new[]
            {
                $"{ns}.Domain", $"{ns}.Application", $"{ns}.Infrastructure", $"{ns}.Api", $"{ns}.Client", $"{ns}.Tests",
            })),
            new("Directory.Build.props", ProjectFilesTemplates.DirectoryBuildProps(options)),

            new(".gitignore", ProjectFilesTemplates.GitIgnore(options)),
            new("README.md", ProjectFilesTemplates.Readme(options)),

            // Domain
            new($"src/{ns}.Domain/{ns}.Domain.csproj", LayerTemplates.DomainCsproj(options)),
            new($"src/{ns}.Domain/Common/BaseEntity.cs", LayerTemplates.BaseEntity(options)),
            new($"src/{ns}.Domain/Common/Result.cs", LayerTemplates.DomainResult(options)),

            // Application
            new($"src/{ns}.Application/{ns}.Application.csproj", LayerTemplates.ApplicationCsproj(options)),
            new($"src/{ns}.Application/Abstractions/IRepository.cs", LayerTemplates.IRepository(options)),
            new($"src/{ns}.Application/Messaging/Messaging.cs", LayerTemplates.Messaging(options)),
            new($"src/{ns}.Application/DependencyInjection.cs", LayerTemplates.ApplicationDependencyInjection(options)),

            // Infrastructure
            new($"src/{ns}.Infrastructure/{ns}.Infrastructure.csproj", LayerTemplates.InfrastructureCsproj(options)),
            new($"src/{ns}.Infrastructure/Persistence/{ns}DbContext.cs", LayerTemplates.DbContext(options)),
            new($"src/{ns}.Infrastructure/Persistence/Repository.cs", LayerTemplates.RepositoryImpl(options)),
            new($"src/{ns}.Infrastructure/Persistence/{ns}DbContextFactory.cs", LayerTemplates.DesignTimeFactory(options)),
            new($"src/{ns}.Infrastructure/DependencyInjection.cs", LayerTemplates.InfrastructureDependencyInjection(options)),

            // Api
            new($"src/{ns}.Api/{ns}.Api.csproj", ApiClientTemplates.ApiCsproj(options)),
            new($"src/{ns}.Api/Program.cs", ApiClientTemplates.ApiProgram(options)),
            new($"src/{ns}.Api/appsettings.json", ApiClientTemplates.ApiAppSettings(options)),
            new($"src/{ns}.Api/appsettings.Development.json", ApiClientTemplates.ApiAppSettingsDev(options)),
            new($"src/{ns}.Api/Properties/launchSettings.json", ApiClientTemplates.ApiLaunchSettings(options)),

            // Client (Blazor WASM)
            new($"src/{ns}.Client/{ns}.Client.csproj", ApiClientTemplates.ClientCsproj(options)),
            new($"src/{ns}.Client/Program.cs", ApiClientTemplates.ClientProgram(options)),
            new($"src/{ns}.Client/App.razor", ApiClientTemplates.ClientAppRazor(options)),
            new($"src/{ns}.Client/_Imports.razor", ApiClientTemplates.ClientImports(options)),
            new($"src/{ns}.Client/Services/{ns}ApiClient.cs", ApiClientTemplates.ClientApiClient(options)),
            new($"src/{ns}.Client/wwwroot/index.html", ApiClientTemplates.ClientIndexHtml(options)),
            new($"src/{ns}.Client/wwwroot/appsettings.json", ApiClientTemplates.ClientAppSettings(options)),
            new($"src/{ns}.Client/Properties/launchSettings.json", ApiClientTemplates.ClientLaunchSettings(options)),

            // Tests
            new($"tests/{ns}.Tests/{ns}.Tests.csproj", ApiClientTemplates.TestsCsproj(options)),
            new($"tests/{ns}.Tests/SmokeTests.cs", ApiClientTemplates.SmokeTest(options)),
        };

        if (options.UseIdentity)
        {
            files.Add(new($"src/{ns}.Infrastructure/Identity/ApplicationUser.cs", LayerTemplates.ApplicationUser(options)));
            files.Add(new($"src/{ns}.Api/Auth/Jwt.cs", ApiClientTemplates.JwtOptions(options)));
            files.Add(new($"src/{ns}.Api/Endpoints/AccountEndpoints.cs", ApiClientTemplates.AccountEndpoints(options)));
        }

        return files;
    }

    /// <summary>
    /// Escreve os arquivos em disco. Retorna o caminho completo da solution.
    /// Diret?rio existente com conte?do exige <paramref name="overwriteFiles"/>.
    /// </summary>
    public string WriteToDisk(NewAppOptions options, bool overwriteFiles = false)
    {
        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new InvalidOperationException("Diret?rio de sa?da n?o informado.");
        }

        var root = Path.GetFullPath(Path.Combine(options.OutputPath, options.RootNamespace));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !overwriteFiles)
        {
            throw new InvalidOperationException($"Diret?rio j? existe e n?o est? vazio: {root}");
        }

        Directory.CreateDirectory(root);

        foreach (var file in Generate(options))
        {
            var fullPath = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(fullPath, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return root;
    }
}
