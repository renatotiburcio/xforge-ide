using XForge.DesignSystem.Services;

namespace XForge.Ide.App.Services;

/// <summary>
/// Definição dos módulos da IDE (rail multi-app, padrão XFAppShell).
/// Itens com IsDefault=true são renderizados na seção inferior do rail (XFRail.FooterItems).
/// </summary>
public class IdeModules
{
    public IReadOnlyList<RailNavItem> RailItems { get; } = new List<RailNavItem>
    {
        new() { Brand = "home",     Label = "Início",     Icon = "house",                OverviewRoute = "/home",     RoutePrefixes = ["/home"] },
        new() { Brand = "data",     Label = "Dados",      Icon = "database",             OverviewRoute = "/data",     RoutePrefixes = ["/data"] },
        new() { Brand = "code",     Label = "Código",     Icon = "file-code",            OverviewRoute = "/code",     RoutePrefixes = ["/code"] },
        new() { Brand = "design",   Label = "Telas",      Icon = "palette",              OverviewRoute = "/design",   RoutePrefixes = ["/design"] },
        new() { Brand = "wizards",  Label = "Wizards",    Icon = "wand-magic-sparkles",  OverviewRoute = "/wizards",  RoutePrefixes = ["/wizards"] },
        new() { Brand = "ai",       Label = "IA",         Icon = "robot",                OverviewRoute = "/ai",       RoutePrefixes = ["/ai"] },
        new() { Brand = "git",      Label = "Git",        Icon = "code-branch",          OverviewRoute = "/git",      RoutePrefixes = ["/git"] },
        new() { Brand = "run",      Label = "Executar",   Icon = "rocket",               OverviewRoute = "/run",      RoutePrefixes = ["/run"] },

        // Rodapé do rail
        new() { Brand = "settings", Label = "Ajustes",    Icon = "gear",                 OverviewRoute = "/settings", RoutePrefixes = ["/settings"], IsDefault = true },
    };

    /// <summary>Título da sidebar por módulo.</summary>
    public static string SidebarTitle(string brand) => brand switch
    {
        "home"     => "Início",
        "data"     => "Designer de Dados",
        "code"     => "Editor de Código",
        "design"   => "Designer de Telas",
        "wizards"  => "Wizards RAD",
        "ai"       => "Inteligência Artificial",
        "git"      => "Git & GitHub",
        "run"      => "Executar & Depurar",
        "settings" => "Configurações",
        _          => "XForge IDE",
    };

    /// <summary>Itens do menu lateral por módulo (rótulo, rota, marco futuro).</summary>
    public static IReadOnlyList<(string Label, string Href, string? Milestone)> SidebarItems(string brand) => brand switch
    {
        "home" => new List<(string, string, string?)>
        {
            ("Visão geral", "/home", null),
            ("Projetos", "/projects", null),
            ("Modelos de projeto", "/models", "M2"),
        },
        "data" => new List<(string, string, string?)>
        {
            ("Dicionário", "/data", null),
            ("Diagrama ER", "/data/er", "M2b"),
            ("Importar banco", "/data/import", null),
            ("Migrations", "/data/migrations", "M2b"),
        },
        "code" => new List<(string, string, string?)>
        {
            ("Explorer", "/code", "M3"),
            ("Busca global", "/code/search", "M3"),
            ("Diff", "/code/diff", "M3"),
        },
        "design" => new List<(string, string, string?)>
        {
            ("Canvas", "/design", "M4"),
            ("Toolbox XF*", "/design/toolbox", "M4"),
            ("Propriedades", "/design/properties", "M4"),
        },
        "wizards" => new List<(string, string, string?)>
        {
            ("Novo aplicativo", "/wizards", null),
            ("CRUD", "/wizards/crud", "M5"),
            ("Master-Detail", "/wizards/master-detail", "M5"),
            ("Grid + Form", "/wizards/grid-form", "M5"),
            ("Inline Edit", "/wizards/inline-edit", "M5"),
            ("Pivot", "/wizards/pivot", "M5"),
            ("Layouts", "/wizards/layouts", "M5"),
        },
        "ai" => new List<(string, string, string?)>
        {
            ("Chat", "/ai", "M6"),
            ("Agentes", "/ai/agents", "M6"),
            ("Skills & Comandos", "/ai/skills", "M6"),
            ("Histórico", "/ai/history", "M6"),
        },
        "git" => new List<(string, string, string?)>
        {
            ("Alterações", "/git", "M7"),
            ("Histórico", "/git/log", "M7"),
            ("Branches", "/git/branches", "M7"),
            ("Pull Requests", "/git/prs", "M7"),
        },
        "run" => new List<(string, string, string?)>
        {
            ("Build & Run", "/run", "M3"),
            ("Console", "/run/console", "M3"),
        },
        "settings" => new List<(string, string, string?)>
        {
            ("Geral", "/settings", null),
            ("Tema", "/settings/theme", null),
            ("Provedores de IA", "/settings/ai", "M6"),
            ("GitHub", "/settings/github", "M7"),
        },
        _ => new List<(string, string, string?)> { ("Visão geral", "/home", null) },
    };
}
