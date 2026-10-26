using Microsoft.Extensions.Logging;

namespace XForge.Ide.Application.Services.Ai;

/// <summary>Seleciona o cliente conforme o provedor configurado.</summary>
public sealed class AiClientFactory
{
    private readonly IHttpClientFactory _http;
    private readonly AiSettingsStore _store;
    private readonly ILoggerFactory _logFactory;

    public AiClientFactory(IHttpClientFactory http, AiSettingsStore store, ILoggerFactory logFactory)
    {
        _http = http;
        _store = store;
        _logFactory = logFactory;
    }

    public IAiClient Create()
    {
        var settings = _store.Get();
        return settings.Provider.ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicClient(_http, settings, _logFactory.CreateLogger<AnthropicClient>()),
            "google" => new GoogleClient(_http, settings, _logFactory.CreateLogger<GoogleClient>()),
            _ => new OpenAiCompatibleClient(_http, settings, _logFactory.CreateLogger<OpenAiCompatibleClient>()),
        };
    }

    /// <summary>merge dos presets: aplica endpoint/modelo padrão quando o usuário troca de provedor.</summary>
    public AiSettings SettingsFor(string provider)
    {
        var settings = _store.Get();
        var preset = settings.PresetFor(provider);
        if (preset is not null)
        {
            settings.Provider = preset.Id;
            if (string.IsNullOrWhiteSpace(settings.Endpoint))
            {
                settings.Endpoint = preset.DefaultEndpoint;
            }
            if (string.IsNullOrWhiteSpace(settings.Model))
            {
                settings.Model = preset.DefaultModel;
            }
        }
        return settings;
    }
}
