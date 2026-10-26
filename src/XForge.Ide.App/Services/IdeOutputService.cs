using System.Collections.Concurrent;

namespace XForge.Ide.App.Services;

/// <summary>
/// Log de saída da IDE (canal "Saída" do painel inferior). Qualquer componente
/// pode registrar linhas; a UI assina o evento para atualizar em tempo real.
/// </summary>
public sealed class IdeOutputService
{
    private readonly ConcurrentQueue<string> _lines = new();
    private const int Capacity = 500;

    public event Action? Changed;

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public void Write(string channel, string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _lines.Enqueue($"[{stamp}] {channel}: {line}");
        while (_lines.Count > Capacity && _lines.TryDequeue(out _))
        {
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        _lines.Clear();
        Changed?.Invoke();
    }
}
