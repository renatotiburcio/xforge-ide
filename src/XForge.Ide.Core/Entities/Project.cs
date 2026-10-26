namespace XForge.Ide.Core.Entities;

/// <summary>
/// Projeto gerenciado pela IDE (uma solution gerada/aberta pela XForge).
/// </summary>
public class Project
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Nome de exibição do projeto.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Caminho absoluto do diretório da solution.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Template usado na geração (ex.: "clean-architecture").</summary>
    public string TemplateKind { get; set; } = "clean-architecture";

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastOpenedAt { get; set; }
}
