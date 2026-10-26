namespace XForge.Ide.Core.Enums;

public enum EntityRelationKind
{
    OneToMany,
    ManyToOne,
    OneToOne,
    ManyToMany
}

public enum AiProviderKind
{
    OpenAI,
    Anthropic,
    Google,
    Ollama,
    OpenRouter,
    Groq,
    Azure,
    Custom
}

/// <summary>Provedor de banco de dados para apps gerados.</summary>
public enum ProviderKind
{
    Sqlite,
    PostgreSql,
    MySql,
    SqlServer
}
