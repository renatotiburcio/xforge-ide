using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using XForge.Ide.Core.Enums;
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
        return entity is null ? null : await WithDerivedNamesAsync(Dto.EntityDto.From(entity), ct);
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
        return Result<Dto.EntityDto>.Success(await WithDerivedNamesAsync(Dto.EntityDto.From(entity), ct));
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
        return Result<Dto.EntityDto>.Success(await WithDerivedNamesAsync(Dto.EntityDto.From(entity), ct));
    }

    public async Task<Result> DeleteEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        await _dictionaries.DeleteEntityAsync(entityId, ct);
        return Result.Success();
    }

    // -------------------------------------------------- colunas globais (Derived From)

    public async Task<IReadOnlyList<Dto.GlobalFieldDto>> ListGlobalFieldsAsync(
        Guid dictionaryId, CancellationToken ct = default)
    {
        var items = await _dictionaries.ListGlobalFieldsAsync(dictionaryId, ct);
        return items.Select(Dto.GlobalFieldDto.From).ToList();
    }

    public async Task<Result<Dto.GlobalFieldDto>> AddGlobalFieldAsync(
        Dto.CreateGlobalFieldRequest request, CancellationToken ct = default)
    {
        var dictionary = await _dictionaries.GetFullAsync(request.DictionaryId, ct);
        if (dictionary is null)
            return Result<Dto.GlobalFieldDto>.Failure("Dicionário não encontrado.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Result<Dto.GlobalFieldDto>.Failure("Nome da coluna global é obrigatório.");

        var existentes = await _dictionaries.ListGlobalFieldsAsync(request.DictionaryId, ct);
        if (existentes.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Result<Dto.GlobalFieldDto>.Failure($"Já existe a coluna global '{name}' neste dicionário.");

        if (!KnownClrTypes.Contains(request.ClrType))
            return Result<Dto.GlobalFieldDto>.Failure(
                $"Tipo CLR '{request.ClrType}' não suportado. Use: {string.Join(", ", KnownClrTypes)}.");

        var template = new GlobalField
        {
            DictionaryId = request.DictionaryId,
            Name = name,
            ClrType = request.ClrType,
            Length = request.Length,
            Picture = string.IsNullOrWhiteSpace(request.Picture) ? null : request.Picture.Trim(),
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            Default = string.IsNullOrWhiteSpace(request.Default) ? null : request.Default.Trim(),
            Required = request.Required,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        };

        await _dictionaries.AddGlobalFieldAsync(template, ct);
        return Result<Dto.GlobalFieldDto>.Success(Dto.GlobalFieldDto.From(template));
    }

    /// <summary>
    /// Atualiza o modelo e PROPAGA para todos os campos derivados dele
    /// (≈ alterar uma coluna global no DCT do Clarion).
    /// </summary>
    public async Task<Result<Dto.GlobalFieldDto>> UpdateGlobalFieldAsync(
        Dto.UpdateGlobalFieldRequest request, CancellationToken ct = default)
    {
        var template = await _dictionaries.GetGlobalFieldAsync(request.GlobalFieldId, ct);
        if (template is null)
            return Result<Dto.GlobalFieldDto>.Failure("Coluna global não encontrada.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return Result<Dto.GlobalFieldDto>.Failure("Nome da coluna global é obrigatório.");

        var existentes = await _dictionaries.ListGlobalFieldsAsync(template.DictionaryId, ct);
        if (existentes.Any(g => g.Id != template.Id && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Result<Dto.GlobalFieldDto>.Failure($"Já existe a coluna global '{name}' neste dicionário.");

        template.Name = name;
        template.ClrType = request.ClrType;
        template.Length = request.Length;
        template.Picture = string.IsNullOrWhiteSpace(request.Picture) ? null : request.Picture.Trim();
        template.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        template.Default = string.IsNullOrWhiteSpace(request.Default) ? null : request.Default.Trim();
        template.Required = request.Required;
        template.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        await _dictionaries.UpdateGlobalFieldAsync(template, ct);

        await PropagateAsync(template, ct);
        return Result<Dto.GlobalFieldDto>.Success(Dto.GlobalFieldDto.From(template));
    }

    public async Task<Result> DeleteGlobalFieldAsync(Guid globalFieldId, CancellationToken ct = default)
    {
        var template = await _dictionaries.GetGlobalFieldAsync(globalFieldId, ct);
        if (template is null)
            return Result.Failure("Coluna global não encontrada.");

        // os campos derivados permanecem (desvinculados)
        await _dictionaries.DeleteGlobalFieldAsync(globalFieldId, ct);
        return Result.Success();
    }

    /// <summary>Copia as propriedades do modelo para todos os campos derivados.</summary>
    private async Task PropagateAsync(GlobalField template, CancellationToken ct)
    {
        var dictionary = await _dictionaries.GetFullAsync(template.DictionaryId, ct);
        if (dictionary is null)
        {
            return;
        }

        // campo a campo (UpdateFieldAsync reanexa só o campo; reanexar o grafo
        // inteiro colidiria com instâncias já rastreadas pelo contexto)
        foreach (var f in dictionary.Entities.SelectMany(e => e.Fields).Where(f => f.DerivedFromId == template.Id))
        {
            f.Name = template.Name;
            f.ClrType = template.ClrType;
            f.DbType = DefaultDbType(template.ClrType);
            f.Length = template.Length;
            f.Required = template.Required;
            f.Default = template.Default;
            f.Picture = template.Picture;
            f.Label = template.Label ?? template.Name;
            await _dictionaries.UpdateFieldAsync(f, ct);
        }
    }

    /// <summary>Preenche o nome do modelo em cada campo derivado (para a UI).</summary>
    private async Task<Dto.EntityDto> WithDerivedNamesAsync(Dto.EntityDto dto, CancellationToken ct)
    {
        var ids = dto.Fields
            .Where(f => f.DerivedFromId is not null)
            .Select(f => f.DerivedFromId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return dto;
        }

        var modelos = await _dictionaries.ListGlobalFieldsByIdsAsync(ids, ct);
        if (modelos.Count == 0)
        {
            return dto;
        }

        var porId = modelos.ToDictionary(g => g.Id);
        var campos = dto.Fields
            .Select(f => f with
            {
                DerivedFromName = f.DerivedFromId is Guid id && porId.TryGetValue(id, out var g)
                    ? g.Name
                    : null,
            })
            .ToList();
        return dto with { Fields = campos };
    }

    /// <summary>Versão para um campo só (retornos de Add/Update).</summary>
    private async Task<Dto.EntityFieldDto> WithDerivedNameAsync(Dto.EntityFieldDto dto, CancellationToken ct)
    {
        if (dto.DerivedFromId is not Guid id)
        {
            return dto;
        }

        var modelo = await _dictionaries.GetGlobalFieldAsync(id, ct);
        return modelo is null ? dto : dto with { DerivedFromName = modelo.Name };
    }

    // --------------------------------------------- verificações de validade (Column Validation)

    /// <summary>
    /// Grava a verificação de validade do campo (upsert). Com
    /// <see cref="FieldValidationKind.None"/> remove a regra existente.
    /// </summary>
    public async Task<Result<Dto.FieldValidationDto>> SaveFieldValidationAsync(
        Dto.SaveFieldValidationRequest request, CancellationToken ct = default)
    {
        var field = await _dictionaries.GetFieldAsync(request.FieldId, ct);
        if (field is null)
        {
            return Result<Dto.FieldValidationDto>.Failure("Campo não encontrado.");
        }

        var existente = await _dictionaries.GetFieldValidationAsync(field.Id, ct);

        if (request.Kind == FieldValidationKind.None)
        {
            if (existente is not null)
            {
                await _dictionaries.DeleteFieldValidationAsync(field.Id, ct);
            }
            return Result<Dto.FieldValidationDto>.Success(
                new Dto.FieldValidationDto(null, FieldValidationKind.None, null, null));
        }

        var data = (request.Data ?? string.Empty).Trim();
        var erro = await ValidateValidationDataAsync(field, request.Kind, data, ct);
        if (erro is not null)
        {
            return Result<Dto.FieldValidationDto>.Failure(erro);
        }

        var message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim();

        if (existente is null)
        {
            var nova = new FieldValidation
            {
                FieldId = field.Id,
                Kind = request.Kind,
                Data = data,
                Message = message,
            };
            await _dictionaries.AddFieldValidationAsync(nova, ct);
            return Result<Dto.FieldValidationDto>.Success(Dto.FieldValidationDto.From(nova));
        }

        existente.Kind = request.Kind;
        existente.Data = data;
        existente.Message = message;
        await _dictionaries.UpdateFieldValidationAsync(existente, ct);
        return Result<Dto.FieldValidationDto>.Success(Dto.FieldValidationDto.From(existente));
    }

    /// <summary>Valida os parâmetros conforme o tipo (formato puro + existência do alvo).</summary>
    private async Task<string?> ValidateValidationDataAsync(
        EntityField field, FieldValidationKind kind, string data, CancellationToken ct)
    {
        switch (kind)
        {
            case FieldValidationKind.Choices:
                return FieldValidationRules.ValidateChoicesData(data);

            case FieldValidationKind.Range:
                return FieldValidationRules.ValidateRangeData(data);

            case FieldValidationKind.MustExistIn:
                var alvo = FieldValidationRules.ParseMustExistIn(data);
                if (alvo is null)
                {
                    return "Alvo inválido: escolha a entidade e o campo de referência.";
                }
                var (entityId, fieldName) = alvo.Value;
                var dona = await _dictionaries.GetEntityAsync(field.EntityId, ct);
                if (dona is null)
                {
                    return "Campo não encontrado.";
                }
                if (entityId == dona.Id)
                {
                    return "\"Deve existir em\" aponta para OUTRA entidade, não para a própria.";
                }
                var dictionary = await _dictionaries.GetFullAsync(dona.DictionaryId, ct);
                var target = dictionary?.Entities.FirstOrDefault(e => e.Id == entityId);
                if (target is null)
                {
                    return "Entidade de referência não encontrada neste dicionário.";
                }
                if (!target.Fields.Any(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"O campo '{fieldName}' não existe na entidade {target.Name}.";
                }
                return null;

            default:
                return "Verificação de validade desconhecida.";
        }
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

        // ≈ Derived From: o modelo de coluna global preenche o campo novo
        Guid? derivedFrom = null;
        if (request.DerivedFromId is Guid templateId)
        {
            var template = await _dictionaries.GetGlobalFieldAsync(templateId, ct);
            if (template is null || template.DictionaryId != entity.DictionaryId)
                return Result<Dto.EntityFieldDto>.Failure("Modelo de coluna global não encontrado neste dicionário.");
            derivedFrom = template.Id;
            name = template.Name;
            if (entity.Fields.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return Result<Dto.EntityFieldDto>.Failure($"Campo '{name}' já existe na entidade {entity.Name}.");

            request = request with
            {
                Name = name,
                ClrType = template.ClrType,
                Length = template.Length,
                DbType = DefaultDbType(template.ClrType),
                Required = template.Required,
                Default = template.Default,
                Picture = template.Picture,
                Label = template.Label ?? name,
            };
        }

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
            DerivedFromId = derivedFrom,
        };

        await _dictionaries.AddFieldAsync(field, ct);
        return Result<Dto.EntityFieldDto>.Success(
            await WithDerivedNameAsync(Dto.EntityFieldDto.From(field), ct));
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
        return Result<Dto.EntityFieldDto>.Success(
            await WithDerivedNameAsync(Dto.EntityFieldDto.From(field), ct));
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
