using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using XForge.Ide.App.Components;
using Xunit;

namespace XForge.Ide.Ui.Tests;

public class ConfirmDialogTests : BunitTestBase
{
    [Fact]
    public void Cancel_invokes_close_without_confirming()
    {
        var closed = false;
        var confirmed = false;

        var cut = Context.Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Title, "Excluir projeto")
            .Add(p => p.ConfirmMessage, "Esta ação é destrutiva.")
            .Add(p => p.OpenChanged, EventCallback.Factory.Create<bool>(this, _ => closed = true))
            .Add(p => p.OnConfirm, EventCallback.Factory.Create(this, () => confirmed = true)));

        cut.FindAll("button").First(b => b.TextContent.Contains("Cancelar")).Click();

        closed.Should().BeTrue();
        confirmed.Should().BeFalse();
    }

    [Fact]
    public void Confirm_invokes_action_and_closes()
    {
        var confirmed = false;
        var closed = false;

        var cut = Context.Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Title, "Excluir projeto")
            .Add(p => p.ConfirmMessage, "Esta ação é destrutiva.")
            .Add(p => p.ConfirmText, "Excluir")
            .Add(p => p.OpenChanged, EventCallback.Factory.Create<bool>(this, _ => closed = true))
            .Add(p => p.OnConfirm, EventCallback.Factory.Create(this, () => confirmed = true)));

        cut.FindAll("button").First(b => b.TextContent.Contains("Excluir")).Click();

        confirmed.Should().BeTrue();
        closed.Should().BeTrue();
    }
}
