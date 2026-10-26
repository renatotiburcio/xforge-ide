using XForge.Ide.Application.Services;
using XForge.Ide.Infrastructure.Persistence;

namespace XForge.Ide.Core.Tests;

/// <summary>
/// Testes dos serviços de aplicação que sustentam a UI: acesso ao workspace,
/// busca global e substituição. Reaproveita a base com EF InMemory.
/// </summary>
public class ApplicationServicesTests : ServiceTestBase
{
    private readonly FileSystemService _files;
    private readonly SearchService _search;

    public ApplicationServicesTests()
    {
        _files = new FileSystemService(new ProjectRepository(Db));
        _search = new SearchService(new ProjectRepository(Db));
    }

    [Fact]
    public async Task Workspace_permite_subdiretorio_e_bloqueia_fora_do_projeto()
    {
        var root = Path.Combine(Path.GetTempPath(), "ide-ws-" + Guid.NewGuid().ToString("N")[..8]);
        var inside = Path.Combine(root, "src");
        var outside = Path.Combine(Path.GetTempPath(), "ide-ws-outside-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(inside);
        Directory.CreateDirectory(outside);

        try
        {
            var project = await Projects.CreateAsync("Projeto WS", root, null, null);
            Assert.True(project.IsSuccess);

            Assert.True(await _files.IsInsideWorkspaceAsync(inside));
            Assert.False(await _files.IsInsideWorkspaceAsync(outside));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Busca_respeita_globs_e_conta_ocorrencias_da_mesma_linha()
    {
        var root = Path.Combine(Path.GetTempPath(), "ide-search-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllLines(Path.Combine(root, "Program.cs"), new[]
            {
                "using System;",
                "using System.Text;",
                "no match"
            });
            File.WriteAllText(Path.Combine(root, "notes.txt"), "system");

            var options = new SearchOptions { Term = "using", Include = "*.cs" };
            var results = await _search.SearchAsync(root, options);

            var file = Assert.Single(results);
            Assert.Equal(2, file.Count);
            Assert.Equal(1, file.Matches[0].Line);
            Assert.NotNull(file.Matches[0].LineSpans);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Busca_respeita_palavra_inteira_e_case_sensitive()
    {
        var root = Path.Combine(Path.GetTempPath(), "ide-search-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "alpha alphax ALPHA");

            var insensitive = await _search.SearchAsync(root, new SearchOptions { Term = "alpha" });
            Assert.True(insensitive[0].Count == 3,
                "Matches: " + string.Join("; ", insensitive[0].Matches.Select(m => $"L{m.Line}:{m.LineText}")));

            var wholeWord = await _search.SearchAsync(root, new SearchOptions
            {
                Term = "alpha",
                WholeWord = true
            });
            Assert.Equal(2, wholeWord[0].Count); // "alphax" não conta

            var caseSensitive = await _search.SearchAsync(root, new SearchOptions
            {
                Term = "alpha",
                CaseSensitive = true
            });
            Assert.Equal(2, caseSensitive[0].Count); // "alpha" e o prefixo de "alphax"; "ALPHA" não entra

            var caseSensitiveWholeWord = await _search.SearchAsync(root, new SearchOptions
            {
                Term = "alpha",
                CaseSensitive = true,
                WholeWord = true
            });
            Assert.Equal(1, caseSensitiveWholeWord[0].Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Substituir_conta_e_altera_todas_as_ocorrencias()
    {
        var root = Path.Combine(Path.GetTempPath(), "ide-search-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var file = Path.Combine(root, "replace.txt");
            File.WriteAllText(file, "alpha alpha beta");

            var options = new SearchOptions { Term = "alpha" };
            Assert.Equal(2, await SearchService.CountInFileAsync(file, options));

            var replaced = await _search.ReplaceInFileAsync(file, options, "X");
            Assert.Equal(2, replaced);
            Assert.Equal("X X beta", await File.ReadAllTextAsync(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Busca_com_regex_invalida_nao_estoura()
    {
        var root = Path.Combine(Path.GetTempPath(), "ide-search-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "conteudo");

            var result = await _search.SearchAsync(root, new SearchOptions
            {
                Term = "[a-",
                Regex = true
            });

            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
