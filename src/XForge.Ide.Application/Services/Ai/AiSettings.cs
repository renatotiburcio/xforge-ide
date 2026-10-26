using System.Text.Json.Serialization;

namespace XForge.Ide.Application.Services.Ai;

/// <summary>
/// Configuração dos provedores de IA da IDE. Persistida FORA do repositório
/// (%LOCALAPPDATA%\XForge\ide\ai-settings.json) — a chave nunca vai para o git.
/// </summary>
public sealed class AiSettings
{
    /// <summary>Provedor preferido: openai, anthropic, google, ollama, openrouter, groq, custom.</summary>
    public string Provider { get; set; } = "ollama";

    public string Model { get; set; } = "llama3.1";

    /// <summary>Endpoint base (ex.: http://localhost:11434/v1 para Ollama).</summary>
    public string Endpoint { get; set; } = "http://localhost:11434/v1";

    /// <summary>Chave de API (opcional para modelos locais).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Temperatura do modelo (0 = determinístico).</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Máximo de tokens por resposta.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Provedores conhecidos com endpoint/modelo padrão (para a tela de ajustes).</summary>
    [JsonIgnore]
    public static IReadOnlyList<AiProviderPreset> Presets { get; } = new List<AiProviderPreset>
    {
        new("ollama",     "Ollama (local)",              "http://localhost:11434/v1",         "llama3.1",         false),
        new("openai",     "OpenAI",                      "https://api.openai.com/v1",         "gpt-4o-mini",      true),
        new("anthropic",  "Anthropic (Claude)",          "https://api.anthropic.com",          "claude-sonnet-4-5", true),
        new("google",     "Google (Gemini)",             "https://generativelanguage.googleapis.com/v1beta", "gemini-2.0-flash", true),
        new("openrouter", "OpenRouter",                  "https://openrouter.ai/api/v1",      "meta-llama/llama-3.3-70b-instruct", true),
        new("groq",       "Groq",                        "https://api.groq.com/openai/v1",    "llama-3.3-70b-versatile", true),
        new("custom",     "Compatível (endpoint próprio)", "",                                "",                 false),
    };

    public AiProviderPreset? PresetFor(string provider) =>
        Presets.FirstOrDefault(p => p.Id.Equals(provider, StringComparison.OrdinalIgnoreCase));
}

public sealed record AiProviderPreset(
    string Id,
    string Label,
    string DefaultEndpoint,
    string DefaultModel,
    bool RequiresKey);
