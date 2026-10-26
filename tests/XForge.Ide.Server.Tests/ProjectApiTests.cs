using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace XForge.Ide.Server.Tests;

public class ProjectApiTests : IClassFixture<IdeWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProjectApiTests(IdeWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task List_projects_returns_array()
    {
        var response = await _client.GetAsync("/api/projects");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Create_get_and_delete_project_roundtrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ide-api-roundtrip-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);

        try
        {
            var createResponse = await _client.PostAsJsonAsync("/api/projects", new
            {
                name = "Projeto API",
                path = directory,
                templateKind = "clean-architecture",
                description = "teste de integração"
            });

            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            var id = ReadId(created);

            var getResponse = await _client.GetAsync($"/api/projects/{id}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var deleteResponse = await _client.DeleteAsync($"/api/projects/{id}");
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var missingResponse = await _client.GetAsync($"/api/projects/{id}");
            missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task File_access_outside_workspace_is_forbidden()
    {
        var outside = Path.Combine(Path.GetTempPath(), "ide-outside-" + Guid.NewGuid().ToString("N")[..8]);

        var response = await _client.GetAsync($"/api/files?path={Uri.EscapeDataString(outside)}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Guid ReadId(JsonElement element)
    {
        if (element.TryGetProperty("id", out var camelId))
        {
            return camelId.GetGuid();
        }

        return element.GetProperty("Id").GetGuid();
    }
}
