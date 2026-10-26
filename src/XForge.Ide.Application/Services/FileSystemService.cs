using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;

namespace XForge.Ide.Application.Services;

/// <summary>
/// Acesso ao sistema de arquivos do workspace, restrito aos diretórios dos projetos
/// registrados na IDE (segurança: nunca caminhos arbitrários fora do workspace).
/// </summary>
public class FileSystemService
{
    private readonly IProjectRepository _projects;

    public FileSystemService(IProjectRepository projects) => _projects = projects;

    /// <summary>
    /// Lista o conteúdo de um diretório (2 níveis por chamada; diretórios primeiro).
    /// </summary>
    public async Task<IReadOnlyList<FileNode>> ListAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            return Array.Empty<FileNode>();
        }

        var nodes = new List<FileNode>();
        foreach (var dir in Directory.EnumerateDirectories(fullPath))
        {
            var info = new DirectoryInfo(dir);
            if (info.Attributes.HasFlag(FileAttributes.Hidden))
            {
                continue;
            }
            nodes.Add(new FileNode(info.Name, dir, IsDirectory: true, Size: 0, ModifiedAt: info.LastWriteTimeUtc));
        }
        foreach (var file in Directory.EnumerateFiles(fullPath))
        {
            var info = new FileInfo(file);
            nodes.Add(new FileNode(info.Name, file, IsDirectory: false, Size: info.Length, ModifiedAt: info.LastWriteTimeUtc));
        }

        await Task.CompletedTask;
        return nodes
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Lê o conteúdo textual de um arquivo.</summary>
    public async Task<string?> ReadAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }
        return await File.ReadAllTextAsync(fullPath, ct);
    }

    /// <summary>Salva conteúdo textual (cria diretórios se necessário).</summary>
    public async Task<bool> WriteAsync(string path, string content, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(fullPath, content, ct);
        return true;
    }

    /// <summary>
    /// Valida que um caminho está dentro de algum projeto registrado (usado pelos endpoints).
    /// </summary>
    public async Task<bool> IsInsideWorkspaceAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(path);
        var projects = await _projects.ListAsync(ct);
        return projects.Any(p =>
            fullPath.Equals(Path.GetFullPath(p.Path), StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(Path.GetFullPath(p.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Nó da árvore de arquivos.</summary>
public sealed record FileNode(
    string Name,
    string Path,
    bool IsDirectory,
    long Size,
    DateTime ModifiedAt);
