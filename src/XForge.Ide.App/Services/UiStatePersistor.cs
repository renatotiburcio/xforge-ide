using System.Text.Json;
using Microsoft.AspNetCore.Components;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.App.Services;

/// <summary>
/// Persistencia do contexto de trabalho da interface ("onde eu estava") por
/// rota/projeto. Gravacoes sao agrupadas por debounce e liberadas (flush) na
/// navegacao e no dispose - o F5 devolve o estado sem travar a UI.
/// </summary>
public sealed class UiStatePersistor : IAsyncDisposable
{
    /// <summary>Debounce das gravacoes (ms) - cliques seguidos viram uma escrita.</summary>
    public const int DebounceMs = 700;

    /// <summary>Teto do JSON por snapshot (alinha com a coluna do banco).</summary>
    public const int MaxJsonLength = 16000;

    private readonly IUiSnapshotStore _store;
    private readonly NavigationManager _nav;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, UiSnapshot> _pending = new(StringComparer.Ordinal);
    private CancellationTokenSource? _debounce;

    /// <summary>Avisa quando um snapshot estourou o teto e foi podado (nao gravado).</summary>
    public event Action<string>? Pruned;

    public UiStatePersistor(IUiSnapshotStore store, NavigationManager nav)
    {
        _store = store;
        _nav = nav;
        // flush na navegacao (Task.Run evita sync-over-async no contexto do circuito)
        _nav.LocationChanged += (_, _) => FlushOnNavigate();
    }

    /// <summary>
    /// Libera o lote pendente na troca de rota. O Task.Run mantem a espera fora
    /// do contexto de thread do circuito (sem risco de deadlock); qualquer falha
    /// e engolida - gravacao nunca quebra a navegacao.
    /// </summary>
    public void FlushOnNavigate()
    {
        try
        {
            Task.Run(() => FlushAsync()).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>"rota|projectId" - projeto ausente vira "global".</summary>
    public static string KeyFor(string route, Guid? projectId)
    {
        var r = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim().ToLowerInvariant();
        return projectId is Guid id ? $"{r}|{id}" : $"{r}|global";
    }

    /// <summary>Le o snapshot da rota/projeto. JSON invalido ou versao antiga = null.</summary>
    public async Task<T?> RestoreAsync<T>(string route, Guid? projectId, CancellationToken ct = default)
        where T : class
    {
        UiSnapshot? snapshot;
        try
        {
            snapshot = await _store.GetAsync(KeyFor(route, projectId), ct);
        }
        catch (Exception)
        {
            return null;
        }

        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Json) || snapshot.Json == "{}")
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(snapshot.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Agenda a gravacao do estado (debounce).</summary>
    public void Save<T>(string route, Guid? projectId, T state)
    {
        var json = JsonSerializer.Serialize(state);
        if (json.Length > MaxJsonLength)
        {
            Pruned?.Invoke(route);
            return;
        }

        var key = KeyFor(route, projectId);
        lock (_gate)
        {
            _pending[key] = new UiSnapshot
            {
                Key = key,
                Route = route,
                ProjectId = projectId,
                Json = json
            };
        }
        ScheduleFlush();
    }

    /// <summary>Grava agora tudo que esta pendente.</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<UiSnapshot> batch;
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return;
            }
            batch = _pending.Values.ToList();
            _pending.Clear();
        }

        foreach (var snapshot in batch)
        {
            try
            {
                await _store.UpsertAsync(snapshot, ct);
            }
            catch (Exception)
            {
                // falha pontual nao derruba o restante do lote
            }
        }
    }

    private void ScheduleFlush()
    {
        lock (_gate)
        {
            _debounce?.Cancel();
            _debounce = new CancellationTokenSource();
        }

        var token = _debounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceMs, token);
                await FlushAsync(token);
            }
            catch (OperationCanceledException)
            {
                // novo agendamento chegou; este sai
            }
            catch (Exception)
            {
                // idem: gravacao nunca deve derrubar o circuito
            }
        }, token);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _debounce?.Cancel();
        }
        await FlushAsync();
    }
}

/// <summary>
/// Estado do Explorer do editor: caminhos de pasta expandidos, relativos a raiz
/// do projeto (assim o snapshot sobrevive a mudanca de caminho do projeto).
/// </summary>
public sealed record ExplorerSnapshot(IReadOnlyList<string> ExpandedPaths);

/// <summary>Ultimo filtro da busca global (o F5 devolve a pesquisa como estava).</summary>
public sealed record SearchFilterSnapshot(
    string Term,
    string Include,
    string Exclude,
    string Replacement,
    bool CaseSensitive,
    bool WholeWord,
    bool Regex);

/// <summary>Diagrama ER: dicionário selecionado (o zoom/pan faz auto-fit ao abrir).</summary>
public sealed record ErDiagramSnapshot(Guid? DictionaryId);
