using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace XForge.Ide.Application.Services.Ai;

/// <summary>
/// Cliente para qualquer API compatível com OpenAI chat/completions:
/// OpenAI, OpenRouter, Groq, Ollama e endpoints próprios.
/// </summary>
public sealed class OpenAiCompatibleClient : IAiClient
{
    private readonly IHttpClientFactory _http;
    private readonly AiSettings _settings;
    private readonly ILogger<OpenAiCompatibleClient> _log;

    public OpenAiCompatibleClient(IHttpClientFactory http, AiSettings settings, ILogger<OpenAiCompatibleClient> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public async Task<AiCompletion> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default)
    {
        var baseUrl = (_settings.Endpoint ?? string.Empty).TrimEnd('/');
        if (baseUrl.Length == 0)
        {
            throw new InvalidOperationException("Endpoint do provedor de IA não configurado (verifique Configurações › Provedores de IA).");
        }

        var payload = new ChatRequest
        {
            Model = _settings.Model,
            Temperature = _settings.Temperature,
            MaxTokens = _settings.MaxTokens,
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = systemPrompt },
            }.Concat(messages.Select(m => new ChatMessage { Role = m.Role, Content = m.Content })).ToArray(),
        };

        using var client = _http.CreateClient("ai");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_settings.ApiKey}");
        }

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("IA respondeu {Status}: {Body}", (int)resp.StatusCode, body.Length > 400 ? body[..400] : body);
            throw new InvalidOperationException($"Provedor de IA retornou {(int)resp.StatusCode}: {Truncate(body)}");
        }

        var parsed = JsonSerializer.Deserialize<ChatResponse>(body, JsonOpts);
        var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Provedor de IA retornou uma resposta vazia.");
        }
        return new AiCompletion(text, parsed?.Model, parsed?.Usage?.TotalTokens);
    }

    private static string Truncate(string s) => s.Length > 400 ? s[..400] : s;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("choices")] public ChatChoice[]? Choices { get; set; }
        [JsonPropertyName("usage")] public ChatUsage? Usage { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    private sealed class ChatUsage
    {
        [JsonPropertyName("total_tokens")] public int? TotalTokens { get; set; }
    }
}
