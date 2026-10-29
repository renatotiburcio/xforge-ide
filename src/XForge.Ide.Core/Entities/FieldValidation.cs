namespace XForge.Ide.Core.Entities;

/// <summary>
/// Verificação de validade do campo (≈ "Column Validation" do DCT do Clarion).
/// Um registro por campo, criado sob demanda. Excluir o campo exclui a regra.
/// </summary>
public class FieldValidation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Campo dono da regra (1:1, exclusão em cascata).</summary>
    public Guid FieldId { get; set; }

    public Enums.FieldValidationKind Kind { get; set; } = Enums.FieldValidationKind.None;

    /// <summary>
    /// Parâmetros codificados conforme <see cref="Kind"/> (ver FieldValidationRules):
    /// Choices → uma escolha por linha "valor;rótulo" (rótulo opcional);
    /// Range → "mínimo;máximo" (vazio = sem limite);
    /// MustExistIn → "idDaEntidade;nomeDoCampo".
    /// </summary>
    public string? Data { get; set; }

    /// <summary>Mensagem exibida quando a validação falha (opcional).</summary>
    public string? Message { get; set; }
}
