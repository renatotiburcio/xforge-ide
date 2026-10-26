using XForge.Ide.Core.Enums;

namespace XForge.Ide.Templates.Templates;

/// <summary>Arquivos de solução: .sln, Directory.Build.props, Directory.Packages.props, .gitignore, README.</summary>
internal static class ProjectFilesTemplates
{
    private const string ProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

    public static string Solution(NewAppOptions o, IReadOnlyList<string> projectNames)
    {
        var guids = projectNames.ToDictionary(p => p, _ => Guid.NewGuid().ToString("B").ToUpperInvariant());
        var projects = string.Join("\r\n", projectNames.Select(p =>
        {
            var dir = p.EndsWith(".Client", StringComparison.Ordinal) || p.EndsWith(".Api", StringComparison.Ordinal)
                || p.EndsWith(".Tests", StringComparison.Ordinal)
                ? (p.EndsWith(".Tests", StringComparison.Ordinal) ? $"tests\\{p}" : $"src\\{p}")
                : $"src\\{p}";
            return $@"Project(""{ProjectTypeGuid}"") = ""{p}"", ""{dir}\{p}.csproj"", ""{guids[p]}""{Environment.NewLine}EndProject";
        }));

        var configs = string.Join("\r\n", projectNames.SelectMany(p =>
        {
            var g = guids[p];
            return new[]
            {
                $"\t\t{g}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
                $"\t\t{g}.Debug|Any CPU.Build.0 = Debug|Any CPU",
                $"\t\t{g}.Release|Any CPU.ActiveCfg = Release|Any CPU",
                $"\t\t{g}.Release|Any CPU.Build.0 = Release|Any CPU",
            };
        }));

        return Tpl.Render(o, """
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31912.275
MinimumVisualStudioVersion = 10.0.40219.1
{{PROJECTS}}
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
{{CONFIGS}}
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
""", ("PROJECTS", projects), ("CONFIGS", configs));
    }

    public static string DirectoryBuildProps(NewAppOptions o) => Tpl.Render(o, """
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
""");

    public static string DirectoryPackagesProps(NewAppOptions o)
    {
        var providerVersions = o.Provider switch
        {
            ProviderKind.PostgreSql => """
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
""",
            ProviderKind.MySql => """
    <PackageVersion Include="MySql.EntityFrameworkCore" Version="10.0.9" />
""",
            ProviderKind.SqlServer => """
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.7" />
""",
            _ => """
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.7" />
""",
        };

        var identityPackages = o.UseIdentity
            ? """
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.7" />
    <PackageVersion Include="Microsoft.Extensions.Identity.Stores" Version="10.0.7" />
"""
            : string.Empty;

        return Tpl.Render(o, """
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.7" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.7" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.7" />
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.7" />
{{PROVIDER_PACKAGE}}
{{IDENTITY}}
    <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.7" />
    <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.7" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
""", ("PROVIDER_PACKAGE", providerVersions), ("IDENTITY", identityPackages));
    }

    public static string GitIgnore(NewAppOptions o) => """
# .NET
bin/
obj/
*.user
.vs/
artifacts/
TestResults/

# Dados locais
*.db
*.db-shm
*.db-wal

# IDE
.idea/
*.swp
""";

    public static string Readme(NewAppOptions o) => Tpl.Render(o, """
# {{NAME}}

Aplicação gerada pela **XForge IDE** -?" Clean Architecture (DDD + CQRS + repositório genérico) com ASP.NET Core + Blazor.

## Estrutura

| Projeto | Responsabilidade |
|---|---|
| `src/{{NS}}.Domain` | Entidades, value objects e regras de negócio (sem dependências) |
| `src/{{NS}}.Application` | Casos de uso (CQRS: Commands/Queries/Handlers), abstrações de persistência |
| `src/{{NS}}.Infrastructure` | EF Core (provider: {{PROVIDER}}), repositórios, Identity{{IDENTITY_NOTE}} |
| `src/{{NS}}.Api` | ASP.NET Core (REST, {{AUTH_NOTE}}OpenAPI, health) |
| `src/{{NS}}.Client` | Blazor WebAssembly (consumo da API) |
| `tests/{{NS}}.Tests` | Testes unitários |

## Executando

```powershell
# API (http://localhost:{{APIPORT}})
dotnet run --project src/{{NS}}.Api

# Client (http://localhost:{{CLIENTPORT}}) -?" em outro terminal
dotnet run --project src/{{NS}}.Client
```

## Banco de dados

Provider: **{{PROVIDER}}** -?" connection string em `src/{{NS}}.Api/appsettings.json`.

Em Development o schema é criado com `EnsureCreated()`. Para produção, gere migrations:

```powershell
dotnet tool install -g dotnet-ef
dotnet ef migrations add InitialCreate --project src/{{NS}}.Infrastructure --startup-project src/{{NS}}.Api
dotnet ef database update --project src/{{NS}}.Infrastructure --startup-project src/{{NS}}.Api
```
""", ("NAME", o.DisplayName), ("NS", o.RootNamespace),
    ("PROVIDER", o.Provider.ToString()), ("APIPORT", o.ApiPort.ToString()), ("CLIENTPORT", o.ClientPort.ToString()),
    ("IDENTITY_NOTE", o.UseIdentity ? " + ASP.NET Core Identity" : string.Empty),
    ("AUTH_NOTE", o.UseIdentity ? "JWT, Identity," : string.Empty));
}
