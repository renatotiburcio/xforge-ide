namespace XForge.Ide.Core.Entities;

/// <summary>Preferência da IDE (chave → JSON).</summary>
public class IdePreference
{
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
}

/// <summary>
/// Sessão do editor persistida (por projeto): abas abertas, arquivo ativo e estado do
/// painel. Uma linha por projeto (Key = caminho do projeto).
/// </summary>
public class IdeSession
{
    public string Key { get; set; } = string.Empty;

    /// <summary>JSON com os caminhos das abas abertas, na ordem.</summary>
    public string TabsJson { get; set; } = "[]";

    /// <summary>Caminho do arquivo ativo.</summary>
    public string? ActivePath { get; set; }

    /// <summary>Altura do painel inferior (px).</summary>
    public double PanelHeight { get; set; } = 240;

    public bool PanelOpen { get; set; }

    /// <summary>Quantidade de terminais abertos.</summary>
    public int TerminalCount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Configuração Git/GitHub por projeto. Credenciais por referência (nunca em claro).</summary>
public class GitSetting
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProjectId { get; set; }
    public string? RemoteUrl { get; set; }
    public string? UserRef { get; set; }
    public string? TokenRef { get; set; }
}

/// <summary>Log de execução de ferramenta MCP (auditoria).</summary>
public class McpToolLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Tool { get; set; } = string.Empty;
    public string ArgsJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
