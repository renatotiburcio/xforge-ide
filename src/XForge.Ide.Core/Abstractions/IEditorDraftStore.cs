using XForge.Ide.Core.Entities;

namespace XForge.Ide.Core.Abstractions;

/// <summary>Persistência dos rascunhos do editor (edições não salvas).</summary>
public interface IEditorDraftStore
{
    Task<EditorDraft?> GetAsync(string path, CancellationToken ct = default);

    Task UpsertAsync(EditorDraft draft, CancellationToken ct = default);

    Task DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>Remove rascunhos de um projeto (prefixo = caminho do projeto).</summary>
    Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default);
}
