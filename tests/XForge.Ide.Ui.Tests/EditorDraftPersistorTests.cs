using FluentAssertions;
using XForge.Ide.App.Services;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using Xunit;

namespace XForge.Ide.Ui.Tests;

/// <summary>
/// Rascunhos do editor: debounce agrupa digitação, flush grava, salvar
/// (Delete) remove o rascunho e o dispose flusha o pendente.
/// </summary>
public class EditorDraftPersistorTests
{
    private sealed class FakeDraftStore : IEditorDraftStore
    {
        public Dictionary<string, EditorDraft> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<EditorDraft?> GetAsync(string path, CancellationToken ct = default)
            => Task.FromResult(Items.TryGetValue(path, out var d) ? d : null);

        public Task UpsertAsync(EditorDraft draft, CancellationToken ct = default)
        {
            Items[draft.Path] = draft;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            Items.Remove(path);
            return Task.CompletedTask;
        }

        public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            var chaves = Items.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in chaves) Items.Remove(k);
            return Task.FromResult(chaves.Count);
        }
    }

    [Fact]
    public async Task Flush_grava_o_rascunho_pendente()
    {
        var store = new FakeDraftStore();
        var drafts = new EditorDraftPersistor(store);

        drafts.Save(@"C:\proj\A.cs", "conteudo novo");
        await drafts.FlushAsync();

        var salvo = await drafts.GetAsync(@"C:\proj\A.cs");
        salvo.Should().NotBeNull();
        salvo!.Content.Should().Be("conteudo novo");
    }

    [Fact]
    public async Task Debounce_agrupa_digitacao_seguida_em_uma_gravacao()
    {
        var store = new FakeDraftStore();
        var drafts = new EditorDraftPersistor(store);

        drafts.Save(@"C:\proj\A.cs", "um");
        drafts.Save(@"C:\proj\A.cs", "um dois");
        await Task.Delay(EditorDraftPersistor.DebounceMs + 350);

        store.Items.Should().HaveCount(1);
        var salvo = await drafts.GetAsync(@"C:\proj\A.cs");
        salvo!.Content.Should().Be("um dois");
    }

    [Fact]
    public async Task Delete_remove_o_rascunho_salvo()
    {
        var store = new FakeDraftStore();
        var drafts = new EditorDraftPersistor(store);

        drafts.Save(@"C:\proj\A.cs", "rascunho");
        await drafts.FlushAsync();
        await drafts.DeleteAsync(@"C:\proj\A.cs");

        var restante = await drafts.GetAsync(@"C:\proj\A.cs");
        restante.Should().BeNull();
    }

    [Fact]
    public async Task Dispose_flusha_o_que_esta_pendente()
    {
        var store = new FakeDraftStore();
        var drafts = new EditorDraftPersistor(store);

        drafts.Save(@"C:\proj\B.cs", "sem tempo de debounce");
        await drafts.DisposeAsync();

        var salvo = await drafts.GetAsync(@"C:\proj\B.cs");
        salvo.Should().NotBeNull();
        salvo!.Content.Should().Be("sem tempo de debounce");
    }
}
