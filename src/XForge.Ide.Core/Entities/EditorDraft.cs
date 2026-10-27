namespace XForge.Ide.Core.Entities;

/// <summary>
/// Rascunho de arquivo do editor (conteúdo não salvo). Sobrevive ao F5:
/// ao reabrir o arquivo, o rascunho entra no lugar do conteúdo do disco e a
/// aba volta marcada como suja. Some quando o usuário salva (Ctrl+S).
/// </summary>
public class EditorDraft
{
    /// <summary>Caminho absoluto do arquivo (chave; único na máquina).</summary>
    public string Path { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
