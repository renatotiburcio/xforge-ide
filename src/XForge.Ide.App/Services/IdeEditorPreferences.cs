using System.Text.Json;
using Microsoft.JSInterop;
using XForge.Ide.Application.Services;

namespace XForge.Ide.App.Services;

/// <summary>
/// Preferências do editor de código (equivale às Settings do VS Code).
/// Fonte da verdade: SQLite (banco da IDE). localStorage é o cache usado no
/// pré-render e enquanto o circuito interativo não está disponível.
/// </summary>
public sealed class IdeEditorPreferences
{
    private const string StorageKey = "xf-editor-prefs";
    private const string PreferenceKey = "editor.prefs";
    private readonly IJSRuntime _js;
    private readonly IIdeSessionStore _store;

    private EditorPrefs _current = new();
    private bool _loadedFromDb;

    public IdeEditorPreferences(IJSRuntime js, IIdeSessionStore store)
    {
        _js = js;
        _store = store;
    }

    public event Action? Changed;

    public EditorPrefs Current => _current;

    /// <summary>Restaura as preferências salvas; chamado na inicialização do circuito.</summary>
    public async Task LoadAsync()
    {
        // 1) banco (fonte da verdade)
        if (!_loadedFromDb)
        {
            try
            {
                var json = await _store.GetPreferenceAsync(PreferenceKey);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var fromDb = JsonSerializer.Deserialize<EditorPrefs>(json);
                    if (fromDb is not null)
                    {
                        _current = fromDb;
                        _loadedFromDb = true;
                    }
                }
            }
            catch (Exception)
            {
                // banco indisponível — segue para o cache local
            }
        }

        // 2) cache local (sempre válido; usado no pré-render)
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var local = JsonSerializer.Deserialize<EditorPrefs>(json);
                if (local is not null && (!_loadedFromDb || local != _current))
                {
                    if (!_loadedFromDb)
                    {
                        _current = local;
                    }
                }
            }
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
            // pré-render: JS indisponível
        }

        Changed?.Invoke();
    }

    public async Task SaveAsync(EditorPrefs prefs)
    {
        _current = prefs;
        var json = JsonSerializer.Serialize(prefs);

        // banco (persistência real; em falha, o cache local ainda vale)
        try
        {
            await _store.SetPreferenceAsync(PreferenceKey, json);
            _loadedFromDb = true;
        }
        catch (Exception)
        {
        }

        // cache local
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch (JSDisconnectedException)
        {
        }
        Changed?.Invoke();
    }

    public Task UpdateAsync(Action<EditorPrefs> change)
    {
        var copy = _current with { };
        change(copy);
        return SaveAsync(copy);
    }
}

/// <summary>Valores das preferências do editor.</summary>
public sealed record EditorPrefs
{
    /// <summary>Tamanho da fonte no editor (px).</summary>
    public int FontSize { get; init; } = 13;

    /// <summary>Família tipográfica (monoespaçada).</summary>
    public string FontFamily { get; init; } = "JetBrains Mono, ui-monospace, monospace";

    /// <summary>Largura do tab (espaços).</summary>
    public int TabSize { get; init; } = 4;

    /// <summary>Quebra de linha automática.</summary>
    public bool WordWrap { get; init; }

    /// <summary>Exibe o minimapa.</summary>
    public bool Minimap { get; init; } = true;

    /// <summary>Exibe números de linha.</summary>
    public bool LineNumbers { get; init; } = true;

    public static IReadOnlyList<int> FontSizes { get; } = new[] { 11, 12, 13, 14, 16, 18, 20 };
    public static IReadOnlyList<int> TabSizes { get; } = new[] { 2, 4, 8 };
}
