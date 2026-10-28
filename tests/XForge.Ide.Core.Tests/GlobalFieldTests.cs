using FluentAssertions;
using XForge.Ide.Application.Dto;
using XForge.Ide.Application.Services;
using Xunit;

namespace XForge.Ide.Core.Tests;

/// <summary>
/// Colunas globais (≈ "Derived From" do DCT): criar modelo, derivar campos,
/// propagar alterações do modelo e desvincular ao excluir.
/// </summary>
public class GlobalFieldTests : ServiceTestBase
{
    private async Task<(Guid dictId, Guid entityId)> CriarDicionarioAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ide-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var project = await Projects.CreateAsync("Projeto Teste", dir, null, null);
        var dict = await Dictionaries.CreateAsync(new CreateDictionaryRequest(project.Value.Id, "Principal"));
        var entity = await Dictionaries.AddEntityAsync(new CreateEntityRequest(
            dict.Value.Id, "Cliente", "Clientes", "Sqlite", null, null));
        return (dict.Value.Id, entity.Value.Id);
    }

    private static CreateGlobalFieldRequest Modelo(Guid dictId, string name = "NomeCliente")
        => new(dictId, name, "string", 50, "@s40", "Nome do cliente", null, true, "Nome completo do cliente");

    [Fact]
    public async Task Criar_modelo_e_listar()
    {
        var (dictId, _) = await CriarDicionarioAsync();

        var criado = await Dictionaries.AddGlobalFieldAsync(Modelo(dictId));
        criado.IsSuccess.Should().BeTrue();

        var lista = await Dictionaries.ListGlobalFieldsAsync(dictId);
        lista.Should().HaveCount(1);
        lista[0].Name.Should().Be("NomeCliente");
        lista[0].Length.Should().Be(50);
    }

    [Fact]
    public async Task Nome_duplicado_de_modelo_e_recusado()
    {
        var (dictId, _) = await CriarDicionarioAsync();
        await Dictionaries.AddGlobalFieldAsync(Modelo(dictId));

        var outro = await Dictionaries.AddGlobalFieldAsync(Modelo(dictId, "nomecliente"));

        outro.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Campo_derivado_nasce_com_as_propriedades_do_modelo()
    {
        var (dictId, entityId) = await CriarDicionarioAsync();
        var modelo = await Dictionaries.AddGlobalFieldAsync(Modelo(dictId));

        var campo = await Dictionaries.AddFieldAsync(new CreateFieldRequest(
            entityId, "qualquer", "string", null, null, null, false, null, null, null, null)
        {
            DerivedFromId = modelo.Value.Id
        });

        campo.IsSuccess.Should().BeTrue();
        campo.Value!.Name.Should().Be("NomeCliente");
        campo.Value.ClrType.Should().Be("string");
        campo.Value.Length.Should().Be(50);
        campo.Value.Required.Should().BeTrue();
        campo.Value.Picture.Should().Be("@s40");
        campo.Value.Label.Should().Be("Nome do cliente");
    }

    [Fact]
    public async Task Atualizar_modelo_propaga_para_os_campos_derivados()
    {
        var (dictId, entityId) = await CriarDicionarioAsync();
        var modelo = await Dictionaries.AddGlobalFieldAsync(Modelo(dictId));
        await Dictionaries.AddFieldAsync(new CreateFieldRequest(
            entityId, "x", "string", null, null, null, false, null, null, null, null)
        {
            DerivedFromId = modelo.Value.Id
        });

        var atualizado = await Dictionaries.UpdateGlobalFieldAsync(new UpdateGlobalFieldRequest(
            modelo.Value.Id, "NomeCompleto", "string", 80, "@s80", "Nome completo", null, false, null));

        atualizado.IsSuccess.Should().BeTrue();
        var entidade = await Dictionaries.GetEntityAsync(entityId);
        var derivado = entidade!.Fields.First(f => f.Name == "NomeCompleto");
        derivado.Length.Should().Be(80);
        derivado.Required.Should().BeFalse();
        derivado.Picture.Should().Be("@s80");
    }

    [Fact]
    public async Task Excluir_modelo_desvincula_o_campo_que_permanece()
    {
        var (dictId, entityId) = await CriarDicionarioAsync();
        var modelo = await Dictionaries.AddGlobalFieldAsync(Modelo(dictId));
        await Dictionaries.AddFieldAsync(new CreateFieldRequest(
            entityId, "x", "string", null, null, null, false, null, null, null, null)
        {
            DerivedFromId = modelo.Value.Id
        });

        await Dictionaries.DeleteGlobalFieldAsync(modelo.Value.Id);

        var lista = await Dictionaries.ListGlobalFieldsAsync(dictId);
        lista.Should().BeEmpty();
        var entidade = await Dictionaries.GetEntityAsync(entityId);
        entidade!.Fields.Should().HaveCount(2); // Id + campo que era derivado
        entidade.Fields.Any(f => f.Name == "NomeCliente").Should().BeTrue();
    }

    [Fact]
    public async Task Propagacao_alcanca_todas_as_tabelas_que_derivaram()
    {
        var (dictId, entityId) = await CriarDicionarioAsync();
        var segunda = await Dictionaries.AddEntityAsync(new CreateEntityRequest(
            dictId, "Pedido", "Pedidos", "Sqlite", null, null));
        var modelo = await Dictionaries.AddGlobalFieldAsync(Modelo(dictId));
        var derivar = new CreateFieldRequest(
            Guid.Empty, "x", "string", null, null, null, false, null, null, null, null)
        {
            DerivedFromId = modelo.Value.Id
        };
        await Dictionaries.AddFieldAsync(derivar with { EntityId = entityId });
        await Dictionaries.AddFieldAsync(derivar with { EntityId = segunda.Value.Id });

        var atualizado = await Dictionaries.UpdateGlobalFieldAsync(new UpdateGlobalFieldRequest(
            modelo.Value.Id, "NomeCompleto", "string", 80, null, null, null, false, null));

        atualizado.IsSuccess.Should().BeTrue();
        foreach (var id in new[] { entityId, segunda.Value.Id })
        {
            var entidade = await Dictionaries.GetEntityAsync(id);
            entidade!.Fields.Should().Contain(f => f.Name == "NomeCompleto" && f.Length == 80);
        }
    }

    [Fact]
    public async Task DTO_do_campo_derivado_traz_o_nome_do_modelo()
    {
        var (dictId, entityId) = await CriarDicionarioAsync();
        var modelo = await Dictionaries.AddGlobalFieldAsync(Modelo(dictId));
        await Dictionaries.AddFieldAsync(new CreateFieldRequest(
            entityId, "x", "string", null, null, null, false, null, null, null, null)
        {
            DerivedFromId = modelo.Value.Id
        });

        var entidade = await Dictionaries.GetEntityAsync(entityId);
        var derivado = entidade!.Fields.First(f => f.DerivedFromId is not null);
        derivado.DerivedFromName.Should().Be("NomeCliente");
        derivado.Name.Should().Be("NomeCliente");
    }

    [Fact]
    public async Task Modelo_de_outro_dicionario_nao_pode_derivar()
    {
        var (dictId, entityId) = await CriarDicionarioAsync();
        var outroDict = await Dictionaries.CreateAsync(new CreateDictionaryRequest(
            (await Projects.CreateAsync("Outro", Path.GetTempPath(), null, null)).Value.Id, "Secundario"));
        var modeloOutro = await Dictionaries.AddGlobalFieldAsync(Modelo(outroDict.Value.Id));

        var campo = await Dictionaries.AddFieldAsync(new CreateFieldRequest(
            entityId, "x", "string", null, null, null, false, null, null, null, null)
        {
            DerivedFromId = modeloOutro.Value.Id
        });

        campo.IsSuccess.Should().BeFalse();
    }
}
