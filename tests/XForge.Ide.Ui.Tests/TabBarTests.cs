using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using XForge.Ide.App.Components;
using Xunit;

namespace XForge.Ide.Ui.Tests;

public class TabBarTests : BunitTestBase
{
    private static TabBar.EditorTab Tab(string id, string title, bool active, bool dirty)
        => new()
        {
            Id = id,
            Title = title,
            Path = $"src/{id}.cs",
            Language = "csharp",
            IsActive = active,
            Dirty = dirty
        };

    [Fact]
    public void Renders_tabs_with_accessible_roles_and_dirty_indicator()
    {
        var cut = Context.Render<TabBar>(parameters => parameters
            .Add(p => p.Tabs, new[]
            {
                Tab("a", "A.cs", true, true),
                Tab("b", "B.cs", false, false)
            }));

        cut.Find("[role='tablist']").Should().NotBeNull();
        cut.FindAll("[role='tab']").Should().HaveCount(2);
        cut.Find("[role='tab'][aria-label='A.cs']").GetAttribute("aria-selected").Should().Be("true");
        cut.Find(".ide-tab__dirty").Should().NotBeNull();
    }

    [Fact]
    public void Close_click_invokes_callback_with_tab()
    {
        TabBar.EditorTab? closed = null;

        var cut = Context.Render<TabBar>(parameters => parameters
            .Add(p => p.Tabs, new[]
            {
                Tab("a", "A.cs", true, false),
                Tab("b", "B.cs", false, false)
            })
            .Add(p => p.OnClose, EventCallback.Factory.Create<TabBar.EditorTab>(this, tab => closed = tab)));

        cut.Find("button[aria-label='Fechar B.cs']").Click();

        closed.Should().NotBeNull();
        closed!.Id.Should().Be("b");
    }
}
