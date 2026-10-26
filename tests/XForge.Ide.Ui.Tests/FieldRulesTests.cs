using FluentAssertions;
using XForge.Ide.App.Services;
using Xunit;

namespace XForge.Ide.Ui.Tests;

/// <summary>
/// Regras do dicionário — equivalentes às verificações imediatas do
/// Dictionary Editor do Clarion. Lógica pura, sem DOM.
/// </summary>
public class FieldRulesTests
{
    // ------------------------------------------------------------------ nome

    [Fact]
    public void Name_is_required()
    {
        FieldRules.ValidateName("  ", Array.Empty<string>()).Should().Be("Informe o nome do campo.");
    }

    [Theory]
    [InlineData("1Codigo")]
    [InlineData("-Campo")]
    [InlineData("Nome Campo")]
    [InlineData("Nome-Compuesto")]
    public void Name_must_be_a_valid_identifier(string name)
    {
        FieldRules.ValidateName(name, Array.Empty<string>()).Should().NotBeNull();
    }

    [Fact]
    public void Name_with_accent_is_rejected_but_normalize_fixes_it()
    {
        FieldRules.ValidateName("Descrição", Array.Empty<string>()).Should().NotBeNull();
        FieldRules.NormalizeName("nome do cliente").Should().Be("NomeDoCliente");
        FieldRules.NormalizeName("Descrição").Should().Be("Descricao");
        FieldRules.NormalizeName("2campos").Should().Be("Campo2campos");
        FieldRules.NormalizeName("   ").Should().StartWith("Campo");
    }

    [Fact]
    public void Duplicate_name_is_rejected_except_itself()
    {
        var siblings = new[] { "Id", "Nome", "Email" };

        FieldRules.ValidateName("nome", siblings).Should().Be("Já existe um campo chamado 'nome' nesta entidade.");
        FieldRules.ValidateName("Nome", siblings, currentName: "Nome").Should().BeNull();
        FieldRules.ValidateName("Novo", siblings).Should().BeNull();
    }

    // --------------------------------------------------------------- tamanho

    [Fact]
    public void Length_only_applies_to_string_and_bytes()
    {
        FieldRules.ValidateLength("abc", "string").Should().Be("Tamanho deve ser um número inteiro.");
        FieldRules.ValidateLength("0", "string").Should().Be("Tamanho deve ficar entre 1 e 4000.");
        FieldRules.ValidateLength("5000", "string").Should().Be("Tamanho deve ficar entre 1 e 4000.");
        FieldRules.ValidateLength("50", "string").Should().BeNull();
        FieldRules.ValidateLength("abc", "int").Should().BeNull(); // tipo sem tamanho
    }

    [Fact]
    public void Empty_length_for_string_is_reported()
    {
        FieldRules.ValidateLength("", "string").Should().Be("Informe o tamanho (1 a 4000).");
    }

    // -------------------------------------------------------------- precisão

    [Fact]
    public void Precision_only_for_decimals_and_bounded()
    {
        FieldRules.ValidatePrecision("2", "decimal").Should().BeNull();
        FieldRules.ValidatePrecision("", "decimal").Should().BeNull(); // opcional
        FieldRules.ValidatePrecision("19", "decimal").Should().Be("Precisão deve ficar entre 0 e 18.");
        FieldRules.ValidatePrecision("x", "decimal").Should().Be("Precisão deve ser um número inteiro.");
        FieldRules.ValidatePrecision("x", "string").Should().BeNull();
    }

    // ---------------------------------------------------------------- picture

    [Fact]
    public void Picture_follows_clarion_tokens()
    {
        FieldRules.ValidatePicture(null).Should().BeNull();
        FieldRules.ValidatePicture("@n07").Should().BeNull();
        FieldRules.ValidatePicture("@s40").Should().BeNull();
        FieldRules.ValidatePicture("@p(###) ####").Should().BeNull();
        FieldRules.ValidatePicture("n07").Should().Be("Picture Clarion começa com '@' (ex.: @n07, @s40, @p(###) ####).");
        FieldRules.ValidatePicture("@").Should().Be("Picture incompleto: informe o formato após '@'.");
        FieldRules.ValidatePicture("@x999").Should().Be("Picture deve começar com @n, @s, @k, @p, @d, @t ou @b.");
    }

    // -------------------------------------------------------------- utilidades

    [Fact]
    public void Db_type_is_suggested_from_clr_type()
    {
        FieldRules.SuggestDbType("string").Should().Be("TEXT");
        FieldRules.SuggestDbType("int").Should().Be("INTEGER");
        FieldRules.SuggestDbType("bool").Should().Be("INTEGER");
        FieldRules.SuggestDbType("DateTime").Should().Be("TEXT");
        FieldRules.SuggestDbType("byte[]").Should().Be("BLOB");
        FieldRules.SuggestDbType("decimal").Should().Be("NUMERIC");
    }

    [Fact]
    public void System_field_id_is_protected()
    {
        FieldRules.IsSystemField("Id").Should().BeTrue();
        FieldRules.IsSystemField("id").Should().BeTrue();
        FieldRules.IsSystemField("Nome").Should().BeFalse();
    }

    [Fact]
    public void Optional_int_parses_or_nulls()
    {
        FieldRules.ParseOptionalInt("42").Should().Be(42);
        FieldRules.ParseOptionalInt("").Should().BeNull();
        FieldRules.ParseOptionalInt(null).Should().BeNull();
        FieldRules.ParseOptionalInt("abc").Should().BeNull();
    }
}
