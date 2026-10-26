using XForge.Ide.Core.Enums;

namespace XForge.Ide.Templates.Templates;

/// <summary>Templates da API (ASP.NET Core + Identity/JWT) e do Client Blazor WASM.</summary>
internal static class ApiClientTemplates
{
    public static string ApiCsproj(NewAppOptions o)
    {
        var identity = o.UseIdentity ? """
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.7" />
  </ItemGroup>

""" : string.Empty;
        return Tpl.Render(o, """
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="..\{{NS}}.Application\{{NS}}.Application.csproj" />
    <ProjectReference Include="..\{{NS}}.Infrastructure\{{NS}}.Infrastructure.csproj" />
  </ItemGroup>

{{IDENTITY}}
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.7" />
  </ItemGroup>


</Project>
""", ("IDENTITY", identity));
    }

    public static string ApiProgram(NewAppOptions o)
    {
        var identityBlocks = o.UseIdentity ? Tpl.Render(o, """
// ---------------------------------------------------------------------------
// Autenticação (ASP.NET Core Identity + JWT)
// ---------------------------------------------------------------------------
builder.Services
    .AddIdentityCore<{{NS}}.Infrastructure.Identity.ApplicationUser>()
    .AddSignInManager()
    .AddEntityFrameworkStores<{{NS}}.Infrastructure.Persistence.{{NS}}DbContext>();

var jwt = builder.Configuration.GetSection("Jwt").Get<{{NS}}.Api.Auth.JwtOptions>()
    ?? throw new InvalidOperationException("Seção 'Jwt' não configurada.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton(jwt);
builder.Services.AddScoped<ITokenService, {{NS}}TokenService>();
""") : string.Empty;

        var useAuth = o.UseIdentity ? """
app.UseAuthentication();
app.UseAuthorization();
""" : string.Empty;

        return Tpl.Render(o, """
using System.Text;
using {{NS}}.Application;
using {{NS}}.Infrastructure;
using {{NS}}.Infrastructure.Persistence;
{{IDENTITY_USINGS}}


var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Persistência (provider: {{PROVIDER}})
// ---------------------------------------------------------------------------
var provider = builder.Configuration["Database:Provider"] ?? "{{PROVIDER}}";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' não configurada.");

builder.Services.AddPersistence(provider, connectionString);
builder.Services.AddApplicationServices();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
{{IDENTITY_BLOCKS}}
var app = builder.Build();

// Banco de dados: Development cria o schema (EnsureCreated); produção usa migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<{{NS}}DbContext>();
    if (app.Environment.IsDevelopment())
    {
        db.Database.EnsureCreated();
    }
    else
    {
        db.Database.Migrate();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
{{USE_AUTH}}
app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", app = "{{NAME}}", at = DateTime.UtcNow }));

app.Run();
""",
    ("PROVIDER", o.Provider.ToString()),
    ("IDENTITY_USINGS", o.UseIdentity ? "using Microsoft.EntityFrameworkCore;\nusing Microsoft.AspNetCore.Authentication.JwtBearer;\nusing Microsoft.AspNetCore.Identity;\nusing Microsoft.IdentityModel.Tokens;\nusing {{NS}}.Api.Auth;" : string.Empty),
    ("IDENTITY_BLOCKS", identityBlocks),
    ("USE_AUTH", useAuth),
    ("NAME", o.DisplayName),
    ("NS", o.RootNamespace));
    }

    public static string JwtOptions(NewAppOptions o) => Tpl.Render(o, """
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
namespace {{NS}}.Api.Auth;

/// <summary>Configuração JWT (appsettings.json -?' seção "Jwt").</summary>
public sealed class JwtOptions
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string Key { get; init; }
    public int ExpiryMinutes { get; init; } = 120;
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(string userId, string email);
}

/// <summary>Emissor de tokens JWT.</summary>
public sealed class {{NS}}TokenService(JwtOptions options) : ITokenService
{
    private readonly JwtOptions _options = options;

    public (string Token, DateTime ExpiresAt) CreateToken(string userId, string email)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
            new(System.Security.Claims.ClaimTypes.Email, email),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return (new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
""");

    public static string AccountEndpoints(NewAppOptions o) => Tpl.Render(o, """
using Microsoft.AspNetCore.Identity;
using {{NS}}.Api.Auth;
using {{NS}}.Infrastructure.Identity;

namespace {{NS}}.Api.Endpoints;

/// <summary>Registro e login (JWT).</summary>
public static class AccountEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/account").WithTags("Account");

        group.MapPost("/register", async (RegisterRequest request, UserManager<ApplicationUser> users) =>
        {
            var user = new ApplicationUser { UserName = request.Email, Email = request.Email, DisplayName = request.DisplayName };
            var result = await users.CreateAsync(user, request.Password);
            return result.Succeeded
                ? Results.Created($"/api/account/{user.Id}", new { user.Id, user.Email })
                : Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        });

        group.MapPost("/login", async (LoginRequest request, UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn, ITokenService tokens) =>
        {
            var user = await users.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var check = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (!check.Succeeded)
            {
                return Results.Unauthorized();
            }

            var (token, expiresAt) = tokens.CreateToken(user.Id, user.Email!);
            return Results.Ok(new { token, expiresAt });
        });
    }
}

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);
public sealed record LoginRequest(string Email, string Password);
""");

    public static string ApiAppSettings(NewAppOptions o)
    {
        var jwtBlock = o.UseIdentity ? JwtAppSettingsBlock(o) : string.Empty;
        var conn = string.IsNullOrEmpty(o.ConnectionString)
            ? DefaultConnectionString(o)
            : o.ConnectionString.Replace("\"", "\\\"");

        return Tpl.Render(o, """
{
  "ConnectionStrings": {
    "Default": "{{CONN}}"
  },
  "Database": {
    "Provider": "{{PROVIDER}}"
  },
{{JWT}}
  "AllowedHosts": "*"
}
""", ("PROVIDER", o.Provider.ToString()), ("CONN", conn), ("JWT", jwtBlock));
    }

    private static string JwtAppSettingsBlock(NewAppOptions o) => Tpl.Render(o, """
  "Jwt": {
    "Issuer": "{{NS}}.Api",
    "Audience": "{{NS}}.Client",
    "Key": "TROQUE-ESTA-CHAVE-PELO-SEGREDO-REAL-com-no-minimo-32-caracteres",
    "ExpiryMinutes": 120
  },
""", ("NS", o.RootNamespace));

    public static string ApiAppSettingsDev(NewAppOptions o) => Tpl.Render(o, """
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
""");

    public static string ApiLaunchSettings(NewAppOptions o) => Tpl.Render(o, """
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:{{APIPORT}}",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
""", ("APIPORT", o.ApiPort.ToString()));

    public static string DefaultConnectionString(NewAppOptions o) => (o.Provider switch
    {
        ProviderKind.Sqlite => "Data Source=app.db",
        ProviderKind.PostgreSql => "Host=localhost;Port=5432;Database={{NS}};Username=postgres;Password=postgres",
        ProviderKind.MySql => "Server=localhost;Port=3306;Database={{NS}};User=root;Password=;",
        ProviderKind.SqlServer => "Server=(localdb)\\mssqllocaldb;Database={{NS}};Trusted_Connection=True;",
        _ => "Data Source=app.db",
    }).Replace("{{NS}}", o.RootNamespace);

    // ------------------------------------------------------------------ Client
    public static string ClientCsproj(NewAppOptions o) => Tpl.Render(o, """
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>{{NS}}.Client</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.7" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.7" PrivateAssets="all" />
  </ItemGroup>

</Project>
""");

    public static string ClientProgram(NewAppOptions o) => Tpl.Render(o, """
using {{NS}}.Client;
using {{NS}}.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp =>
{
    var http = new HttpClient { BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:{{APIPORT}}") };
    return http;
});
builder.Services.AddScoped<{{NS}}ApiClient>();

await builder.Build().RunAsync();
""", ("APIPORT", o.ApiPort.ToString()));

    public static string ClientAppRazor(NewAppOptions o) => Tpl.Render(o, """
<!DOCTYPE html>
<html lang="pt-BR">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>{{NAME}}</title>
    <base href="/" />
    <style>
        body { font-family: system-ui, sans-serif; margin: 0; background: #f8fafc; color: #0f172a; }
        main { max-width: 720px; margin: 3rem auto; padding: 0 1rem; }
        .card { background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 1.5rem; }
        .ok { color: #16a34a; font-weight: 600; }
        .err { color: #dc2626; font-weight: 600; }
    </style>
</head>

<body>
    <div id="app">Carregando...</div>
    <script src="_framework/blazor.webassembly.js"></script>
</body>

</html>
""", ("NAME", o.DisplayName));

    // Manter nome consistente: App.razor é o root component do WASM
    public static string ClientRoot(NewAppOptions o) => Tpl.Render(o, """
<main>
    <h1>{{NAME}}</h1>
    <p>Aplicação Blazor WebAssembly gerada pela XForge IDE.</p>
    <div class="card">
        <h3>API</h3>
        @if (_health is null)
        {
            <p>Consultando /api/health...</p>
        }
        else if (_health.StartsWith("erro", StringComparison.Ordinal))
        {
            <p class="err">@_health</p>
        }
        else
        {
            <p class="ok">@_health</p>
        }
        <button @onclick="CheckHealthAsync">Verificar API</button>
    </div>
</main>

@code {
    private string? _health;

    protected override async Task OnInitializedAsync() => await CheckHealthAsync();

    private async Task CheckHealthAsync()
    {
        try
        {
            _health = "ok -?" " + await Http.GetStringAsync("/api/health");
        }
        catch (Exception ex)
        {
            _health = "erro: " + ex.Message;
        }
    }
}
""", ("NAME", o.DisplayName));

    public static string ClientImports(NewAppOptions o) => Tpl.Render(o, """
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using {{NS}}.Client
@using {{NS}}.Client.Services
""");

    public static string ClientRoutes(NewAppOptions o) => Tpl.Render(o, """
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" />
    </Found>
    <NotFound>
        <p>Página não encontrada.</p>
    </NotFound>
</Router>
""");

    public static string ClientApiClient(NewAppOptions o) => Tpl.Render(o, """
namespace {{NS}}.Client.Services;

/// <summary>Cliente HTTP tipado para a API.</summary>
public class {{NS}}ApiClient(HttpClient http)
{
    private readonly HttpClient _http = http;

    public Task<string> GetHealthAsync() => _http.GetStringAsync("/api/health");
}
""");

    public static string ClientIndexHtml(NewAppOptions o) => Tpl.Render(o, """
<!DOCTYPE html>
<html lang="pt-BR">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>{{NAME}}</title>
    <base href="/" />
    <style>
        body { font-family: system-ui, sans-serif; margin: 0; background: #f8fafc; color: #0f172a; }
        #app { padding: 3rem 1rem; max-width: 720px; margin: 0 auto; }
        .card { background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 1.5rem; }
        .ok { color: #16a34a; font-weight: 600; }
        .err { color: #dc2626; font-weight: 600; }
    </style>
</head>

<body>
    <div id="app">Carregando...</div>
    <script src="_framework/blazor.webassembly.js"></script>
</body>

</html>
""", ("NAME", o.DisplayName));

    public static string ClientAppSettings(NewAppOptions o) => Tpl.Render(o, """
{
  "ApiBaseUrl": "http://localhost:{{APIPORT}}"
}
""", ("APIPORT", o.ApiPort.ToString()));

    public static string ClientLaunchSettings(NewAppOptions o) => Tpl.Render(o, """
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:{{CLIENTPORT}}",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
""", ("CLIENTPORT", o.ClientPort.ToString()));

    // ------------------------------------------------------------------- Tests
    public static string TestsCsproj(NewAppOptions o) => Tpl.Render(o, """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\{{NS}}.Domain\{{NS}}.Domain.csproj" />
    <ProjectReference Include="..\..\src\{{NS}}.Application\{{NS}}.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

</Project>
""");

    public static string SmokeTest(NewAppOptions o) => Tpl.Render(o, """
using Xunit;
using {{NS}}.Domain.Common;

namespace {{NS}}.Tests;

public class SmokeTests
{
    [Fact]
    public void BaseEntity_tem_id_e_timestamp()
    {
        var entity = new TestEntity();

        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.True(entity.CreatedAt <= DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void Result_failure_carrega_erro()
    {
        var result = Result<string>.Failure("falha de teste");

        Assert.False(result.IsSuccess);
        Assert.Equal("falha de teste", result.Error);
    }

    private sealed class TestEntity : BaseEntity;
}
""");
}
