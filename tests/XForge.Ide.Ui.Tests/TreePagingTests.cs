using FluentAssertions;
using XForge.Ide.App.Services;
using Xunit;

namespace XForge.Ide.Ui.Tests;

/// <summary>
/// Paginação da árvore do Explorer: um diretório com milhares de entradas
/// não pode renderizar tudo de uma vez (travaria o circuito Blazor).
/// </summary>
public class TreePagingTests
{
    [Fact]
    public void Small_directory_renders_everything_without_paging()
    {
        const int total = 42;

        TreePaging.HasMore(total, TreePaging.InitialLimit).Should().BeFalse();
        TreePaging.VisibleCount(total, TreePaging.InitialLimit).Should().Be(total);
        TreePaging.RemainingLabel(total, TreePaging.InitialLimit).Should().BeEmpty();
    }

    [Fact]
    public void Exactly_one_page_does_not_show_more()
    {
        const int total = TreePaging.PageSize;

        TreePaging.HasMore(total, TreePaging.InitialLimit).Should().BeFalse();
        TreePaging.RemainingLabel(total, TreePaging.InitialLimit).Should().BeEmpty();
    }

    [Fact]
    public void Large_directory_shows_first_page_and_remaining_count()
    {
        const int total = TreePaging.PageSize + 128;

        TreePaging.HasMore(total, TreePaging.InitialLimit).Should().BeTrue();
        TreePaging.VisibleCount(total, TreePaging.InitialLimit).Should().Be(TreePaging.PageSize);
        TreePaging.RemainingLabel(total, TreePaging.InitialLimit).Should().Be("Mostrar mais (128)");
    }

    [Fact]
    public void Show_more_advances_exactly_one_page_and_terminates()
    {
        const int total = TreePaging.PageSize + 1; // uma entrada além da primeira página

        var limit = TreePaging.InitialLimit;
        limit = TreePaging.NextLimit(limit);
        TreePaging.VisibleCount(total, limit).Should().Be(TreePaging.PageSize + 1);
        TreePaging.HasMore(total, limit).Should().BeFalse();
        TreePaging.RemainingLabel(total, limit).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Degenerate_totals_render_nothing(int total)
    {
        TreePaging.VisibleCount(total, TreePaging.InitialLimit).Should().Be(0);
        TreePaging.HasMore(total, TreePaging.InitialLimit).Should().BeFalse();
    }

    [Fact]
    public void Limit_never_exceeds_total()
    {
        var visible = TreePaging.VisibleCount(10, 1_000_000);
        visible.Should().Be(10);
    }
}
