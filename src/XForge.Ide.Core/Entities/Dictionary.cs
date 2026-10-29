namespace XForge.Ide.Core.Entities;

/// <summary>
/// Dicionário de dados do projeto (sucessor do .DCT do Clarion).
/// Fonte da verdade para os wizards RAD e para o mapeamento EF Core.
/// </summary>
public class IdeDictionary
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Entity> Entities { get; set; } = new();
}

/// <summary>Entidade de negócio (≈ arquivo/tabela do dicionário Clarion).</summary>
public class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DictionaryId { get; set; }

    public string Name { get; set; } = string.Empty;      // ex.: Customer
    public string TableName { get; set; } = string.Empty; // ex.: Customers
    public string Provider { get; set; } = "Sqlite";      // Sqlite | MySql | PostgreSql | SqlServer
    public string? Prefix { get; set; }                   // ≈ prefixo Clarion (ex.: ETI)
    public string? ExternalName { get; set; }             // nome físico externo (SQL)

    public List<EntityField> Fields { get; set; } = new();
    public List<EntityKey> Keys { get; set; } = new();
}

/// <summary>Campo da entidade.</summary>
public class EntityField
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EntityId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string ClrType { get; set; } = "string";       // string | int | long | decimal | DateTime | bool | Guid
    public string DbType { get; set; } = "TEXT";          // mapeamento físico
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public bool Required { get; set; }
    public string? Default { get; set; }

    /// <summary>Máscara herdada do picture Clarion (ex.: @s50 → máscara XF).</summary>
    public string? Picture { get; set; }

    public string? Label { get; set; }
    public string? ExternalName { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Modelo de coluna global de onde este campo foi derivado (≈ Derived From).</summary>
    public Guid? DerivedFromId { get; set; }

    /// <summary>Verificação de validade do campo (≈ Column Validation; 1:1, criada sob demanda).</summary>
    public FieldValidation? Validation { get; set; }
}

/// <summary>Chave/índice da entidade (≈ ChaveCodigo do Clarion).</summary>
public class EntityKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EntityId { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsUnique { get; set; }

    /// <summary>JSON array com os nomes de campo componentes.</summary>
    public string FieldsJson { get; set; } = "[]";
}

/// <summary>Relacionamento entre entidades.</summary>
public class EntityRelation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DictionaryId { get; set; }
    public Guid FromEntityId { get; set; }
    public Guid ToEntityId { get; set; }
    public Enums.EntityRelationKind Kind { get; set; } = Enums.EntityRelationKind.OneToMany;
    public string FieldsJson { get; set; } = "[]";

    /// <summary>Entidade do lado "muitos"/origem.</summary>
    public Entity? FromEntity { get; set; }

    /// <summary>Entidade do lado "um"/destino.</summary>
    public Entity? ToEntity { get; set; }
}
