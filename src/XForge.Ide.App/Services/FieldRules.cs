using System.Globalization;
using System.Text;

namespace XForge.Ide.App.Services;

/// <summary>
/// Regras puras do dicionário de dados — equivalentes às verificações imediatas
/// do Dictionary Editor do Clarion ("Column used twice in one key", "Dimension
/// cannot be zero", ...). Sem DOM, testável em xUnit sem renderizar a página.
/// </summary>
public static class FieldRules
{
    /// <summary>Tipos CLR oferecidos (ordem = popularidade).</summary>
    public static readonly string[] ClrTypes =
        { "string", "int", "long", "decimal", "double", "DateTime", "bool", "Guid", "byte[]" };

    /// <summary>Tamanho padrão de um campo string quando o usuário não informa.</summary>
    public const int DefaultStringLength = 100;

    /// <summary>Limite de caracteres de um campo string (equivalente ao "too large" do Clarion).</summary>
    public const int MaxStringLength = 4000;

    /// <summary>
    /// Valida o nome do campo: obrigatório, identificador C# válido, único na
    /// entidade (exceto o próprio nome quando editando) e protegido para "Id".
    /// </summary>
    public static string? ValidateName(string? name, IEnumerable<string> siblings, string? currentName = null)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "Informe o nome do campo.";
        }
        if (!IsAsciiLetterOrUnderscore(value[0]))
        {
            return "O nome deve começar com letra ou '_'.";
        }
        foreach (var c in value)
        {
            if (!IsAsciiIdentifierChar(c))
            {
                // acento contado: use "Nome sem acento" — .NET aceitaria 'ç' num
                // identificador C#, mas colunas SQL com acento dão problema
                return "Use apenas letras sem acento, dígitos e '_'.";
            }
        }
        var isSelf = string.Equals(value, currentName, StringComparison.OrdinalIgnoreCase);
        if (!isSelf && siblings.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Já existe um campo chamado '{value}' nesta entidade.";
        }
        return null;
    }

    /// <summary>O Id da entidade é gerado automaticamente e não pode ser renomeado/removido.</summary>
    public static bool IsSystemField(string? name) =>
        string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase);

    private static bool IsAsciiLetterOrUnderscore(char c) =>
        c is '_' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsAsciiIdentifierChar(char c) =>
        IsAsciiLetterOrUnderscore(c) || char.IsAsciiDigit(c);

    /// <summary>
    /// Valida o tamanho: só faz sentido para string/byte[]; numérico entre 1 e
    /// <see cref="MaxStringLength"/>. Outros tipos ignoram (tamanho implícito).
    /// </summary>
    public static string? ValidateLength(string? length, string clrType)
    {
        if (!TypeAcceptsLength(clrType))
        {
            return null;
        }
        var text = (length ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return $"Informe o tamanho (1 a {MaxStringLength}).";
        }
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return "Tamanho deve ser um número inteiro.";
        }
        if (value < 1 || value > MaxStringLength)
        {
            return $"Tamanho deve ficar entre 1 e {MaxStringLength}.";
        }
        return null;
    }

    /// <summary>Valida precisão (casas decimais): 0 a 18, só para decimal/double.</summary>
    public static string? ValidatePrecision(string? precision, string clrType)
    {
        if (clrType is not ("decimal" or "double"))
        {
            return null;
        }
        var text = (precision ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null; // opcional: default do provider
        }
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return "Precisão deve ser um número inteiro.";
        }
        if (value is < 0 or > 18)
        {
            return "Precisão deve ficar entre 0 e 18.";
        }
        return null;
    }

    /// <summary>
    /// Valida um picture no estilo Clarion (@n07, @s2, @p(999)999-9999 ...).
    /// Aceita vazio (opcional) e a forma básica @ + token conhecido.
    /// </summary>
    public static string? ValidatePicture(string? picture)
    {
        var text = (picture ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }
        if (!text.StartsWith('@'))
        {
            return "Picture Clarion começa com '@' (ex.: @n07, @s40, @p(###) ####).";
        }
        var body = text[1..];
        if (body.Length == 0)
        {
            return "Picture incompleto: informe o formato após '@'.";
        }
        var token = char.ToLowerInvariant(body[0]);
        if (token is not ('n' or 's' or 'k' or 'p' or 'd' or 't' or 'b'))
        {
            return "Picture deve começar com @n, @s, @k, @p, @d, @t ou @b.";
        }
        return null;
    }

    /// <summary>Converte texto opcional em inteiro nulo (vazio ⇒ null).</summary>
    public static int? ParseOptionalInt(string? text) =>
        int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    /// <summary>Sugere o tipo físico (DbType) a partir do tipo CLR.</summary>
    public static string SuggestDbType(string clrType) => clrType switch
    {
        "string" => "TEXT",
        "int" => "INTEGER",
        "long" => "INTEGER",
        "decimal" => "NUMERIC",
        "double" => "REAL",
        "DateTime" => "TEXT",
        "bool" => "INTEGER",
        "Guid" => "TEXT",
        "byte[]" => "BLOB",
        _ => "TEXT",
    };

    /// <summary>Tipos que aceitam tamanho explícito.</summary>
    public static bool TypeAcceptsLength(string clrType) => clrType is "string" or "byte[]";

    /// <summary>Tipos que aceitam precisão decimal.</summary>
    public static bool TypeAcceptsPrecision(string clrType) => clrType is "decimal" or "double";

    /// <summary>
    /// Normaliza um nome digitado livremente para identificador C# (PascalCase,
    /// sem acentos/espaços) — equivalente ao "Name Fix" do Clarion.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        var text = RemoveDiacritics((name ?? string.Empty).Trim());
        var sb = new StringBuilder();
        var upper = true;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true; // separador: próxima letra maiúscula
            }
        }
        var result = sb.ToString();
        if (result.Length == 0 || char.IsDigit(result[0]))
        {
            return "Campo" + result;
        }
        return result;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
