using System.Globalization;
using XForge.Ide.Core.Enums;

namespace XForge.Ide.Application.Services;

/// <summary>
/// Regras puras das verificações de validade (≈ "Column Validation" do DCT):
/// codificação/decodificação dos parâmetros e a verificação propriamente dita.
/// Sem banco e sem DOM — as telas geradas e o dicionário usam o mesmo motor.
/// </summary>
public static class FieldValidationRules
{
    /// <summary>Separa campos dentro de uma linha/between limites ("valor;rótulo", "min;max", "entidade;campo").</summary>
    public const char Separator = ';';

    /// <summary>Uma escolha da lista (valor gravado + rótulo exibido).</summary>
    public record Choice(string Value, string? Label)
    {
        public string Text => string.IsNullOrWhiteSpace(Label) ? Value : Label;
    }

    // ------------------------------------------------------------ lista de escolhas

    /// <summary>Codifica as escolhas no formato do dicionário (uma por linha "valor;rótulo").</summary>
    public static string FormatChoices(IEnumerable<Choice> choices)
        => string.Join('\n', choices.Select(c => string.IsNullOrWhiteSpace(c.Label)
            ? c.Value
            : c.Value + Separator + c.Label));

    /// <summary>Decodifica o texto do dicionário em escolhas (linhas vazias são ignoradas).</summary>
    public static IReadOnlyList<Choice> ParseChoices(string? data)
    {
        var list = new List<Choice>();
        foreach (var raw in (data ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }
            var idx = line.IndexOf(Separator);
            if (idx < 0)
            {
                list.Add(new Choice(line, null));
            }
            else
            {
                var value = line[..idx].Trim();
                var label = line[(idx + 1)..].Trim();
                list.Add(new Choice(value, label.Length == 0 ? null : label));
            }
        }
        return list;
    }

    public static string? ValidateChoicesData(string? data)
    {
        var choices = ParseChoices(data);
        if (choices.Count < 2)
        {
            return "Informe pelo menos duas escolhas (uma por linha, formato \"valor;rótulo\").";
        }
        foreach (var c in choices)
        {
            if (c.Value.Length == 0)
            {
                return "Toda escolha precisa de um valor (formato \"valor;rótulo\").";
            }
        }
        var duplicada = choices
            .GroupBy(c => c.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicada is not null)
        {
            return $"Valor repetido nas escolhas: '{duplicada.Key}'.";
        }
        return null;
    }

    // ------------------------------------------------------------ faixa de valores

    /// <summary>Codifica mín/máx ("min;max"; vazio = sem limite).</summary>
    public static string FormatRange(decimal? min, decimal? max)
        => (min?.ToString(CultureInfo.InvariantCulture) ?? string.Empty) + Separator +
           (max?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    public static (decimal? Min, decimal? Max) ParseRange(string? data)
    {
        var parts = (data ?? string.Empty).Split(Separator);
        var min = parts.Length > 0 && decimal.TryParse(parts[0].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var lo) ? lo : (decimal?)null;
        var max = parts.Length > 1 && decimal.TryParse(parts[1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var hi) ? hi : (decimal?)null;
        return (min, max);
    }

    public static string? ValidateRangeData(string? data)
    {
        var parts = (data ?? string.Empty).Split(Separator);
        if (parts.Length > 2)
        {
            return "Faixa deve ter no máximo dois números (mínimo;máximo).";
        }
        foreach (var part in parts)
        {
            var text = part.Trim();
            if (text.Length == 0)
            {
                continue;
            }
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                return $"'{text}' não é um número válido para a faixa.";
            }
        }
        var (min, max) = ParseRange(data);
        if (min is not null && max is not null && min > max)
        {
            return "O mínimo não pode ser maior que o máximo.";
        }
        if (min is null && max is null)
        {
            return "Informe pelo menos um dos limites (mínimo ou máximo).";
        }
        return null;
    }

    // ------------------------------------------------------------ deve existir em

    /// <summary>Codifica o alvo ("idDaEntidade;nomeDoCampo").</summary>
    public static string FormatMustExistIn(Guid entityId, string fieldName) => entityId + Separator.ToString() + fieldName;

    public static (Guid EntityId, string FieldName)? ParseMustExistIn(string? data)
    {
        var parts = (data ?? string.Empty).Split(Separator);
        if (parts.Length != 2 || !Guid.TryParse(parts[0].Trim(), out var entityId))
        {
            return null;
        }
        var field = parts[1].Trim();
        if (field.Length == 0)
        {
            return null;
        }
        return (entityId, field);
    }

    // ------------------------------------------------------------ verificação de valor

    /// <summary>
    /// Aplica a regra a um valor: devolve a mensagem de violação (personalizada
    /// quando existir) ou <c>null</c> quando o valor passa. <see cref="FieldValidationKind.MustExistIn"/>
    /// só é verificável contra o banco — a regra fica registrada no dicionário e o
    /// app gerado consulta a existência.
    /// </summary>
    public static string? CheckValue(
        FieldValidationKind kind, string? data, string? message, string? value)
    {
        switch (kind)
        {
            case FieldValidationKind.Choices:
                var choices = ParseChoices(data);
                if (choices.Count == 0)
                {
                    return null;
                }
                var texto = (value ?? string.Empty).Trim();
                return choices.Any(c => string.Equals(c.Value, texto, StringComparison.OrdinalIgnoreCase))
                    ? null
                    : message ?? $"Valor não está na lista de escolhas ({string.Join(", ", choices.Select(c => c.Value))}).";

            case FieldValidationKind.Range:
                var (min, max) = ParseRange(data);
                if (min is null && max is null)
                {
                    return null;
                }
                if (!decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var numero))
                {
                    return message ?? "Informe um número.";
                }
                var falhaMin = min is not null && numero < min;
                var falhaMax = max is not null && numero > max;
                return falhaMin || falhaMax
                    ? message ?? $"Valor fora da faixa permitida ({FriendlyRange(min, max)})."
                    : null;

            default:
                return null;
        }
    }

    private static string FriendlyRange(decimal? min, decimal? max) => (min, max) switch
    {
        (not null, not null) => $"{min} a {max}",
        (not null, null) => $"a partir de {min}",
        (null, not null) => $"até {max}",
        _ => "sem limite",
    };
}
