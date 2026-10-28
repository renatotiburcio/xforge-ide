namespace XForge.Ide.Core.Entities;

/// <summary>
/// Coluna global (≈ "Derived From" do DCT do Clarion): definida UMA vez no
/// dicionário e derivada em N entidades. Alterar o modelo propaga para todos
/// os campos derivados — consistência e velocidade de modelagem.
/// </summary>
public class GlobalField
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Dicionário dono do modelo (as derivações são dentro dele).</summary>
    public Guid DictionaryId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string ClrType { get; set; } = "string";

    /// <summary>Tamanho para tipos que aceitam (string/bytes).</summary>
    public int? Length { get; set; }

    public string? Picture { get; set; }
    public string? Label { get; set; }
    public string? Default { get; set; }
    public bool Required { get; set; }

    /// <summary>Documentação do modelo (aparece como dica nas telas geradas).</summary>
    public string? Description { get; set; }
}
