using Microsoft.EntityFrameworkCore;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Infrastructure.Persistence;

public class DictionaryRepository : IDictionaryRepository
{
    private readonly IdeDbContext _db;

    public DictionaryRepository(IdeDbContext db) => _db = db;

    public async Task<IReadOnlyList<IdeDictionary>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => await _db.Dictionaries
            .AsNoTracking()
            .Include(d => d.Entities)
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

    public Task<IdeDictionary?> GetAsync(Guid id, CancellationToken ct = default)
        => _db.Dictionaries.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IdeDictionary?> GetFullAsync(Guid id, CancellationToken ct = default)
        => await _db.Dictionaries
            .AsNoTracking()
            .Include(d => d.Entities)
                .ThenInclude(e => e.Fields)
                    .ThenInclude(f => f.Validation)
            .Include(d => d.Entities)
                .ThenInclude(e => e.Keys)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Entity?> GetEntityAsync(Guid entityId, CancellationToken ct = default)
        => await _db.Entities
            .AsNoTracking()
            .Include(e => e.Fields)
                .ThenInclude(f => f.Validation)
            .Include(e => e.Keys)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == entityId, ct);

    public async Task<EntityField?> GetFieldAsync(Guid fieldId, CancellationToken ct = default)
        => await _db.EntityFields
            .Include(f => f.Validation)
            .FirstOrDefaultAsync(f => f.Id == fieldId, ct);

    public Task<EntityKey?> GetKeyAsync(Guid keyId, CancellationToken ct = default)
        => _db.EntityKeys.FirstOrDefaultAsync(k => k.Id == keyId, ct);

    public async Task AddAsync(IdeDictionary dictionary, CancellationToken ct = default)
    {
        _db.Dictionaries.Add(dictionary);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(IdeDictionary dictionary, CancellationToken ct = default)
    {
        if (_db.Entry(dictionary).State == EntityState.Detached)
        {
            _db.Dictionaries.Update(dictionary);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _db.Dictionaries.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task AddEntityAsync(Entity entity, CancellationToken ct = default)
    {
        // Grafo adicionado explicitamente: filhos adicionados apenas pela navegação
        // (sem navegação inversa) podem ficar Detached no InMemory.
        _db.Entities.Add(entity);
        foreach (var field in entity.Fields)
        {
            _db.EntityFields.Add(field);
        }
        foreach (var key in entity.Keys)
        {
            _db.EntityKeys.Add(key);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateEntityAsync(Entity entity, CancellationToken ct = default)
    {
        if (_db.Entry(entity).State == EntityState.Detached)
        {
            _db.Entities.Update(entity);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        await _db.Entities.Where(e => e.Id == entityId).ExecuteDeleteAsync(ct);
    }

    public async Task AddFieldAsync(EntityField field, CancellationToken ct = default)
    {
        _db.EntityFields.Add(field);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateFieldAsync(EntityField field, CancellationToken ct = default)
    {
        if (_db.Entry(field).State == EntityState.Detached)
        {
            // desanexa instância rastreada com a mesma chave (o DbContext do
            // circuito Blazor sobrevive entre operações)
            var rastreado = _db.ChangeTracker.Entries<EntityField>()
                .FirstOrDefault(e => e.Entity.Id == field.Id);
            rastreado?.State = EntityState.Detached;

            _db.EntityFields.Update(field);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteFieldAsync(Guid fieldId, CancellationToken ct = default)
    {
        await _db.EntityFields.Where(f => f.Id == fieldId).ExecuteDeleteAsync(ct);
    }

    public async Task AddKeyAsync(EntityKey key, CancellationToken ct = default)
    {
        _db.EntityKeys.Add(key);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateKeyAsync(EntityKey key, CancellationToken ct = default)
    {
        if (_db.Entry(key).State == EntityState.Detached)
        {
            _db.EntityKeys.Update(key);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteKeyAsync(Guid keyId, CancellationToken ct = default)
    {
        await _db.EntityKeys.Where(k => k.Id == keyId).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<EntityRelation>> ListRelationsAsync(Guid dictionaryId, CancellationToken ct = default)
        => await _db.EntityRelations
            .AsNoTracking()
            .Include(r => r.FromEntity!)
                .ThenInclude(e => e.Fields)
            .Include(r => r.ToEntity!)
                .ThenInclude(e => e.Fields)
            .AsSplitQuery()
            .Where(r => r.DictionaryId == dictionaryId)
            .OrderBy(r => r.FromEntity!.Name)
            .ThenBy(r => r.ToEntity!.Name)
            .ToListAsync(ct);

    public async Task AddRelationAsync(EntityRelation relation, CancellationToken ct = default)
    {
        _db.EntityRelations.Add(relation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteRelationAsync(Guid relationId, CancellationToken ct = default)
    {
        await _db.EntityRelations.Where(r => r.Id == relationId).ExecuteDeleteAsync(ct);
    }

    // -------------------------------------------------------- colunas globais

    public async Task<IReadOnlyList<GlobalField>> ListGlobalFieldsAsync(Guid dictionaryId, CancellationToken ct = default)
        => await _db.GlobalFields
            .Where(g => g.DictionaryId == dictionaryId)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

    public Task<GlobalField?> GetGlobalFieldAsync(Guid id, CancellationToken ct = default)
        => _db.GlobalFields.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<IReadOnlyList<GlobalField>> ListGlobalFieldsByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<GlobalField>();
        }
        return await _db.GlobalFields
            .AsNoTracking()
            .Where(g => ids.Contains(g.Id))
            .ToListAsync(ct);
    }

    public async Task AddGlobalFieldAsync(GlobalField field, CancellationToken ct = default)
    {
        _db.GlobalFields.Add(field);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateGlobalFieldAsync(GlobalField field, CancellationToken ct = default)
    {
        if (_db.Entry(field).State == EntityState.Detached)
        {
            _db.GlobalFields.Update(field);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteGlobalFieldAsync(Guid id, CancellationToken ct = default)
    {
        // desvincula os campos derivados ANTES de remover o modelo (eles permanecem)
        var dependentes = await _db.EntityFields
            .Where(f => f.DerivedFromId == id)
            .ToListAsync(ct);
        foreach (var f in dependentes)
        {
            f.DerivedFromId = null;
        }
        var modelo = await _db.GlobalFields.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (modelo is not null)
        {
            _db.GlobalFields.Remove(modelo);
        }
        await _db.SaveChangesAsync(ct);
    }

    // -------------------------------------------------------- verificações de validade

    public Task<FieldValidation?> GetFieldValidationAsync(Guid fieldId, CancellationToken ct = default)
        => _db.FieldValidations.FirstOrDefaultAsync(v => v.FieldId == fieldId, ct);

    public async Task AddFieldValidationAsync(FieldValidation validation, CancellationToken ct = default)
    {
        _db.FieldValidations.Add(validation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateFieldValidationAsync(FieldValidation validation, CancellationToken ct = default)
    {
        if (_db.Entry(validation).State == EntityState.Detached)
        {
            var rastreada = _db.ChangeTracker.Entries<FieldValidation>()
                .FirstOrDefault(e => e.Entity.Id == validation.Id);
            rastreada?.State = EntityState.Detached;
            _db.FieldValidations.Update(validation);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteFieldValidationAsync(Guid fieldId, CancellationToken ct = default)
    {
        await _db.FieldValidations.Where(v => v.FieldId == fieldId).ExecuteDeleteAsync(ct);
    }
}
