using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using XForge.Ide.App.Components;
using XForge.Ide.Application.Dto;
using XForge.Ide.Application.Services;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using Xunit;

namespace XForge.Ide.Ui.Tests;

/// <summary>
/// Workspace do dicionário com seleção dirigida pela URL (?field= e ?tab=):
/// o F5 devolve a coluna selecionada e a aba do Property Sheet.
/// </summary>
public class DictionaryWorkspaceTests : BunitTestBase
{
    public DictionaryWorkspaceTests()
    {
        // os testes exercitam só render/clique — nada de banco
        Context.Services.AddSingleton(new DictionaryService(new StubDictionaryRepository()));
    }

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

    /// <summary>Repositório sem banco: os caminhos exercitados não persistem.</summary>
    private sealed class StubDictionaryRepository : IDictionaryRepository
    {
        public Task<IReadOnlyList<IdeDictionary>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IdeDictionary>>(Array.Empty<IdeDictionary>());

        public Task<IdeDictionary?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IdeDictionary?>(null);
        public Task<IdeDictionary?> GetFullAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IdeDictionary?>(null);
        public Task<Entity?> GetEntityAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<Entity?>(null);
        public Task<EntityField?> GetFieldAsync(Guid fieldId, CancellationToken ct = default) => Task.FromResult<EntityField?>(null);
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
    }
}
