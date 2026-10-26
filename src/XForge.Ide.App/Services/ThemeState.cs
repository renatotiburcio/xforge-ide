using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace XForge.Ide.App.Services;

/// <summary>
/// Estado do tema (light/dark) da IDE. Persiste em localStorage (chave 'xf-theme',
/// mesmo contrato do design system) e aplica em document.documentElement[data-theme].
/// </summary>
public class ThemeState
{
    private const string StorageKey = "xf-theme";
    private readonly IJSRuntime _js;

    public ThemeState(IJSRuntime js) => _js = js;

    public string Current { get; private set; } = "light";

    public event Action? Changed;

    /// <summary>Aplica o tema informado e persiste.</summary>
    public async Task SetAsync(string theme)
    {
        Current = theme is "dark" or "light" ? theme : "light";
        try
        {
            await _js.InvokeVoidAsync("eval",
                $"document.documentElement.setAttribute('data-theme','{Current}');" +
                $"localStorage.setItem('{StorageKey}','{Current}');");
        }
        catch (JSDisconnectedException)
        {
            // circuito encerrado — nada a fazer
        }
        Changed?.Invoke();
    }

    public Task ToggleAsync() => SetAsync(Current == "dark" ? "light" : "dark");

    /// <summary>Restaura o tema salvo na inicialização do circuito.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var saved = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            Current = saved is "dark" or "light" ? saved : "light";
            await _js.InvokeVoidAsync("eval",
                $"document.documentElement.setAttribute('data-theme','{Current}');");
            Changed?.Invoke();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
            // JSRuntime ainda indisponível (prerender) — mantém default
        }
    }
}
