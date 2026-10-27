namespace XForge.Ide.Core.Entities;

/// <summary>
/// Snapshot de interface ("onde eu estava"): seleções, nós expandidos,
/// viewports — por rota e projeto. Restaura o CONTEXTO após F5, nunca os dados.
/// A chave é "rota|projectId" (projeto ausente = rota sozinha).
/// </summary>
public class UiSnapshot
{
    public string Key { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public string Json { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
