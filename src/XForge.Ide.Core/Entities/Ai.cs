namespace XForge.Ide.Core.Entities;

/// <summary>Provedor de modelo de IA configurado (pago, gratuito ou local).</summary>
public class AiProvider
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Enums.AiProviderKind Kind { get; set; } = Enums.AiProviderKind.OpenAI;
    public string Name { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    /// <summary>Referência à credencial no Credential Manager/DPAPI — nunca a chave em si.</summary>
    public string? ApiKeyRef { get; set; }
    public bool IsLocal { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Agente da IDE. ProjectId nulo = agente global; definido = específico do projeto.</summary>
public class AiAgent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;       // Architect, Implementer, Fixer, Debugger...
    public string Role { get; set; } = string.Empty;
    public string? SystemPromptJson { get; set; }
    public string? ToolsJson { get; set; }
    public string? ModelRef { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Skill/comando customizável (global, por projeto ou por agente).</summary>
public class AiSkill
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? AgentId { get; set; }
    public Guid? ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? ParamsJson { get; set; }
}

public class AiConversation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProjectId { get; set; }
    public Guid? AgentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<AiMessage> Messages { get; set; } = new();
}

public class AiMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ConversationId { get; set; }

    /// <summary>user | assistant | system | tool</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string? ToolCallsJson { get; set; }
    public int? Tokens { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
