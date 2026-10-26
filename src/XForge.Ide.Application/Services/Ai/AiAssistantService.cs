using Microsoft.Extensions.Logging;

namespace XForge.Ide.Application.Services.Ai;

/// <summary>Ações prontas do assistente (atalhos do painel de IA).</summary>
public static class AiAction
{
    public const string Explain = "explain";
    public const string Refactor = "refactor";
    public const string Review = "review";
    public const string Tests = "tests";
    public const string Feature = "feature";
    public const string Fix = "fix";
    public const string Custom = "custom";

    public static string Label(string action) => action switch
    {
        Explain => "Explicar",
        Refactor => "Refatorar",
        Review => "Revisar",
        Tests => "Gerar testes",
        Feature => "Criar funcionalidade",
        Fix => "Corrigir erro",
        _ => "Perguntar",
    };

    public static string IoClass(string action) => action switch
    {
        Explain => "file-code",
        Refactor => "code",
        Review => "eye",
        Tests => "flask",
        Feature => "wand-magic-sparkles",
        Fix => "wrench",
        _ => "robot",
    };
}

/// <summary>
/// Orquestra o assistente: monta o contexto do workspace, aplica o prompt da
/// ação escolhida e devolve a resposta do modelo.
/// </summary>
public sealed class AiAssistantService
{
    private readonly AiClientFactory _factory;
    private readonly ILogger<AiAssistantService> _log;

    public AiAssistantService(AiClientFactory factory, ILogger<AiAssistantService> log)
    {
        _factory = factory;
        _log = log;
    }

    /// <summary>Executa uma ação do assistente sobre o arquivo ativo do workspace.</summary>
    public async Task<AiCompletion> RunAsync(
        string action,
        WorkspaceContext context,
        string? userPrompt = null,
        CancellationToken ct = default)
    {
        var client = _factory.Create();
        var question = BuildPrompt(action, userPrompt);
        var messages = new List<AiMessage>
        {
            new("user", context.Render()),
            new("user", question),
        };

        _log.LogInformation("Assistente IA: ação {Action} em {File}", action, context.ActiveFilePath ?? "(sem arquivo)");
        return await client.CompleteAsync(WorkspaceContext.SystemPrompt, messages, ct);
    }

    private static string BuildPrompt(string action, string? userPrompt)
    {
        var base_ = action switch
        {
            AiAction.Explain => "Explique o que o código abaixo faz, em português, de forma objetiva (máximo 12 linhas).",
            AiAction.Refactor => "Refatore o código abaixo mantendo o comportamento. Aplique boas práticas de C# e .NET 10. Entregue o código completo ao final.",
            AiAction.Review => "Revise o código abaixo e aponte: bugs, riscos, problemas de desempenho e de segurança. Seja específico e sugira correções.",
            AiAction.Tests => "Escreva testes xUnit para o código abaixo, cobrindo casos de borda. Use FluentAssertions se estiver disponível.",
            AiAction.Feature => "Implemente a funcionalidade pedida no código abaixo, seguindo os padrões do projeto. Entregue o código completo alterado.",
            AiAction.Fix => "Diagnostique e corrija o problema descrito abaixo no código. Explique a causa raiz em 3 linhas e entregue o código corrigido.",
            _ => "Responda a pergunta do usuário sobre o código abaixo.",
        };

        return string.IsNullOrWhiteSpace(userPrompt)
            ? base_
            : $"{base_}\n\nPedido do usuário: {userPrompt}";
    }
}
