using XForge.Ide.Core.Entities;
using XForge.Ide.Core.Enums;

namespace XForge.Ide.Application.Dto;

public record DictionaryDto(Guid Id, Guid ProjectId, string Name, int Version, int EntityCount)
{
    public static DictionaryDto From(IdeDictionary d) => new(d.Id, d.ProjectId, d.Name, d.Version, d.Entities.Count);
}

public record EntityDto(
    Guid Id,
    string Name,
    string TableName,
    string Provider,
    string? Prefix,
    string? ExternalName,
    IReadOnlyList<EntityFieldDto> Fields,
    IReadOnlyList<EntityKeyDto> Keys)
{
    public static EntityDto From(Entity e) => new(
        e.Id, e.Name, e.TableName, e.Provider, e.Prefix, e.ExternalName,
        e.Fields.OrderBy(f => f.SortOrder).Select(EntityFieldDto.From).ToList(),
        e.Keys.Select(EntityKeyDto.From).ToList());
}

public record EntityFieldDto(
    Guid Id,
    string Name,
    string ClrType,
    string DbType,
    int? Length,
    int? Precision,
    bool Required,
    string? Default,
    string? Picture,
    string? Label,
    string? ExternalName,
    int SortOrder)
{
    public static EntityFieldDto From(EntityField f) => new(
        f.Id, f.Name, f.ClrType, f.DbType, f.Length, f.Precision, f.Required,
        f.Default, f.Picture, f.Label, f.ExternalName, f.SortOrder);
}

public record EntityKeyDto(Guid Id, string Name, bool IsPrimary, bool IsUnique, string FieldsJson)
{
    public static EntityKeyDto From(EntityKey k) => new(k.Id, k.Name, k.IsPrimary, k.IsUnique, k.FieldsJson);
}

/// <summary>Relacionamento entre entidades do dicionário.</summary>
public record EntityRelationDto(
    Guid Id,
    string FromEntity,
    string ToEntity,
    string Kind,
    string FieldsJson)
{
    public static EntityRelationDto From(EntityRelation r) => new(
        r.Id,
        r.FromEntity?.Name ?? "(desconhecida)",
        r.ToEntity?.Name ?? "(desconhecida)",
        r.Kind.ToString(),
        r.FieldsJson);
}

/// <summary>Símbolo do diagrama ER: uma entidade e seus relacionamentos.</summary>
public record ErDiagramDto(
    IReadOnlyList<ErNodeDto> Nodes,
    IReadOnlyList<ErEdgeDto> Edges)
{
    public static ErDiagramDto From(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<EntityRelation> relations)
    {
        var nodes = new List<ErNodeDto>();
        foreach (var e in entities)
        {
            var primary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in e.Keys.Where(k => k.IsPrimary))
            {
                var names = System.Text.Json.JsonSerializer.Deserialize<string[]>(k.FieldsJson);
                if (names is not null)
                {
                    foreach (var n in names)
                    {
                        primary.Add(n);
                    }
                }
            }

            nodes.Add(new ErNodeDto(
                e.Name,
                e.TableName,
                e.Fields
                    .OrderBy(f => f.SortOrder)
                    .Select(f => new ErFieldDto(f.Name, f.ClrType, f.Required, primary.Contains(f.Name)))
                    .ToList()));
        }

        var edges = relations
            .Select(r => new ErEdgeDto(
                r.Id,
                r.FromEntity?.Name ?? string.Empty,
                r.ToEntity?.Name ?? string.Empty,
                Cardinality(r.Kind)))
            .ToList();

        return new ErDiagramDto(nodes, edges);
    }

    /// <summary>1:N do lado da origem, N:1 do lado do destino (para o diagrama).</summary>
    private static string Cardinality(EntityRelationKind kind) => kind switch
    {
        EntityRelationKind.ManyToOne => "N:1",
        EntityRelationKind.OneToOne => "1:1",
        EntityRelationKind.ManyToMany => "N:N",
        _ => "1:N",
    };
}

public record ErNodeDto(string Name, string TableName, IReadOnlyList<ErFieldDto> Fields);
public record ErFieldDto(string Name, string ClrType, bool Required, bool PrimaryKey);

public record ErEdgeDto(
    Guid Id,
    string From,
    string To,
    string Cardinality);

// ------------------------------------------------------------------ Requests

public record CreateDictionaryRequest(Guid ProjectId, string Name);
public record UpdateDictionaryRequest(Guid DictionaryId, string Name);
public record CreateEntityRequest(
    Guid DictionaryId, string Name, string TableName, string Provider, string? Prefix, string? ExternalName);
public record UpdateEntityRequest(
    Guid EntityId, string Name, string TableName, string Provider, string? Prefix, string? ExternalName);
public record CreateFieldRequest(
    Guid EntityId, string Name, string ClrType, string? DbType, int? Length, int? Precision,
    bool Required, string? Default, string? Picture, string? Label, string? ExternalName);
public record UpdateFieldRequest(
    Guid FieldId, string Name, string ClrType, string DbType, int? Length, int? Precision,
    bool Required, string? Default, string? Picture, string? Label, string? ExternalName);
public record CreateKeyRequest(Guid EntityId, string Name, bool IsPrimary, bool IsUnique, string FieldsJson);
public record UpdateKeyRequest(Guid KeyId, string Name, bool IsPrimary, bool IsUnique, string FieldsJson);
public record CreateRelationRequest(
    Guid DictionaryId, Guid FromEntityId, Guid ToEntityId, EntityRelationKind Kind, string FieldsJson);
