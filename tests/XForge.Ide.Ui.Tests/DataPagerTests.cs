using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using XForge.Ide.App.Components;
using Xunit;

namespace XForge.Ide.Ui.Tests;

public class DataPagerTests : BunitTestBase
{
    [Fact]
    public void Shows_summary_and_invokes_next_page()
    {
        var invokedPage = -1;

        var cut = Context.Render<DataPager>(parameters => parameters
            .Add(p => p.TotalItems, 100)
            .Add(p => p.Page, 1)
            .Add(p => p.PageSize, 20)
            .Add(p => p.PageChanged, EventCallback.Factory.Create<int>(this, page => invokedPage = page)));

        cut.Find("button[aria-label='Próxima página']").Click();

        cut.Markup.Should().Contain("Mostrando 1–20 de 100");
        cut.Markup.Should().Contain("Página 1 de 5");
        invokedPage.Should().Be(2);
    }

    [Fact]
    public void Hidden_when_items_fit_in_one_page()
    {
        var cut = Context.Render<DataPager>(parameters => parameters
            .Add(p => p.TotalItems, 5)
            .Add(p => p.Page, 1)
            .Add(p => p.PageSize, 20));

        cut.Markup.Trim().Should().BeEmpty();
    }
}
