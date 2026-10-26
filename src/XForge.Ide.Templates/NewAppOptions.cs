using XForge.Ide.Core.Enums;

namespace XForge.Ide.Templates;

/// <summary>Op??es do wizard "Novo Aplicativo".</summary>
public sealed class NewAppOptions
{
    /// <summary>Nome de exibi??o (ex.: "Meu CRM").</summary>
    public required string DisplayName { get; init; }

    /// <summary>Namespace raiz (ex.: "MeuCrm") - derivado do nome se n?o informado.</summary>
    public required string RootNamespace { get; init; }

    /// <summary>Diret?rio de sa?da (a solution ? criada dentro dele).</summary>
    public required string OutputPath { get; init; }

    public ProviderKind Provider { get; init; } = ProviderKind.Sqlite;

    public string ConnectionString { get; init; } = string.Empty;

    public bool UseIdentity { get; init; } = true;

    public int ApiPort { get; init; } = 5080;

    public int ClientPort { get; init; } = 5173;

    public bool ValidateBuild { get; init; } = true;

    /// <summary>Descri??o para README e registro do projeto.</summary>
    public string? Description { get; init; }
}

/// <summary>Arquivo gerado (relativo ? raiz da solution).</summary>
public sealed record GeneratedFile(string RelativePath, string Content);
