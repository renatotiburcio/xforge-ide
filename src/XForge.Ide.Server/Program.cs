using Microsoft.EntityFrameworkCore;
using XForge.DesignSystem.Services;
using XForge.Ide.App.Services;
using XForge.Ide.Application;
using XForge.Ide.Application.Services;
using XForge.Ide.Infrastructure;
using XForge.Ide.Infrastructure.Persistence;
using XForge.Ide.Server.Components;
using XForge.Ide.Server.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Banco de dados da IDE (SQLite)
// ---------------------------------------------------------------------------
var dbPath = builder.Configuration["Ide:DatabasePath"];
if (string.IsNullOrWhiteSpace(dbPath))
{
    dbPath = IdeDbContextFactory.ResolveDatabasePath();
}

builder.Services.AddIdeInfrastructure(dbPath);
builder.Services.AddIdeApplication();

// ---------------------------------------------------------------------------
// UI: Razor Components (Blazor Server interativo) + XForge Design System
// ---------------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddXForgeComponents();
builder.Services.AddScoped<NavigationStateService>();
builder.Services.AddScoped<ThemeState>();
builder.Services.AddScoped<IdeUiState>();
builder.Services.AddSingleton<IdeModules>();

// canal "Saída" do painel inferior (editor, terminal, IA)
builder.Services.AddSingleton<XForge.Ide.App.Services.IdeOutputService>();

// estado do editor (abas, arquivo ativo, buffers pendentes) — compartilhado no circuito
builder.Services.AddScoped<XForge.Ide.App.Services.IdeEditorState>();

// preferências do editor (fonte, tab, wrap, minimapa)
builder.Services.AddScoped<XForge.Ide.App.Services.IdeEditorPreferences>();

// build/run dos projetos gerados com saída em tempo real
builder.Services.AddSingleton<BuildRunService>();

// IntelliSense real (Roslyn) do editor de código
builder.Services.AddSingleton<XForge.Ide.Server.Services.RoslynIntellisenseService>();

// Terminal integrado (xterm + ConPTY) via SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<XForge.Ide.Server.Services.TerminalSessionManager>();

var app = builder.Build();

// Aplica migrations da IDE na inicialização
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdeDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddAdditionalAssemblies(typeof(XForge.Ide.App.Layout.IdeShell).Assembly)
    .AddInteractiveServerRenderMode();

// API REST (usada pelo Electron/MCP; a UI Blazor injeta os serviços diretamente)
ProjectEndpoints.Map(app);
WizardEndpoints.Map(app);
FileEndpoints.Map(app);

// Hub do terminal integrado (xterm.js <-> ConPTY)
app.MapHub<XForge.Ide.Server.Hubs.TerminalHub>("/hubs/terminal");

// Hub do IntelliSense (Roslyn: completions, hover, diagnósticos)
app.MapHub<XForge.Ide.Server.Hubs.IntellisenseHub>("/hubs/intellisense");

// encerra PTYs órfãos ao desligar a IDE
var terminalManager = app.Services.GetRequiredService<XForge.Ide.Server.Services.TerminalSessionManager>();
app.Lifetime.ApplicationStopping.Register(() => terminalManager.DisposeAllAsync().GetAwaiter().GetResult());

app.Run();
