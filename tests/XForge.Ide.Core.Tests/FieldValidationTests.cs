using FluentAssertions;
using XForge.Ide.Application.Dto;
using XForge.Ide.Application.Services;
using XForge.Ide.Core.Enums;
using Xunit;

namespace XForge.Ide.Core.Tests;

/// <summary>
/// Verificações de validade (≈ "Column Validation" do DCT): gravar a regra no
/// dicionário, recusar parâmetros inválidos e conferir o alvo do "Deve existir em".
/// </summary>
public class FieldValidationTests : ServiceTestBase
{
    private async Task<(Guid dictId, Guid entityId, Guid segundaId)> CriarDicionarioAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ide-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var project = await Projects.CreateAsync("Projeto Teste", dir, null, null);
        var dict = await Dictionaries.CreateAsync(new CreateDictionaryRequest(project.Value.Id, "Principal"));
        var entity = await Dictionaries.AddEntityAsync(new CreateEntityRequest(
            dict.Value.Id, "Cliente", "Clientes", "Sqlite", null, null));
        var segunda = await Dictionaries.AddEntityAsync(new CreateEntityRequest(
            dict.Value.Id, "Cidade", "Cidades", "Sqlite", null, null));
        return (dict.Value.Id, entity.Value.Id, segunda.Value.Id);
    }

    private async Task<Guid> CriarCampoAsync(Guid entityId, string name = "Status", string clrType = "string")
    {
        var campo = await Dictionaries.AddFieldAsync(new CreateFieldRequest(
            entityId, name, clrType, null, null, null, false, null, null, null, null));
        return campo.Value!.Id;
    }

    [Fact]
    public async Task Gravar_e_reler_uma_lista_de_escolhas()
    {
        var (_, entityId, _) = await CriarDicionarioAsync();
        var campoId = await CriarCampoAsync(entityId);
        var data = "A;Ativo\nI;Inativo\nC;Cancelado";

        var salvo = await Dictionaries.SaveFieldValidationAsync(
            new(campoId, FieldValidationKind.Choices, data, "Escolha um status."));

        salvo.IsSuccess.Should().BeTrue();
        salvo.Value!.Message.Should().Be("Escolha um status.");

        var entidade = await Dictionaries.GetEntityAsync(entityId);
        var campo = entidade!.Fields.First(f => f.Id == campoId);
        campo.Validation!.Kind.Should().Be(FieldValidationKind.Choices);
        FieldValidationRules.ParseChoices(campo.Validation!.Data).Should().HaveCount(3);
    }

    [Fact]
    public async Task Salvar_novamente_atualiza_a_regra_existente()
    {
        var (_, entityId, _) = await CriarDicionarioAsync();
        var campoId = await CriarCampoAsync(entityId);
        await Dictionaries.SaveFieldValidationAsync(new(campoId, FieldValidationKind.Choices, "A;Ativo\nI;Inativo", null));

        var salvo = await Dictionaries.SaveFieldValidationAsync(
            new(campoId, FieldValidationKind.Range, "0;10", "Fora da faixa."));

        salvo.IsSuccess.Should().BeTrue();
        var entidade = await Dictionaries.GetEntityAsync(entityId);
        entidade!.Fields.First(f => f.Id == campoId).Validation!.Kind
            .Should().Be(FieldValidationKind.Range);
    }

    [Fact]
    public async Task Nenhuma_remove_a_regra()
    {
        var (_, entityId, _) = await CriarDicionarioAsync();
        var campoId = await CriarCampoAsync(entityId);
        await Dictionaries.SaveFieldValidationAsync(new(campoId, FieldValidationKind.Choices, "A;Ativo\nI;Inativo", null));

        var removido = await Dictionaries.SaveFieldValidationAsync(
            new(campoId, FieldValidationKind.None, null, null));

        removido.IsSuccess.Should().BeTrue();
        removido.Value!.IsEmpty.Should().BeTrue();

        var entidade = await Dictionaries.GetEntityAsync(entityId);
        var campo = entidade!.Fields.First(f => f.Id == campoId);
        (campo.Validation is null || campo.Validation.IsEmpty).Should().BeTrue();
    }

    [Fact]
    public async Task Dados_invalidos_sao_recusados_sem_gravar()
    {
        var (_, entityId, _) = await CriarDicionarioAsync();
        var campoId = await CriarCampoAsync(entityId);

        var escolhaRuim = await Dictionaries.SaveFieldValidationAsync(
            new(campoId, FieldValidationKind.Choices, "A", null));
        escolhaRuim.IsSuccess.Should().BeFalse();

        var rangeRuim = await Dictionaries.SaveFieldValidationAsync(
            new(campoId, FieldValidationKind.Range, "10;1", null));
        rangeRuim.IsSuccess.Should().BeFalse();

        var entidade = await Dictionaries.GetEntityAsync(entityId);
        entidade!.Fields.First(f => f.Id == campoId).Validation.Should().BeNull();
    }

    [Fact]
    public async Task Deve_existir_em_aceita_campo_de_outra_entidade_do_mesmo_dicionario()
    {
        var (_, entityId, segundaId) = await CriarDicionarioAsync();
        var campoId = await CriarCampoAsync(entityId, "CidadeId");

        var salvo = await Dictionaries.SaveFieldValidationAsync(new(
            campoId, FieldValidationKind.MustExistIn,
            FieldValidationRules.FormatMustExistIn(segundaId, "Id"), null));

        salvo.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Deve_existir_em_recusa_a_propria_entidade_e_campos_inexistentes()
    {
        var (_, entityId, segundaId) = await CriarDicionarioAsync();
        var campoId = await CriarCampoAsync(entityId, "CidadeId");

        var propria = await Dictionaries.SaveFieldValidationAsync(new(
            campoId, FieldValidationKind.MustExistIn,
            FieldValidationRules.FormatMustExistIn(entityId, "Id"), null));
        propria.IsSuccess.Should().BeFalse();

        var campoSemNome = await Dictionaries.SaveFieldValidationAsync(new(
            campoId, FieldValidationKind.MustExistIn,
            FieldValidationRules.FormatMustExistIn(segundaId, "NaoExiste"), null));
        campoSemNome.IsSuccess.Should().BeFalse();

        var entidadeFantasma = Guid.NewGuid();
        var outra = await Dictionaries.SaveFieldValidationAsync(new(
            campoId, FieldValidationKind.MustExistIn,
            FieldValidationRules.FormatMustExistIn(entidadeFantasma, "Id"), null));
        outra.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Excluir_o_campo_remove_a_regra_junto()
    {
        var (_, entityId, _) = await CriarDicionarioAsync();
        var campoId = await CriarCampoAsync(entityId);
        await Dictionaries.SaveFieldValidationAsync(new(campoId, FieldValidationKind.Choices, "A;Ativo\nI;Inativo", null));

        await Dictionaries.DeleteFieldAsync(campoId);

        // a regra ficou órfã? não: a entidade recarregada não tem o campo nem a regra
        var entidade = await Dictionaries.GetEntityAsync(entityId);
        entidade!.Fields.Any(f => f.Id == campoId).Should().BeFalse();
    }
}
