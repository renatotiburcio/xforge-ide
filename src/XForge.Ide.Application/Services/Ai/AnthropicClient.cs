using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace XForge.Ide.Application.Services.Ai;

/// <summary>Cliente da API Anthropic (Messages).</summary>
public sealed class AnthropicClient : IAiClient
{
    private readonly IHttpClientFactory _http;
    private readonly AiSettings _settings;
    private readonly ILogger<AnthropicClient> _log;

    public AnthropicClient(IHttpClientFactory http, AiSettings settings, ILogger<AnthropicClient> log)
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
        var baseUrl = (_settings.Endpoint ?? "https://api.anthropic.com").TrimEnd('/');
        var payload = new MessagesRequest
        {
            Model = _settings.Model,
            MaxTokens = Math.Max(256, _settings.MaxTokens),
            Temperature = _settings.Temperature,
            System = systemPrompt,
            Messages = messages.Select(m => new AnthropicMessage { Role = m.Role == "assistant" ? "assistant" : "user", Content = m.Content }).ToArray(),
        };

        using var client = _http.CreateClient("ai");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        req.Headers.TryAddWithoutValidation("x-api-key", _settings.ApiKey ?? string.Empty);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Anthropic respondeu {Status}: {Body}", (int)resp.StatusCode, body[..Math.Min(400, body.Length)]);
            throw new InvalidOperationException($"Anthropic retornou {(int)resp.StatusCode}: {Truncate(body)}");
        }

        var parsed = JsonSerializer.Deserialize<MessagesResponse>(body, JsonOpts);
        var text = string.Join("\n", parsed?.Content?.Select(c => c.Text) ?? Array.Empty<string>()).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Anthropic retornou uma resposta vazia.");
        }
        return new AiCompletion(text, parsed?.Model, parsed?.Usage?.OutputTokens);
    }

    private static string Truncate(string s) => s.Length > 400 ? s[..400] : s;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class MessagesRequest
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("system")] public string? System { get; set; }
        [JsonPropertyName("messages")] public AnthropicMessage[] Messages { get; set; } = Array.Empty<AnthropicMessage>();
    }

    private sealed class AnthropicMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user";
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class MessagesResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("content")] public ContentBlock[]? Content { get; set; }
        [JsonPropertyName("usage")] public Usage? Usage { get; set; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("output_tokens")] public int? OutputTokens { get; set; }
    }
}
