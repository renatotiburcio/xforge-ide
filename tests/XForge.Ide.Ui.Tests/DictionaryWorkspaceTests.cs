using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XForge.Ide.App.Components;
using XForge.Ide.Application.Dto;
using XForge.Ide.Application.Services;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using XForge.Ide.Core.Enums;
using Xunit;

namespace XForge.Ide.Ui.Tests;

/// <summary>
/// Workspace do dicionário com seleção dirigida pela URL (?field= e ?tab=):
/// o F5 devolve a coluna selecionada e a aba do Property Sheet.
/// </summary>
public class DictionaryWorkspaceTests : BunitTestBase
{
    private readonly StubDictionaryRepository _repo;

    public DictionaryWorkspaceTests()
    {
        // os testes exercitam só render/clique — nada de banco
        _repo = new StubDictionaryRepository();
        Context.Services.AddSingleton(new DictionaryService(_repo));
    }

    /// <summary>Campo devolvido pelo stub quando o serviço precisa recarregar (salvar validação).</summary>
    protected EntityField StubField(Guid entityId, Guid fieldId, string name = "Status")
        => _repo.SetField(entityId, fieldId, name);

    private static EntityDto Entity()
    {
        var id = Guid.NewGuid();
        var nome = new EntityFieldDto(Guid.NewGuid(), "Nome", "string", "TEXT", 50, null, false, null, null, "Nome", null, 1);
        var criado = new EntityFieldDto(Guid.NewGuid(), "CriadoEm", "DateTime", "TEXT", null, null, true, null, null, "Criado em", null, 2);
        return new EntityDto(
            id, "Cliente", "Clientes", "Sqlite", null, null,
            new[] { nome, criado }, Array.Empty<EntityKeyDto>());
    }

    private static EntityFieldDto[] Fields(EntityDto entity) => entity.Fields.ToArray();

    [Fact]
    public void FieldId_da_URL_restaura_a_coluna_selecionada()
    {
        var entity = Entity();
        var esperado = Fields(entity)[1];

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entity)
            .Add(p => p.Entities, new[] { entity })
            .Add(p => p.FieldId, esperado.Id));

        // cabeçalho do Property Sheet mostra a coluna restaurada
        cut.Find(".ide-dict__props-head").TextContent.Should().Contain(esperado.Name);
        cut.Find(".ide-dict__row--active").TextContent.Should().Contain(esperado.Name);
    }

    [Fact]
    public void Clique_na_linha_avisa_o_pai_para_gravar_na_URL()
    {
        var entity = Entity();
        var alvo = Fields(entity)[1];
        Guid? notificado = null;

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entity)
            .Add(p => p.Entities, new[] { entity })
            .Add(p => p.FieldIdChanged, EventCallback.Factory.Create<Guid?>(this, id => notificado = id)));

        cut.FindAll(".ide-dict__row")[1].Click();

        notificado.Should().Be(alvo.Id);
    }

    [Fact]
    public void Clique_na_aba_avisa_o_pai_com_o_indice_da_aba()
    {
        var entity = Entity();
        var notificados = new List<int>();

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entity)
            .Add(p => p.Entities, new[] { entity })
            .Add(p => p.TabIndexChanged, EventCallback.Factory.Create<int>(this, tab => notificados.Add(tab))));

        // as abas só existem com uma coluna selecionada
        cut.FindAll(".ide-dict__row")[0].Click();
        cut.Find("#ide-dict-tab-picture").Click();
        cut.Find("#ide-dict-tab-advanced").Click();

        notificados.Should().Equal(1, 2);
    }

    [Fact]
    public void TabIndex_da_URL_abre_a_aba_correta()
    {
        var entity = Entity();

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entity)
            .Add(p => p.Entities, new[] { entity })
            .Add(p => p.FieldId, Fields(entity)[0].Id)
            .Add(p => p.TabIndex, 2));

        // painel "Avançado" visível (campo exclusivo da aba)
        cut.Find("#ide-dict-panel-advanced").Should().NotBeNull();
        cut.Find("#ide-dict-panel-advanced").TextContent.Should().Contain("Nome externo (SQL)");
        cut.Find("#ide-dict-tab-advanced").GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void Sem_FieldId_nada_fica_selecionado()
    {
        var entity = Entity();

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entity)
            .Add(p => p.Entities, new[] { entity }));

        cut.FindAll(".ide-dict__row--active").Should().BeEmpty();
        cut.Find(".ide-dict__props").TextContent.Should().Contain("Selecione uma coluna");
    }

    [Fact]
    public void Modal_de_adicionar_coluna_oferece_derivar_da_coluna_global()
    {
        var entity = Entity();
        var modelo = new GlobalFieldDto(
            Guid.NewGuid(), Guid.NewGuid(), "NomeCliente", "string", 50, "@s40", "Nome do cliente", null, true, null);

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entity)
            .Add(p => p.Entities, new[] { entity })
            .Add(p => p.GlobalFields, new[] { modelo }));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Adicionar coluna").Click();

        // combobox "Derivar de" antes dos campos manuais
        var combo = cut.FindAll(".xf-combobox")[0];
        combo.TextContent.Should().Contain("Derivar de (opcional)");

        // focar abre a lista (OpenOnFocus) e o modelo aparece como opção
        cut.Find(".xf-combobox input").TriggerEvent("onfocus", new FocusEventArgs());
        cut.FindAll("button[role=option]").Should().Contain(o => o.TextContent.Trim() == "NomeCliente");
    }

    [Fact]
    public void Coluna_derivada_fica_protegida_no_Property_Sheet()
    {
        var entity = Entity();
        var derivada = entity.Fields[0] with
        {
            DerivedFromId = Guid.NewGuid(),
            DerivedFromName = "NomeCliente",
        };
        var comDerivada = entity with
        {
            Fields = new[] { derivada, entity.Fields[1] },
        };

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, comDerivada)
            .Add(p => p.Entities, new[] { comDerivada })
            .Add(p => p.FieldId, derivada.Id));

        // seleção restaurada + marcação visual
        cut.Find(".ide-dict__row--active").TextContent.Should().Contain("Nome");
        cut.Find(".ide-dict__props-head").TextContent.Should().Contain("derivada");

        // nome não editável e "Aplicar" ausente: a edição honesta é no modelo
        cut.Find("#ide-dict-name").HasAttribute("disabled").Should().BeTrue();
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Aplicar");
        cut.Find(".ide-dict__hint").TextContent.Should().Contain("NomeCliente");
    }

    [Fact]
    public void Aba_validade_mostra_a_regra_gravada_no_campo()
    {
        var entity = Entity();
        var comRegra = entity.Fields[0] with
        {
            Validation = new FieldValidationDto(
                Guid.NewGuid(), FieldValidationKind.Choices, "A;Ativo\nI;Inativo", "Escolha um status."),
        };
        var entidade = entity with { Fields = new[] { comRegra, entity.Fields[1] } };

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entidade)
            .Add(p => p.Entities, new[] { entidade })
            .Add(p => p.FieldId, comRegra.Id));

        // a linha da coluna e a aba anunciam a regra
        cut.Find(".ide-dict__row--active").TextContent.Should().Contain("regra");
        cut.Find("#ide-dict-tab-validity").TextContent.Should().Contain("regra");

        cut.Find("#ide-dict-tab-validity").Click();

        cut.Find("#ide-dict-validity-kind").GetAttribute("value").Should().Be("Choices");
        cut.Find("#ide-dict-validity-choices").GetAttribute("value").Should().Be("A;Ativo\nI;Inativo");
        cut.Find("#ide-dict-validity-message").GetAttribute("value").Should().Be("Escolha um status.");
    }

    [Fact]
    public async Task Salvar_validade_recusa_faixa_invertida_com_mensagem_acessivel()
    {
        var entity = Entity();
        var campo = StubField(entity.Id, Fields(entity)[0].Id);

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entity)
            .Add(p => p.Entities, new[] { entity }));
        cut.FindAll(".ide-dict__row")[0].Click();
        cut.Find("#ide-dict-tab-validity").Click();

        cut.Find("#ide-dict-validity-kind").Change("Range");
        cut.Find("#ide-dict-validity-min").Input("10");
        cut.Find("#ide-dict-validity-max").Input("1");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Salvar validade").Click();

        var status = cut.Find("#ide-dict-panel-validity [role=status]");
        status.TextContent.Should().Contain("pode ser maior que"); // "O m?nimo n?o pode ser maior que o m?ximo."
    }

    [Fact]
    public async Task Salvar_validade_com_escolhas_validas_confirma_na_regiao_viva()
    {
        var entity = Entity();
        var campo = StubField(entity.Id, Fields(entity)[0].Id);

        var cut = Context.Render<DictionaryWorkspace>(parameters => parameters
            .Add(p => p.Entity, entity)
            .Add(p => p.Entities, new[] { entity }));
        cut.FindAll(".ide-dict__row")[0].Click();
        cut.Find("#ide-dict-tab-validity").Click();

        cut.Find("#ide-dict-validity-kind").Change("Choices");
        cut.Find("#ide-dict-validity-choices").Input("A;Ativo\nI;Inativo");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Salvar validade").Click();

        var status = cut.Find("#ide-dict-panel-validity [role=status]");
        status.TextContent.Should().Contain("validade salva"); // "Verifica??o de validade salva."
        status.GetAttribute("class").Should().NotContain("ide-dict__status--error");
    }

    /// <summary>Repositório sem banco: os caminhos exercitados não persistem.</summary>
    private sealed class StubDictionaryRepository : IDictionaryRepository
    {
        private EntityField? _campo;

        /// <summary>Registra o campo devolvido por GetFieldAsync (id do parâmetro é ignorado).</summary>
        public EntityField SetField(Guid entityId, Guid fieldId, string name)
        {
            _campo = new EntityField
            {
                Id = fieldId,
                EntityId = entityId,
                Name = name,
                ClrType = "string",
                DbType = "TEXT",
                Length = 50,
                Label = name,
                SortOrder = 0,
            };
            return _campo;
        }

        public Task<IReadOnlyList<IdeDictionary>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IdeDictionary>>(Array.Empty<IdeDictionary>());

        public Task<IdeDictionary?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IdeDictionary?>(null);
        public Task<IdeDictionary?> GetFullAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IdeDictionary?>(null);
        public Task<Entity?> GetEntityAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<Entity?>(null);
        public Task<EntityField?> GetFieldAsync(Guid fieldId, CancellationToken ct = default)
            => Task.FromResult(_campo is not null && _campo.Id == fieldId ? _campo : null);
        public Task<EntityKey?> GetKeyAsync(Guid keyId, CancellationToken ct = default) => Task.FromResult<EntityKey?>(null);

        public Task AddAsync(IdeDictionary dictionary, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(IdeDictionary dictionary, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEntityAsync(Entity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateEntityAsync(Entity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteEntityAsync(Guid entityId, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddFieldAsync(EntityField field, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateFieldAsync(EntityField field, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteFieldAsync(Guid fieldId, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddKeyAsync(EntityKey key, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateKeyAsync(EntityKey key, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteKeyAsync(Guid keyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EntityRelation>> ListRelationsAsync(Guid dictionaryId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntityRelation>>(Array.Empty<EntityRelation>());
        public Task AddRelationAsync(EntityRelation relation, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteRelationAsync(Guid relationId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<GlobalField>> ListGlobalFieldsAsync(Guid dictionaryId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GlobalField>>(Array.Empty<GlobalField>());
        public Task<IReadOnlyList<GlobalField>> ListGlobalFieldsByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GlobalField>>(Array.Empty<GlobalField>());
        public Task<GlobalField?> GetGlobalFieldAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<GlobalField?>(null);
        public Task AddGlobalFieldAsync(GlobalField field, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateGlobalFieldAsync(GlobalField field, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteGlobalFieldAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<FieldValidation?> GetFieldValidationAsync(Guid fieldId, CancellationToken ct = default)
            => Task.FromResult<FieldValidation?>(null);
        public Task AddFieldValidationAsync(FieldValidation validation, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateFieldValidationAsync(FieldValidation validation, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteFieldValidationAsync(Guid fieldId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
