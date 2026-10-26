namespace XForge.Ide.Application.Services.Ai;

/// <summary>Mensagem de conversa para o modelo.</summary>
public sealed record AiMessage(string Role, string Content);

/// <summary>Resposta de uma conclusão do modelo.</summary>
public sealed record AiCompletion(string Content, string? Model, int? TotalTokens);

/// <summary>
/// Cliente de modelo de linguagem. Implementações: OpenAI/OpenRouter/Groq/Ollama/Custom
/// (API compatível com chat/completions), Anthropic e Google.
/// </summary>
public interface IAiClient
{
    /// <summary>Envia o prompt de sistema e o histórico; retorna a resposta do modelo.</summary>
    Task<AiCompletion> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<AiMessage> messages,
        CancellationToken ct = default);
}
