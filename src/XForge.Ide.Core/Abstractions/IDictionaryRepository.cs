using XForge.Ide.Core.Entities;

namespace XForge.Ide.Core.Abstractions;

/// <summary>
/// Persistência do dicionário (agregado: dicionário → entidades → campos/chaves).
/// </summary>
public interface IDictionaryRepository
{
    Task<IReadOnlyList<IdeDictionary>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<IdeDictionary?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Retorna o dicionário com entidades, campos e chaves carregados.</summary>
    Task<IdeDictionary?> GetFullAsync(Guid id, CancellationToken ct = default);

    /// <summary>Retorna a entidade com campos e chaves carregados.</summary>
    Task<Entity?> GetEntityAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Retorna um campo pelo id.</summary>
    Task<EntityField?> GetFieldAsync(Guid fieldId, CancellationToken ct = default);

    /// <summary>Retorna uma chave pelo id.</summary>
    Task<EntityKey?> GetKeyAsync(Guid keyId, CancellationToken ct = default);

    Task AddAsync(IdeDictionary dictionary, CancellationToken ct = default);
    Task UpdateAsync(IdeDictionary dictionary, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task AddEntityAsync(Entity entity, CancellationToken ct = default);
    Task UpdateEntityAsync(Entity entity, CancellationToken ct = default);
    Task DeleteEntityAsync(Guid entityId, CancellationToken ct = default);

    Task AddFieldAsync(EntityField field, CancellationToken ct = default);
    Task UpdateFieldAsync(EntityField field, CancellationToken ct = default);
    Task DeleteFieldAsync(Guid fieldId, CancellationToken ct = default);

    Task AddKeyAsync(EntityKey key, CancellationToken ct = default);
    Task UpdateKeyAsync(EntityKey key, CancellationToken ct = default);
    Task DeleteKeyAsync(Guid keyId, CancellationToken ct = default);

    /// <summary>Relacionamentos do dicionário (com as entidades de origem e destino).</summary>
    Task<IReadOnlyList<EntityRelation>> ListRelationsAsync(Guid dictionaryId, CancellationToken ct = default);

    Task AddRelationAsync(EntityRelation relation, CancellationToken ct = default);
    Task DeleteRelationAsync(Guid relationId, CancellationToken ct = default);

    // -------------------------------------------------------- colunas globais

    Task<IReadOnlyList<GlobalField>> ListGlobalFieldsAsync(Guid dictionaryId, CancellationToken ct = default);
    Task<IReadOnlyList<GlobalField>> ListGlobalFieldsByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<GlobalField?> GetGlobalFieldAsync(Guid id, CancellationToken ct = default);
    Task AddGlobalFieldAsync(GlobalField field, CancellationToken ct = default);
    Task UpdateGlobalFieldAsync(GlobalField field, CancellationToken ct = default);

    /// <summary>Remove o modelo e desvincula os campos derivados (eles permanecem).</summary>
    Task DeleteGlobalFieldAsync(Guid id, CancellationToken ct = default);
}
