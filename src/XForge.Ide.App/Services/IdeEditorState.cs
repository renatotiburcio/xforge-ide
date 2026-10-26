namespace XForge.Ide.App.Services;

/// <summary>Uma aba aberta no editor (persistida entre navegações do circuito).</summary>
public sealed class EditorTabState
{
    public required string Path { get; init; }
    public required string Title { get; init; }
    public required string Language { get; init; }
    public bool Dirty { get; set; }
    public bool Active { get; set; }
}

/// <summary>
/// Estado do editor compartilhado entre páginas do circuito: abas abertas, arquivo
/// ativo e buffers pendentes (edições não salvas). Usado pelo painel de Diff
/// ("não salvas × disco") e pelo Explorer ("Abertos").
/// </summary>
public sealed class IdeEditorState
{
    private readonly Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<EditorTabState> Tabs { get; private set; } = Array.Empty<EditorTabState>();
    public string? ActiveFilePath { get; private set; }
    public bool ActiveDirty { get; private set; }

    public event Action? Changed;

    /// <summary>Conteúdo pendente (não salvo) do arquivo ativo; null = sem alterações.</summary>
    public string? PendingContent => ActiveFilePath is not null && _pending.TryGetValue(ActiveFilePath, out var v)
        ? v
        : null;

    public void Sync(
        IEnumerable<EditorTabState> tabs,
        string? activePath,
        bool activeDirty)
    {
        Tabs = tabs.ToList();
        ActiveFilePath = activePath;
        ActiveDirty = activeDirty;
        Changed?.Invoke();
    }

    public void SetPending(string path, string? content)
    {
        if (content is null)
        {
            _pending.Remove(path);
        }
        else
        {
            _pending[path] = content;
        }
    }

    public string? PendingFor(string path) =>
        _pending.TryGetValue(path, out var v) ? v : null;

    public void RemovePending(string path) => _pending.Remove(path);

    public void Close(string path)
    {
        _pending.Remove(path);
        Tabs = Tabs.Where(t => !t.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
        Changed?.Invoke();
    }
}
