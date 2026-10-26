using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace XForge.Ide.Application.Services.Ai;

/// <summary>Cliente da API Google Gemini (generateContent).</summary>
public sealed class GoogleClient : IAiClient
{
    private readonly IHttpClientFactory _http;
    private readonly AiSettings _settings;
    private readonly ILogger<GoogleClient> _log;

    public GoogleClient(IHttpClientFactory http, AiSettings settings, ILogger<GoogleClient> log)
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
        var baseUrl = (_settings.Endpoint ?? "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');
        var contents = messages
            .Select(m => new Content
            {
                Role = m.Role == "assistant" ? "model" : "user",
                Parts = new[] { new Part { Text = m.Content } },
            })
            .ToArray();

        var payload = new GenerateRequest
        {
            Contents = contents,
            SystemInstruction = new Content { Parts = new[] { new Part { Text = systemPrompt } } },
            GenerationConfig = new GenerationConfig
            {
                Temperature = _settings.Temperature,
                MaxOutputTokens = Math.Max(256, _settings.MaxTokens),
            },
        };

        using var client = _http.CreateClient("ai");
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/models/{_settings.Model}:generateContent")
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        req.Headers.TryAddWithoutValidation("x-goog-api-key", _settings.ApiKey ?? string.Empty);

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Gemini respondeu {Status}: {Body}", (int)resp.StatusCode, body[..Math.Min(400, body.Length)]);
            throw new InvalidOperationException($"Gemini retornou {(int)resp.StatusCode}: {Truncate(body)}");
        }

        var parsed = JsonSerializer.Deserialize<GenerateResponse>(body, JsonOpts);
        var text = string.Join("\n", parsed?.Candidates?
            .SelectMany(c => c.Content?.Parts ?? Array.Empty<Part>())
            .Select(p => p.Text ?? string.Empty) ?? Array.Empty<string>()).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Gemini retornou uma resposta vazia.");
        }
        return new AiCompletion(text, _settings.Model, null);
    }

    private static string Truncate(string s) => s.Length > 400 ? s[..400] : s;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class GenerateRequest
    {
        [JsonPropertyName("contents")] public Content[] Contents { get; set; } = Array.Empty<Content>();
        [JsonPropertyName("systemInstruction")] public Content? SystemInstruction { get; set; }
        [JsonPropertyName("generationConfig")] public GenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class Content
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("parts")] public Part[]? Parts { get; set; }
    }

    private sealed class Part
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class GenerationConfig
    {
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("maxOutputTokens")] public int MaxOutputTokens { get; set; }
    }

    private sealed class GenerateResponse
    {
        [JsonPropertyName("candidates")] public Candidate[]? Candidates { get; set; }
    }

    private sealed class Candidate
    {
        [JsonPropertyName("content")] public Content? Content { get; set; }
    }
}
