using FluentAssertions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Core.Tests;

public class ProjectTests
{
    [Fact]
    public void New_project_has_generated_id_and_defaults()
    {
        var project = new Project { Name = "Meu Sistema", Path = @"E:\Projetos\MeuSistema" };

        project.Id.Should().NotBe(Guid.Empty);
        project.TemplateKind.Should().Be("clean-architecture");
        project.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        project.LastOpenedAt.Should().BeNull();
    }

    [Fact]
    public void Result_failure_carries_error()
    {
        var result = XForge.Ide.Core.Results.Result< string>.Failure("erro");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("erro");
        var act = () => result.Value;
        act.Should().Throw<InvalidOperationException>();
    }
}
