namespace XForge.Ide.Core.Entities;

/// <summary>Página/tela gerada pela IDE (alvo do designer visual).</summary>
public class GeneratedPage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Kind { get; set; } = "CrudList";       // CrudList | CrudForm | MasterDetail | Custom...
    public string RazorPath { get; set; } = string.Empty;
    public string? StateJson { get; set; }               // árvore de componentes persistida
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Sessão de edição no designer visual.</summary>
public class DesignSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid GeneratedPageId { get; set; }
    public string TreeJson { get; set; } = "{}";
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Execução de wizard RAD.</summary>
public class WizardRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProjectId { get; set; }
    public string WizardKind { get; set; } = string.Empty; // NewApp | CrudPages | MasterDetail...
    public string InputsJson { get; set; } = "{}";
    public string? OutputJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
