using XForge.Ide.Templates.Templates;

namespace XForge.Ide.Templates;

/// <summary>
/// Renderizador minimalista de templates: substitui tokens <c>{{Chave}}</c>.
/// Tokens base (NS, NAME, PROVIDER, APIPORT, CLIENTPORT, CONN) v?m sempre das op??es
/// (decis?o M-13: templates em C# tipado, sem runtime Razor).
/// </summary>
internal static class Tpl
{
    public static string Render(NewAppOptions o, string raw, params (string Key, string Value)[] tokens)
    {
        raw = raw
            .Replace("{{NS}}", o.RootNamespace, StringComparison.Ordinal)
            .Replace("{{NAME}}", o.DisplayName, StringComparison.Ordinal)
            .Replace("{{PROVIDER}}", o.Provider.ToString(), StringComparison.Ordinal)
            .Replace("{{APIPORT}}", o.ApiPort.ToString(), StringComparison.Ordinal)
            .Replace("{{CLIENTPORT}}", o.ClientPort.ToString(), StringComparison.Ordinal)
            .Replace("{{CONN}}", ApiClientTemplates.DefaultConnectionString(o), StringComparison.Ordinal);

        foreach (var (key, value) in tokens)
        {
            raw = raw.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        }
        return raw;
    }
}
