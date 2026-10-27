using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.App.Services;

/// <summary>
/// Rascunhos do editor com debounce: cada pausa na digitação grava uma vez
/// (não a cada tecla), e o flush acontece na navegação e no dispose. Salvar
/// (Ctrl+S) apaga o rascunho — o disco volta a ser a verdade.
/// </summary>
public sealed class EditorDraftPersistor : IAsyncDisposable
{
    public const int DebounceMs = 500;

    private readonly IEditorDraftStore _store;
    private readonly Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private CancellationTokenSource? _debounce;

    public EditorDraftPersistor(IEditorDraftStore store) => _store = store;

    public Task<EditorDraft?> GetAsync(string path, CancellationToken ct = default)
        => _store.GetAsync(path, ct);

    /// <summary>Agenda a gravação do rascunho (debounce).</summary>
    public void Save(string path, string content)
    {
        lock (_gate)
        {
            _pending[path] = content;
        }
        ScheduleFlush();
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _pending.Remove(path);
        }
        await _store.DeleteAsync(path, ct);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<KeyValuePair<string, string>> batch;
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                return;
            }
            batch = _pending.ToList();
            _pending.Clear();
        }

        foreach (var (path, content) in batch)
        {
            try
            {
                await _store.UpsertAsync(new EditorDraft { Path = path, Content = content }, ct);
            }
            catch (Exception)
            {
                // falha pontual não derruba o restante do lote
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
                // nova digitação chegou; este ciclo sai
            }
            catch (Exception)
            {
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
