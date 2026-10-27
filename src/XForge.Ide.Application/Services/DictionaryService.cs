using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using XForge.Ide.Core.Results;
using Dto = XForge.Ide.Application.Dto;

namespace XForge.Ide.Application.Services;

/// <summary>
/// Casos de uso do dicionário de dados (sucessor do Dictionary Editor do Clarion).
/// Fonte da verdade para os wizards RAD e para o mapeamento EF Core.
/// </summary>
public class DictionaryService
{
    private readonly IDictionaryRepository _dictionaries;

    private static readonly string[] KnownClrTypes =
        ["string", "int", "long", "decimal", "double", "DateTime", "bool", "Guid", "byte[]"];

    public DictionaryService(IDictionaryRepository dictionaries) => _dictionaries = dictionaries;

    // ------------------------------------------------------------ Dicionários

    public async Task<IReadOnlyList<Dto.DictionaryDto>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var items = await _dictionaries.ListByProjectAsync(projectId, ct);
        return items.Select(Dto.DictionaryDto.From).ToList();
    }

    public async Task<Dto.EntityDto?> GetEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        var entity = await _dictionaries.GetEntityAsync(entityId, ct);
        return entity is null ? null : Dto.EntityDto.From(entity);
    }

    public async Task<IReadOnlyList<Dto.EntityDto>> ListEntitiesAsync(Guid dictionaryId, CancellationToken ct = default)
    {
        var dictionary = await _dictionaries.GetFullAsync(dictionaryId, ct);
        if (dictionary is null)
            return Array.Empty<Dto.EntityDto>();
        return dictionary.Entities.OrderBy(e => e.Name).Select(Dto.EntityDto.From).ToList();
    }

    public async Task<Result<Dto.DictionaryDto>> CreateAsync(Dto.CreateDictionaryRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return Result<Dto.DictionaryDto>.Failure("Projeto não informado.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Result<Dto.DictionaryDto>.Failure("Nome do dicionário é obrigatório.");

        var existing = await _dictionaries.ListByProjectAsync(request.ProjectId, ct);
        if (existing.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Result<Dto.DictionaryDto>.Failure($"Já existe um dicionário '{name}' neste projeto.");

        var dictionary = new IdeDictionary { ProjectId = request.ProjectId, Name = name };
        await _dictionaries.AddAsync(dictionary, ct);
        return Result<Dto.DictionaryDto>.Success(Dto.DictionaryDto.From(dictionary));
    }

    public async Task<Result> DeleteAsync(Guid dictionaryId, CancellationToken ct = default)
    {
        await _dictionaries.DeleteAsync(dictionaryId, ct);
        return Result.Success();
    }

    /// <summary>Renomeia o dicionário (≈ renomear o .DCT no Clarion).</summary>
    public async Task<Result<Dto.DictionaryDto>> RenameAsync(
        Dto.UpdateDictionaryRequest request, CancellationToken ct = default)
    {
        var dictionary = await _dictionaries.GetAsync(request.DictionaryId, ct);
        if (dictionary is null)
            return Result<Dto.DictionaryDto>.Failure("Dicionário não encontrado.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Result<Dto.DictionaryDto>.Failure("Nome do dicionário é obrigatório.");

        var projectDictionaries = await _dictionaries.ListByProjectAsync(dictionary.ProjectId, ct);
        if (projectDictionaries.Any(d => d.Id != dictionary.Id && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Result<Dto.DictionaryDto>.Failure($"Já existe um dicionário '{name}' neste projeto.");

        dictionary.Name = name;
        await _dictionaries.UpdateAsync(dictionary, ct);
        return Result<Dto.DictionaryDto>.Success(Dto.DictionaryDto.From(dictionary));
    }

    // --------------------------------------------------------------- Entidades

    public async Task<Result<Dto.EntityDto>> AddEntityAsync(Dto.CreateEntityRequest request, CancellationToken ct = default)
    {
        var dictionary = await _dictionaries.GetFullAsync(request.DictionaryId, ct);
        if (dictionary is null)
            return Result<Dto.EntityDto>.Failure("Dicionário não encontrado.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || !char.IsUpper(name[0]))
            return Result<Dto.EntityDto>.Failure("Nome da entidade deve começar com maiúscula (ex.: Customer).");

        if (dictionary.Entities.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Result<Dto.EntityDto>.Failure($"Entidade '{name}' já existe no dicionário.");

        var tableName = string.IsNullOrWhiteSpace(request.TableName) ? name + "s" : request.TableName.Trim();

        var entity = new Entity
        {
            DictionaryId = dictionary.Id,
            Name = name,
            TableName = tableName,
            Provider = string.IsNullOrWhiteSpace(request.Provider) ? "Sqlite" : request.Provider,
            Prefix = string.IsNullOrWhiteSpace(request.Prefix) ? null : request.Prefix.Trim(),
            ExternalName = string.IsNullOrWhiteSpace(request.ExternalName) ? null : request.ExternalName.Trim(),
        };

        // Chave primária padrão: Id (Guid) — best practice
        entity.Fields.Add(new EntityField
        {
            EntityId = entity.Id,
            Name = "Id",
            ClrType = "Guid",
            DbType = "TEXT",
            Required = true,
            Label = "Código",
            SortOrder = 0,
        });
        entity.Keys.Add(new EntityKey
        {
            EntityId = entity.Id,
            Name = "PK_" + tableName,
            IsPrimary = true,
            IsUnique = true,
            FieldsJson = """["Id"]""",
        });

        await _dictionaries.AddEntityAsync(entity, ct);
        return Result<Dto.EntityDto>.Success(Dto.EntityDto.From(entity));
    }

    public async Task<Result<Dto.EntityDto>> UpdateEntityAsync(Dto.UpdateEntityRequest request, CancellationToken ct = default)
    {
        var entity = await _dictionaries.GetEntityAsync(request.EntityId, ct);
        if (entity is null)
            return Result<Dto.EntityDto>.Failure("Entidade não encontrada.");

        entity.Name = request.Name?.Trim() ?? entity.Name;
        entity.TableName = request.TableName?.Trim() ?? entity.TableName;
        entity.Provider = string.IsNullOrWhiteSpace(request.Provider) ? entity.Provider : request.Provider;
        entity.Prefix = string.IsNullOrWhiteSpace(request.Prefix) ? null : request.Prefix.Trim();
        entity.ExternalName = string.IsNullOrWhiteSpace(request.ExternalName) ? null : request.ExternalName.Trim();

        await _dictionaries.UpdateEntityAsync(entity, ct);
        return Result<Dto.EntityDto>.Success(Dto.EntityDto.From(entity));
    }

    public async Task<Result> DeleteEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        await _dictionaries.DeleteEntityAsync(entityId, ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ Campos

    public async Task<Result<Dto.EntityFieldDto>> AddFieldAsync(Dto.CreateFieldRequest request, CancellationToken ct = default)
    {
        var entity = await _dictionaries.GetEntityAsync(request.EntityId, ct);
        if (entity is null)
            return Result<Dto.EntityFieldDto>.Failure("Entidade não encontrada.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Result<Dto.EntityFieldDto>.Failure("Nome do campo é obrigatório.");

        if (entity.Fields.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Result<Dto.EntityFieldDto>.Failure($"Campo '{name}' já existe na entidade {entity.Name}.");

        if (!KnownClrTypes.Contains(request.ClrType))
            return Result<Dto.EntityFieldDto>.Failure(
                $"Tipo CLR '{request.ClrType}' não suportado. Use: {string.Join(", ", KnownClrTypes)}.");

        var field = new EntityField
        {
            EntityId = entity.Id,
            Name = name,
            ClrType = request.ClrType,
            DbType = string.IsNullOrWhiteSpace(request.DbType) ? DefaultDbType(request.ClrType) : request.DbType,
            Length = request.Length,
            Precision = request.Precision,
            Required = request.Required,
            Default = string.IsNullOrWhiteSpace(request.Default) ? null : request.Default.Trim(),
            Picture = string.IsNullOrWhiteSpace(request.Picture) ? null : request.Picture.Trim(),
            Label = string.IsNullOrWhiteSpace(request.Label) ? name : request.Label.Trim(),
            ExternalName = string.IsNullOrWhiteSpace(request.ExternalName) ? null : request.ExternalName.Trim(),
            SortOrder = entity.Fields.Count,
        };

        await _dictionaries.AddFieldAsync(field, ct);
        return Result<Dto.EntityFieldDto>.Success(Dto.EntityFieldDto.From(field));
    }

    public async Task<Result<Dto.EntityFieldDto>> UpdateFieldAsync(Dto.UpdateFieldRequest request, CancellationToken ct = default)
    {
        var field = await _dictionaries.GetFieldAsync(request.FieldId, ct);
        if (field is null)
            return Result<Dto.EntityFieldDto>.Failure("Campo não encontrado.");

        if (!KnownClrTypes.Contains(request.ClrType))
            return Result<Dto.EntityFieldDto>.Failure($"Tipo CLR '{request.ClrType}' não suportado.");

        field.Name = request.Name?.Trim() ?? field.Name;
        field.ClrType = request.ClrType;
        field.DbType = string.IsNullOrWhiteSpace(request.DbType) ? DefaultDbType(request.ClrType) : request.DbType;
        field.Length = request.Length;
        field.Precision = request.Precision;
        field.Required = request.Required;
        field.Default = string.IsNullOrWhiteSpace(request.Default) ? null : request.Default.Trim();
        field.Picture = string.IsNullOrWhiteSpace(request.Picture) ? null : request.Picture.Trim();
        field.Label = string.IsNullOrWhiteSpace(request.Label) ? field.Name : request.Label.Trim();
        field.ExternalName = string.IsNullOrWhiteSpace(request.ExternalName) ? null : request.ExternalName.Trim();

        await _dictionaries.UpdateFieldAsync(field, ct);
        return Result<Dto.EntityFieldDto>.Success(Dto.EntityFieldDto.From(field));
    }

    public async Task<Result> DeleteFieldAsync(Guid fieldId, CancellationToken ct = default)
    {
        await _dictionaries.DeleteFieldAsync(fieldId, ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------- Chaves

    public async Task<Result<Dto.EntityKeyDto>> AddKeyAsync(Dto.CreateKeyRequest request, CancellationToken ct = default)
    {
        var entity = await _dictionaries.GetEntityAsync(request.EntityId, ct);
        if (entity is null)
            return Result<Dto.EntityKeyDto>.Failure("Entidade não encontrada.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Result<Dto.EntityKeyDto>.Failure("Nome da chave é obrigatório.");

        if (entity.Keys.Any(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Result<Dto.EntityKeyDto>.Failure($"Chave '{name}' já existe.");

        if (request.IsPrimary && entity.Keys.Any(k => k.IsPrimary))
            return Result<Dto.EntityKeyDto>.Failure("A entidade já possui chave primária.");

        var key = new EntityKey
        {
            EntityId = entity.Id,
            Name = name,
            IsPrimary = request.IsPrimary,
            IsUnique = request.IsUnique,
            FieldsJson = string.IsNullOrWhiteSpace(request.FieldsJson) ? "[]" : request.FieldsJson,
        };

        await _dictionaries.AddKeyAsync(key, ct);
        return Result<Dto.EntityKeyDto>.Success(Dto.EntityKeyDto.From(key));
    }

    public async Task<Result> DeleteKeyAsync(Guid keyId, CancellationToken ct = default)
    {
        await _dictionaries.DeleteKeyAsync(keyId, ct);
        return Result.Success();
    }

    public async Task<Result> UpdateKeyAsync(Dto.UpdateKeyRequest request, CancellationToken ct = default)
    {
        var key = await _dictionaries.GetKeyAsync(request.KeyId, ct);
        if (key is null)
        {
            return Result.Failure("Chave nao encontrada.");
        }
        key.Name = request.Name;
        key.IsPrimary = request.IsPrimary;
        key.IsUnique = request.IsUnique;
        key.FieldsJson = request.FieldsJson;
        await _dictionaries.UpdateKeyAsync(key, ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<Dto.EntityRelationDto>> ListRelationsAsync(Guid dictionaryId, CancellationToken ct = default)
    {
        var relations = await _dictionaries.ListRelationsAsync(dictionaryId, ct);
        return relations.Select(Dto.EntityRelationDto.From).ToList();
    }

    public async Task<Result<Dto.EntityRelationDto>> AddRelationAsync(Dto.CreateRelationRequest request, CancellationToken ct = default)
    {
        var relation = new EntityRelation
        {
            DictionaryId = request.DictionaryId,
            FromEntityId = request.FromEntityId,
            ToEntityId = request.ToEntityId,
            Kind = request.Kind,
            FieldsJson = request.FieldsJson,
        };
        await _dictionaries.AddRelationAsync(relation, ct);
        return Result<Dto.EntityRelationDto>.Success(Dto.EntityRelationDto.From(relation));
    }

    public async Task<Result> DeleteRelationAsync(Guid relationId, CancellationToken ct = default)
    {
        await _dictionaries.DeleteRelationAsync(relationId, ct);
        return Result.Success();
    }

    /// <summary>Diagrama ER: entidades com campos e arestas de relacionamento.</summary>
    public async Task<Dto.ErDiagramDto?> GetErDiagramAsync(Guid dictionaryId, CancellationToken ct = default)
    {
        var dictionary = await _dictionaries.GetFullAsync(dictionaryId, ct);
        if (dictionary is null)
        {
            return null;
        }
        var relations = await _dictionaries.ListRelationsAsync(dictionaryId, ct);
        return Dto.ErDiagramDto.From(dictionary.Entities, relations);
    }

    /// <summary>Salva um dicionário inferido por engenharia reversa.</summary>
    public async Task<Result<Dto.DictionaryDto>> SaveInferredAsync(
        Guid projectId, string dictionaryName, IReadOnlyList<InferredTable> tables, CancellationToken ct = default)
    {
        var created = await CreateAsync(new Dto.CreateDictionaryRequest(projectId, dictionaryName), ct);
        if (!created.IsSuccess)
            return created;

        var dictionary = await _dictionaries.GetFullAsync(created.Value.Id, ct)
            ?? throw new InvalidOperationException("Dicionário recém-criado não encontrado.");

        foreach (var table in tables)
        {
            var entity = new Entity
            {
                DictionaryId = dictionary.Id,
                Name = ToPascalCase(table.Name),
                TableName = table.Name,
                Provider = "Sqlite",
            };

            foreach (var column in table.Columns)
            {
                entity.Fields.Add(new EntityField
                {
                    EntityId = entity.Id,
                    Name = ToPascalCase(column.Name),
                    ClrType = MapClrType(column.DbType),
                    DbType = column.DbType,
                    Required = column.Required || column.IsPrimaryKey,
                    Label = ToPascalCase(column.Name),
                    ExternalName = column.Name,
                    SortOrder = column.Ordinal,
                });
            }

            var pk = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
            if (pk is not null)
            {
                entity.Keys.Add(new EntityKey
                {
                    EntityId = entity.Id,
                    Name = "PK_" + table.Name,
                    IsPrimary = true,
                    IsUnique = true,
                    FieldsJson = System.Text.Json.JsonSerializer.Serialize(new[] { ToPascalCase(pk.Name) }),
                });
            }

            // Índices declarados (exclui autoindexes internos do SQLite)
            foreach (var index in table.Indexes.Where(i => !i.Name.StartsWith("sqlite_autoindex", StringComparison.Ordinal)))
            {
                var indexColumns = index.Columns.Select(ToPascalCase).ToList();
                if (indexColumns.Count == 0)
                {
                    continue;
                }
                entity.Keys.Add(new EntityKey
                {
                    EntityId = entity.Id,
                    Name = index.Name,
                    IsPrimary = false,
                    IsUnique = index.IsUnique,
                    FieldsJson = System.Text.Json.JsonSerializer.Serialize(indexColumns),
                });
            }

        await _dictionaries.AddEntityAsync(entity, ct);
    }

    return Result<Dto.DictionaryDto>.Success(
        new Dto.DictionaryDto(dictionary.Id, dictionary.ProjectId, dictionary.Name, dictionary.Version, tables.Count));
    }

    // ----------------------------------------------------------------- Helpers

    internal static string DefaultDbType(string clrType) => clrType switch
    {
        "int" or "long" or "bool" => "INTEGER",
        "decimal" => "NUMERIC",
        "double" => "REAL",
        "byte[]" => "BLOB",
        _ => "TEXT",
    };

    private static string MapClrType(string dbType) => dbType.ToUpperInvariant() switch
    {
        "INTEGER" or "INT" or "BIGINT" => "long",
        "REAL" or "FLOAT" or "DOUBLE" => "double",
        "NUMERIC" or "DECIMAL" => "decimal",
        "BLOB" => "byte[]",
        _ => "string",
    };

    private static string ToPascalCase(string value)
    {
        var parts = value.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(string.Empty, parts.Select(p =>
            p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
