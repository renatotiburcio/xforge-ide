using FluentAssertions;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using XForge.Ide.Infrastructure.Persistence;
using Xunit;

namespace XForge.Ide.Core.Tests;

/// <summary>Store de snapshots de interface (upsert/get/delete por chave).</summary>
public class UiSnapshotStoreTests : ServiceTestBase
{
    private IUiSnapshotStore Store => new UiSnapshotRepository(Db);

    [Fact]
    public async Task Upsert_cria_e_atualiza_pela_mesma_chave()
    {
        var key = UiStatePersistorKey("/code", Guid.NewGuid());

        await Store.UpsertAsync(new UiSnapshot { Key = key, Route = "/code", Json = "{\"a\":1}" });
        var criado = await Store.GetAsync(key);
        criado.Should().NotBeNull();
        criado!.Json.Should().Be("{\"a\":1}");

        await Store.UpsertAsync(new UiSnapshot { Key = key, Route = "/code", Json = "{\"a\":2}" });
        var atualizado = await Store.GetAsync(key);
        atualizado!.Json.Should().Be("{\"a\":2}");
    }

    [Fact]
    public async Task Chaves_de_projetos_diferentes_nao_se_misturam()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();

        await Store.UpsertAsync(new UiSnapshot { Key = UiStatePersistorKey("/code", p1), Json = "projeto1" });
        await Store.UpsertAsync(new UiSnapshot { Key = UiStatePersistorKey("/code", p2), Json = "projeto2" });

        var um = await Store.GetAsync(UiStatePersistorKey("/code", p1));
        var dois = await Store.GetAsync(UiStatePersistorKey("/code", p2));
        um!.Json.Should().Be("projeto1");
        dois!.Json.Should().Be("projeto2");
    }

    [Fact]
    public async Task Delete_remove_o_snapshot()
    {
        var key = UiStatePersistorKey("/code", Guid.NewGuid());
        await Store.UpsertAsync(new UiSnapshot { Key = key, Json = "x" });

        await Store.DeleteAsync(key);

        var restante = await Store.GetAsync(key);
        restante.Should().BeNull();
    }

    /// <summary>Formato da chave do UiStatePersistor ("rota|projectId").</summary>
    private static string UiStatePersistorKey(string route, Guid projectId)
        => $"{route.Trim().ToLowerInvariant()}|{projectId}";
}
