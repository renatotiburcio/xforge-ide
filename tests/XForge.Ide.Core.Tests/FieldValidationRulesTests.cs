using FluentAssertions;
using XForge.Ide.Application.Services;
using XForge.Ide.Core.Enums;
using Xunit;

namespace XForge.Ide.Core.Tests;

/// <summary>
/// Regras puras das verificações de validade (≈ Column Validation do DCT):
/// codificação dos parâmetros e a verificação aplicada a valores.
/// </summary>
public class FieldValidationRulesTests
{
    // ------------------------------------------------------------ escolhas

    [Fact]
    public void Parse_escolhas_separa_valor_e_rotulo()
    {
        var data = "A;Ativo\nI;Inativo\nC";

        var choices = FieldValidationRules.ParseChoices(data);

        choices.Should().HaveCount(3);
        choices[0].Value.Should().Be("A");
        choices[0].Label.Should().Be("Ativo");
        choices[1].Text.Should().Be("Inativo");
        choices[2].Value.Should().Be("C");
        choices[2].Label.Should().BeNull();
    }

    [Fact]
    public void Escolhas_exigem_duas_linhas_validas_e_sem_duplicidade()
    {
        FieldValidationRules.ValidateChoicesData("A;Ativo\nB").Should().BeNull("duas escolhas válidas");
        FieldValidationRules.ValidateChoicesData("A").Should().NotBeNull();
        FieldValidationRules.ValidateChoicesData("").Should().NotBeNull();
        FieldValidationRules.ValidateChoicesData("A;Ativo\na").Should().NotBeNull("valor repetido ignora caixa");
        FieldValidationRules.ValidateChoicesData(";Ativo\nB").Should().NotBeNull("valor vazio");
    }

    [Fact]
    public void Roundtrip_de_escolhas_preserva_valores_e_rotulos()
    {
        var choices = new[]
        {
            new FieldValidationRules.Choice("A", "Ativo"),
            new FieldValidationRules.Choice("C", null),
        };

        var texto = FieldValidationRules.FormatChoices(choices);
        var volta = FieldValidationRules.ParseChoices(texto);

        volta.Should().Equal(choices);
    }

    // ------------------------------------------------------------ faixa

    [Fact]
    public void Parse_faixa_aceita_limites_abertos()
    {
        var (min, max) = FieldValidationRules.ParseRange("0;100");
        min.Should().Be(0);
        max.Should().Be(100);

        var (soMin, _) = FieldValidationRules.ParseRange("5;");
        soMin.Should().Be(5);

        var (_, soMax) = FieldValidationRules.ParseRange(";9");
        soMax.Should().Be(9);
    }

    [Fact]
    public void Faixa_recurso_recusa_invertida_texto_e_vazia()
    {
        FieldValidationRules.ValidateRangeData("10;1").Should().NotBeNull();
        FieldValidationRules.ValidateRangeData("abc;10").Should().NotBeNull();
        FieldValidationRules.ValidateRangeData(";").Should().NotBeNull();
        FieldValidationRules.ValidateRangeData("1;2;3").Should().NotBeNull();
        FieldValidationRules.ValidateRangeData("0;100").Should().BeNull();
    }

    [Fact]
    public void Roundtrip_de_faixa_preserva_os_limites()
    {
        var texto = FieldValidationRules.FormatRange(1.5m, null);

        var (min, max) = FieldValidationRules.ParseRange(texto);

        min.Should().Be(1.5m);
        max.Should().BeNull();
    }

    // ------------------------------------------------------------ deve existir em

    [Fact]
    public void Parse_deve_existir_em_exige_entidade_e_campo()
    {
        var target = Guid.NewGuid();
        var alvo = FieldValidationRules.ParseMustExistIn(FieldValidationRules.FormatMustExistIn(target, "Id"));

        alvo.Should().NotBeNull();
        alvo!.Value.EntityId.Should().Be(target);
        alvo.Value.FieldName.Should().Be("Id");
        FieldValidationRules.ParseMustExistIn("nao-e-um-guid;Id").Should().BeNull();
        FieldValidationRules.ParseMustExistIn("();sem-campo").Should().BeNull();
        FieldValidationRules.ParseMustExistIn(target + ";").Should().BeNull();
    }

    // ------------------------------------------------------------ verificação de valor

    [Fact]
    public void Valor_fora_da_lista_de_escolhas_e_recusado()
    {
        var data = "A;Ativo\nI;Inativo";

        FieldValidationRules.CheckValue(FieldValidationKind.Choices, data, null, "A").Should().BeNull();
        FieldValidationRules.CheckValue(FieldValidationKind.Choices, data, null, "i")
            .Should().BeNull("comparação ignora caixa");
        FieldValidationRules.CheckValue(FieldValidationKind.Choices, data, null, "X")
            .Should().Contain("não está na lista");
        FieldValidationRules.CheckValue(FieldValidationKind.Choices, data, "Escolha A ou I.", "X")
            .Should().Be("Escolha A ou I.", "mensagem personalizada prevalece");
    }

    [Fact]
    public void Valor_fora_da_faixa_e_recusado()
    {
        var data = FieldValidationRules.FormatRange(0m, 100m);

        FieldValidationRules.CheckValue(FieldValidationKind.Range, data, null, "50").Should().BeNull();
        FieldValidationRules.CheckValue(FieldValidationKind.Range, data, null, "101")
            .Should().Contain("fora da faixa");
        FieldValidationRules.CheckValue(FieldValidationKind.Range, data, null, "-1")
            .Should().Contain("fora da faixa");
        FieldValidationRules.CheckValue(FieldValidationKind.Range, data, null, "texto")
            .Should().Be("Informe um número.");
        FieldValidationRules.CheckValue(FieldValidationKind.Range, data, null, "")
            .Should().Be("Informe um número.", "vazio não é número — obrigatoriedade é do campo");
    }

    [Fact]
    public void Deve_existir_em_e_none_sao_delegados()
    {
        // MustExistIn só pode ser conferida contra o banco: o motor não inventa resposta
        FieldValidationRules.CheckValue(FieldValidationKind.MustExistIn, Guid.NewGuid() + ";Id", null, "qualquer")
            .Should().BeNull();
        FieldValidationRules.CheckValue(FieldValidationKind.None, null, null, "qualquer")
            .Should().BeNull();
    }
}
