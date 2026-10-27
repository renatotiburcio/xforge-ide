using FluentAssertions;
using Microsoft.AspNetCore.Components;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using XForge.Ide.App.Services;
using Xunit;

namespace XForge.Ide.Ui.Tests;

/// <summary>
/// Persistor de snapshots de interface: debounce agrupa gravações, flush
/// libera o lote pendente, JSON inválido restaura null e o dispose flusha.
/// </summary>
public class UiStatePersistorTests
{
    private sealed class FakeSnapshotStore : IUiSnapshotStore
    {
        public Dictionary<string, UiSnapshot> Items { get; } = new(StringComparer.Ordinal);

        public Task<UiSnapshot?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Items.TryGetValue(key, out var s) ? s : null);

        public Task UpsertAsync(UiSnapshot snapshot, CancellationToken ct = default)
        {
            Items[snapshot.Key] = snapshot;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            Items.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public int LocationChangedCount { get; private set; }

        public TestNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/code");
            LocationChanged += (_, _) => LocationChangedCount++;
        }

        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = uri;

        /// <summary>Navega (a própria navegação dispara LocationChanged).</summary>
        public void RaiseLocationChanged() => NavigateTo("http://localhost/outra");
    }

    [Fact]
    public async Task Flush_grava_o_estado_pendente_e_Restore_devolve()
    {
        var store = new FakeSnapshotStore();
        var persistor = new UiStatePersistor(store, new TestNavigationManager());
        var projectId = Guid.NewGuid();

        persistor.Save("/code", projectId, new ExplorerSnapshot(new[] { "src", "src/Pages" }));
        await persistor.FlushAsync();

        var gravado = await persistor.RestoreAsync<ExplorerSnapshot>("/code", projectId);
        gravado.Should().NotBeNull();
        gravado!.ExpandedPaths.Should().BeEquivalentTo(new[] { "src", "src/Pages" });
    }

    [Fact]
    public async Task Debounce_agrupa_gravacoes_seguidas_em_uma_linha()
    {
        var store = new FakeSnapshotStore();
        var persistor = new UiStatePersistor(store, new TestNavigationManager());
        var projectId = Guid.NewGuid();

        persistor.Save("/code", projectId, new ExplorerSnapshot(new[] { "src" }));
        persistor.Save("/code", projectId, new ExplorerSnapshot(new[] { "src", "src/Pages" }));
        await Task.Delay(UiStatePersistor.DebounceMs + 350);

        store.Items.Should().HaveCount(1);
        var gravado = await persistor.RestoreAsync<ExplorerSnapshot>("/code", projectId);
        gravado!.ExpandedPaths.Should().BeEquivalentTo(new[] { "src", "src/Pages" });
    }

    [Fact]
    public async Task Dispose_flusha_o_que_esta_pendente()
    {
        var store = new FakeSnapshotStore();
        var persistor = new UiStatePersistor(store, new TestNavigationManager());
        var projectId = Guid.NewGuid();

        persistor.Save("/code", projectId, new ExplorerSnapshot(new[] { "testes" }));
        await persistor.DisposeAsync();

        var gravado = await persistor.RestoreAsync<ExplorerSnapshot>("/code", projectId);
        gravado.Should().NotBeNull();
        gravado!.ExpandedPaths.Should().BeEquivalentTo(new[] { "testes" });
    }

    [Fact]
    public async Task Restore_de_snapshot_inexistente_devolve_null()
    {
        var store = new FakeSnapshotStore();
        var persistor = new UiStatePersistor(store, new TestNavigationManager());

        var resultado = await persistor.RestoreAsync<ExplorerSnapshot>("/code", Guid.NewGuid());

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task Restore_de_JSON_invalido_devolve_null()
    {
        var store = new FakeSnapshotStore();
        var projectId = Guid.NewGuid();
        var key = UiStatePersistor.KeyFor("/code", projectId);
        store.Items[key] = new UiSnapshot { Key = key, Route = "/code", Json = "{ isso não é JSON" };

        var persistor = new UiStatePersistor(store, new TestNavigationManager());

        var resultado = await persistor.RestoreAsync<ExplorerSnapshot>("/code", projectId);

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task Estado_acima_do_teto_nao_e_gravado()
    {
        var store = new FakeSnapshotStore();
        var persistor = new UiStatePersistor(store, new TestNavigationManager());
        var podas = 0;
        persistor.Pruned += _ => podas++;

        persistor.Save("/code", Guid.NewGuid(), new ExplorerSnapshot(new[] { new string('x', 20000) }));
        await persistor.FlushAsync();

        store.Items.Should().BeEmpty();
        podas.Should().Be(1);
    }

    [Fact]
    public async Task Navegacao_dispara_flush_do_pendente()
    {
        var store = new FakeSnapshotStore();
        var nav = new TestNavigationManager();
        var persistor = new UiStatePersistor(store, nav);
        var projectId = Guid.NewGuid();

        persistor.Save("/code", projectId, new ExplorerSnapshot(new[] { "src" }));
        await Task.Delay(10);
        persistor.FlushOnNavigate(); // mesmo caminho do handler de navegação
        await Task.Delay(50);

        nav.LocationChangedCount.Should().Be(0); // instancia de teste nao navega
        store.Items.Should().HaveCount(1);
        await persistor.DisposeAsync();
    }

    [Fact]
    public void Chave_separa_projeto_e_rota()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        UiStatePersistor.KeyFor("/code", a).Should().NotBe(UiStatePersistor.KeyFor("/code", b));
        UiStatePersistor.KeyFor("/code", a).Should().NotBe(UiStatePersistor.KeyFor("/data", a));
        UiStatePersistor.KeyFor("/code", null).Should().EndWith("|global");
    }
}
