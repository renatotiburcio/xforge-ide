namespace XForge.Ide.App.Services;

/// <summary>
/// Paginação da árvore de arquivos do Explorer.
/// <para>
/// Um diretório pode conter milhares de entradas (builds, assets, vendor). Sem
/// limite, um único diretório expandido renderiza todos os nós de uma vez e
/// trava o circuito Blazor. A árvore renderiza no máximo <see cref="PageSize"/>
/// itens por diretório e oferece "Mostrar mais" para avançar.
/// </para>
/// <para>
/// Lógica pura (sem DOM) para ser testável em bUnit/xUnit sem renderizar a página.
/// </para>
/// </summary>
public static class TreePaging
{
    /// <summary>Quantidade de itens renderizados por página, por diretório.</summary>
    public const int PageSize = 300;

    /// <summary>Limite inicial de um diretório recém-carregado.</summary>
    public static int InitialLimit => PageSize;

    /// <summary>Quantos itens devem ser renderizados agora (nunca mais que o total).</summary>
    public static int VisibleCount(int total, int limit) =>
        total <= 0 ? 0 : Math.Min(total, Math.Max(0, limit));

    /// <summary>True quando ainda existem itens além do limite atual.</summary>
    public static bool HasMore(int total, int limit) => VisibleCount(total, limit) < total;

    /// <summary>Novo limite após acionar "Mostrar mais" (uma página adiante).</summary>
    public static int NextLimit(int limit) => Math.Max(0, limit) + PageSize;

    /// <summary>Rótulo do botão, ex.: "Mostrar mais (128)".</summary>
    public static string RemainingLabel(int total, int limit)
    {
        var remaining = total - VisibleCount(total, limit);
        return remaining <= 0 ? string.Empty : $"Mostrar mais ({remaining})";
    }
}
