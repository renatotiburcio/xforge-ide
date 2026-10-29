namespace XForge.Ide.Core.Enums;

public enum EntityRelationKind
{
    OneToMany,
    ManyToOne,
    OneToOne,
    ManyToMany
}

/// <summary>
/// Verificação de validade de um campo (≈ "Column Validation" do DCT do Clarion:
/// Choices, Must be in table, limites). Fica no dicionário — a regra é do modelo,
/// não de uma tela.
/// </summary>
public enum FieldValidationKind
{
    None,
    /// <summary>Lista de escolhas ("A;Ativo" por linha).</summary>
    Choices,
    /// <summary>Faixa de valores ("min;max"; vazio = sem limite).</summary>
    Range,
    /// <summary>O valor deve existir em uma coluna de outra entidade ("Must be in table").</summary>
    MustExistIn
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
