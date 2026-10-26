using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace XForge.Ide.Application.Services.Ai;

/// <summary>
/// Lê/grava a configuração de IA em %LOCALAPPDATA%\XForge\ide\ai-settings.json.
/// Service em cache (singleton) para evitar I/O a cada requisição.
/// </summary>
public sealed class AiSettingsStore
{
    private readonly ILogger<AiSettingsStore> _log;
    private readonly string _path;
    private AiSettings? _cache;
    private readonly object _lock = new();

    public AiSettingsStore(ILogger<AiSettingsStore> log)
    {
        _log = log;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XForge", "ide");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "ai-settings.json");
    }

    public AiSettings Get()
    {
        lock (_lock)
        {
            if (_cache is not null)
            {
                return _cache;
            }
            try
            {
                if (File.Exists(_path))
                {
                    _cache = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(_path)) ?? new AiSettings();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "config de IA inválida em {Path} — usando padrão", _path);
            }
            return _cache ??= new AiSettings();
        }
    }

    public void Save(AiSettings settings)
    {
        lock (_lock)
        {
            _cache = settings;
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
    }

    public bool HasKey() => !string.IsNullOrWhiteSpace(Get().ApiKey);

    /// <summary>Caminho do arquivo (exibido na tela de ajustes, sem revelar a chave).</summary>
    public string FilePath => _path;
}
