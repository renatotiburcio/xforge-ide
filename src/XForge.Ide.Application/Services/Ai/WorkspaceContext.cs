using XForge.Ide.Application.Services;

namespace XForge.Ide.Application.Services.Ai;

/// <summary>
/// Contexto do workspace enviado ao modelo: projeto, arquivo ativo, seleção e
/// um mapa enxuto da árvore de arquivos. Mantém o prompt informativo sem
/// estourar o limite de tokens.
/// </summary>
public sealed class WorkspaceContext
{
    public string? ProjectName { get; init; }
    public string? ProjectPath { get; init; }
    public string? ActiveFilePath { get; init; }
    public string? ActiveFileLanguage { get; init; }
    public string? ActiveFileContent { get; init; }
    public string? SelectedText { get; init; }
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();

    /// <summary>Instruções de sistema com a persona do assistente da IDE.</summary>
    public static string SystemPrompt { get; } = """
        Você é o assistente de engenharia da XForge IDE, especialista em .NET 10, C#,
        Blazor, Clean Architecture, DDD, CQRS, EF Core e Identity.

        Regras:
        - Responda em português do Brasil, direto e técnico.
        - Quando pedirem código, entregue o código COMPLETO do arquivo ou trecho alterado,
          em blocos ```linguagem, sem rodeios e sem explicações óbvias.
        - Respeite os padrões do projeto: componentes XF* do design system (nunca HTML cru),
          async/await, injeção de dependência, tratamento de erro com exceções de domínio.
        - Se não houver contexto suficiente, faça perguntas curtas antes de assumir.
        - Nunca invente APIs; se não souber, diga o que precisa verificar.
        """;

    /// <summary>Resumo legível do contexto anexado à primeira mensagem do usuário.</summary>
    public string Render()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Contexto do workspace");
        if (ProjectName is not null)
        {
            sb.AppendLine($"- Projeto: {ProjectName} ({ProjectPath})");
        }
        if (ActiveFilePath is not null)
        {
            sb.AppendLine($"- Arquivo ativo: {ActiveFilePath} [{ActiveFileLanguage}]");
        }
        if (Files.Count > 0)
        {
            sb.AppendLine("- Arquivos do projeto:");
            foreach (var f in Files.Take(60))
            {
                sb.AppendLine($"  - {f}");
            }
            if (Files.Count > 60)
            {
                sb.AppendLine($"  - ... (+{Files.Count - 60} arquivos)");
            }
        }
        if (ActiveFileContent is not null)
        {
            sb.AppendLine();
            sb.AppendLine("## Conteúdo do arquivo ativo");
            sb.AppendLine("```" + (ActiveFileLanguage ?? string.Empty));
            sb.AppendLine(Truncate(ActiveFileContent, 24000));
            sb.AppendLine("```");
        }
        if (!string.IsNullOrWhiteSpace(SelectedText))
        {
            sb.AppendLine();
            sb.AppendLine("## Trecho selecionado pelo usuário");
            sb.AppendLine("```" + (ActiveFileLanguage ?? string.Empty));
            sb.AppendLine(Truncate(SelectedText, 8000));
            sb.AppendLine("```");
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "\n... (truncado)";
}

/// <summary>Monta o <see cref="WorkspaceContext"/> a partir do projeto e do editor.</summary>
public sealed class WorkspaceContextBuilder
{
    private readonly FileSystemService _files;

    public WorkspaceContextBuilder(FileSystemService files)
    {
        _files = files;
    }

    public async Task<WorkspaceContext> BuildAsync(
        string? projectName,
        string? projectPath,
        string? activeFilePath,
        string? activeFileContent,
        string? selectedText,
        CancellationToken ct = default)
    {
        var files = new List<string>();
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            try
            {
                var entries = await _files.ListAsync(projectPath);
                foreach (var e in entries.Where(e => !e.Name.StartsWith(".") && e.Name is not ("bin" or "obj")))
                {
                    files.Add(e.Name + (e.IsDirectory ? "/" : string.Empty));
                }
            }
            catch
            {
                // projeto inacessível — segue sem o índice
            }
        }

        return new WorkspaceContext
        {
            ProjectName = projectName,
            ProjectPath = projectPath,
            ActiveFilePath = activeFilePath,
            ActiveFileLanguage = Path.GetExtension(activeFilePath ?? string.Empty).TrimStart('.'),
            ActiveFileContent = activeFileContent,
            SelectedText = selectedText,
            Files = files,
        };
    }
}
