using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace XForge.Ide.Server.Services;

/// <summary>Uma sugestão de autocompletar.</summary>
public sealed record CompletionItem(
    string Label,
    string Detail,
    string Kind,
    string? InsertText,
    int SortPriority);

/// <summary>Um erro/aviso de compilação.</summary>
public sealed record DiagnosticItem(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Severity,
    string Message,
    string Id);

/// <summary>Informação de hover (quick info).</summary>
public sealed record HoverInfo(string Contents);

/// <summary>
/// IntelliSense real do editor. Indexa os fontes do projeto (montando uma
/// CSharpCompilation com as referências do runtime e do próprio projeto) e usa o
/// modelo semântico do Roslyn para completions, hover e diagnósticos.
/// A compilação é refeita apenas quando o arquivo editado muda (o cliente debounce).
/// </summary>
public sealed class RoslynIntellisenseService
{
    private readonly ILogger<RoslynIntellisenseService> _log;
    private readonly ConcurrentDictionary<string, Lazy<Task<ProjectIndex>>> _indexes = new(StringComparer.OrdinalIgnoreCase);

    public RoslynIntellisenseService(ILogger<RoslynIntellisenseService> log) => _log = log;

    /// <summary>Fontes + referências de um projeto (imutável enquanto os arquivos não mudam).</summary>
    private sealed class ProjectIndex
    {
        public required ConcurrentDictionary<string, string> Sources { get; init; }
        public required IReadOnlyList<MetadataReference> References { get; init; }
        public required string Root { get; init; }
    }

    // ------------------------------------------------------------ referências

    private static readonly Lazy<List<MetadataReference>> RuntimeReferences = new(() =>
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(Assembly a)
        {
            try
            {
                if (a.IsDynamic || string.IsNullOrWhiteSpace(a.Location))
                {
                    return;
                }
                var name = a.GetName().Name ?? string.Empty;
                if (seen.Add(name))
                {
                    refs.Add(MetadataReference.CreateFromFile(a.Location));
                }
            }
            catch
            {
            }
        }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Add(a);
        }
        // assemblies do runtime que ainda não foram carregados
        Add(typeof(object).Assembly);
        Add(typeof(Enumerable).Assembly);
        Add(typeof(System.Text.Json.JsonSerializer).Assembly);
        Add(typeof(Microsoft.AspNetCore.Components.ComponentBase).Assembly);
        return refs;
    });

    // ------------------------------------------------------------- indexação

    public Task InvalidateAsync(string projectRootPath)
    {
        _indexes.TryRemove(projectRootPath, out _);
        return Task.CompletedTask;
    }

    private async Task<ProjectIndex> GetIndexAsync(string projectRoot, CancellationToken ct)
    {
        var lazy = _indexes.GetOrAdd(projectRoot, p => new Lazy<Task<ProjectIndex>>(() => BuildIndexAsync(p, ct)));
        return await lazy.Value;
    }

    private static async Task<ProjectIndex> BuildIndexAsync(string projectRoot, CancellationToken ct)
    {
        var sources = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(projectRoot, f);
            if (rel.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("obj", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }
            try
            {
                sources[f] = await File.ReadAllTextAsync(f, ct);
            }
            catch
            {
            }
        }

        var references = RuntimeReferences.Value.ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in Directory.EnumerateFiles(projectRoot, "*.dll", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(projectRoot, dll);
            if (!rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                if (seen.Add(name))
                {
                    references.Add(MetadataReference.CreateFromFile(dll));
                }
            }
            catch
            {
            }
        }

        return new ProjectIndex { Sources = sources, References = references, Root = projectRoot };
    }

    /// <summary>Compila o projeto com o conteúdo atual do arquivo editado.</summary>
    private static async Task<(CSharpCompilation Compilation, SyntaxTree Tree)> CompileWithAsync(
        ProjectIndex index, string filePath, string content, CancellationToken ct)
    {
        var trees = new List<SyntaxTree>(index.Sources.Count + 1);
        foreach (var (path, text) in index.Sources)
        {
            ct.ThrowIfCancellationRequested();
            var isTarget = string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase);
            var source = isTarget ? content : text;
            trees.Add(CSharpSyntaxTree.ParseText(source, path: path,
                options: new CSharpParseOptions(LanguageVersion.Preview)));
        }

        // arquivo novo (ainda não salvo em disco): entra na compilação
        if (!index.Sources.ContainsKey(filePath))
        {
            trees.Add(CSharpSyntaxTree.ParseText(content, path: filePath,
                options: new CSharpParseOptions(LanguageVersion.Preview)));
        }

        var compilation = CSharpCompilation.Create(
            "xforge-editor",
            trees.DistinctBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase),
            index.References.DistinctBy(r => r.Display, StringComparer.OrdinalIgnoreCase),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var target = compilation.SyntaxTrees.First(t =>
            string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        await Task.CompletedTask;
        return (compilation, target);
    }

    private async Task<(CSharpCompilation Compilation, SemanticModel Model, SyntaxTree Tree)?> PrepareAsync(
        string projectRoot, string filePath, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            var index = await GetIndexAsync(projectRoot, ct);
            var (compilation, tree) = await CompileWithAsync(index, filePath, content, ct);
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            return (compilation, model, tree);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha ao preparar análise de {File}", filePath);
            return null;
        }
    }

    // ------------------------------------------------------------ completions

    public async Task<IReadOnlyList<CompletionItem>> CompletionsAsync(
        string projectRoot, string filePath, string content, int line, int column,
        CancellationToken ct = default)
    {
        var result = new List<CompletionItem>();
        try
        {
            var prepared = await PrepareAsync(projectRoot, filePath, content, ct);
            if (prepared is null)
            {
                return result;
            }
            var (compilation, model, tree) = prepared.Value;
            var pos = ToOffset(content, line, column);
            if (pos < 0)
            {
                return result;
            }

            var root = tree.GetRoot();
            var token = root.FindToken(pos);
            var before = content[..pos].TrimEnd();

            // 1) membros após "." — usa o tipo da expressão à esquerda
            if (before.EndsWith(".", StringComparison.Ordinal) || before.EndsWith("?.", StringComparison.Ordinal))
            {
                var memberAccess = root.FindNode(TextSpan.FromBounds(Math.Max(0, pos - 1), Math.Min(content.Length, pos)))
                    .AncestorsAndSelf()
                    .OfType<MemberAccessExpressionSyntax>()
                    .FirstOrDefault();

                if (memberAccess is not null)
                {
                    var type = model.GetTypeInfo(memberAccess.Expression).Type;
                    if (type is not null)
                    {
                        foreach (var m in type.GetMembers()
                                     .Where(m => m.DeclaredAccessibility == Accessibility.Public)
                                     .Where(m => m is IMethodSymbol or IFieldSymbol or IPropertySymbol or IEventSymbol)
                                     .GroupBy(m => m.Name)
                                     .Select(g => g.First())
                                     .OrderBy(m => m.Name, StringComparer.Ordinal))
                        {
                            result.Add(new CompletionItem(
                                m.Name,
                                m.ToDisplayString(),
                                KindOf(m),
                                m is IMethodSymbol ? m.Name + "(" : m.Name,
                                m.IsStatic ? 1 : 0));
                        }
                        if (result.Count > 0)
                        {
                            return Sort(result);
                        }
                    }
                }
            }

            // 2) símbolos no escopo
            foreach (var s in model.LookupSymbols(position: pos))
            {
                if (!s.IsImplicitlyDeclared)
                {
                    result.Add(new CompletionItem(s.Name, s.ToDisplayString(), KindOf(s), s.Name, s.IsStatic ? 1 : 0));
                }
            }

            // 3) palavras-chave
            result.AddRange(Keywords());
            return Sort(result);
        }
        catch (OperationCanceledException)
        {
            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha no autocompletar de {File}", filePath);
            return result;
        }
    }

    private static IReadOnlyList<CompletionItem> Sort(List<CompletionItem> items)
        => items
            .GroupBy(i => i.Label)
            .Select(g => g.First())
            .OrderBy(i => i.SortPriority)
            .ThenBy(i => i.Label, StringComparer.Ordinal)
            .Take(150)
            .ToList();

    private static IEnumerable<CompletionItem> Keywords()
    {
        string[] ks =
        {
            "var", "public", "private", "protected", "internal", "static", "readonly", "const",
            "class", "record", "interface", "struct", "enum", "namespace", "using", "new",
            "return", "if", "else", "foreach", "for", "while", "switch", "case", "break",
            "continue", "async", "await", "try", "catch", "finally", "throw", "null", "true", "false",
            "this", "base", "get", "set", "int", "string", "bool", "double", "decimal", "void", "object",
        };
        foreach (var k in ks)
        {
            yield return new CompletionItem(k, "palavra-chave", "keyword", k, 3);
        }
    }

    // ------------------------------------------------------------ diagnósticos

    public async Task<IReadOnlyList<DiagnosticItem>> DiagnosticsAsync(
        string projectRoot, string filePath, string content, CancellationToken ct = default)
    {
        var result = new List<DiagnosticItem>();
        try
        {
            var prepared = await PrepareAsync(projectRoot, filePath, content, ct);
            if (prepared is null)
            {
                return result;
            }
            var model = prepared.Value.Model;
            var tree = prepared.Value.Tree;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in tree.GetDiagnostics(ct).Concat(model.GetDiagnostics()))
            {
                if (d.Severity is DiagnosticSeverity.Hidden or DiagnosticSeverity.Info)
                {
                    continue;
                }
                var span = d.Location.GetLineSpan();
                var start = span.StartLinePosition;
                var end = span.EndLinePosition;
                if (!seen.Add($"{d.Id}:{start.Line}:{start.Character}"))
                {
                    continue;
                }
                result.Add(new DiagnosticItem(
                    start.Line + 1,
                    start.Character + 1,
                    end.Line + 1,
                    end.Character + 1,
                    d.Severity.ToString().ToLowerInvariant(),
                    d.GetMessage(),
                    d.Id));
            }
            return result.Take(80).ToList();
        }
        catch (OperationCanceledException)
        {
            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha nos diagnósticos de {File}", filePath);
            return result;
        }
    }

    // ---------------------------------------------------------------- hover

    public async Task<HoverInfo?> HoverAsync(
        string projectRoot, string filePath, string content, int line, int column,
        CancellationToken ct = default)
    {
        try
        {
            var prepared = await PrepareAsync(projectRoot, filePath, content, ct);
            if (prepared is null)
            {
                return null;
            }
            var pos = ToOffset(content, line, column);
            if (pos < 0)
            {
                return null;
            }

            var tree = prepared.Value.Tree;
            var model = prepared.Value.Model;
            var node = tree.GetRoot().FindNode(TextSpan.FromBounds(pos, pos));
            var symbol = model.GetDeclaredSymbol(node)
                         ?? model.GetSymbolInfo(node).Symbol
                         ?? (node is IdentifierNameSyntax id ? model.GetSymbolInfo(id, ct).Symbol : null);
            if (symbol is null)
            {
                return null;
            }
            var doc = symbol.GetDocumentationCommentXml();
            return new HoverInfo($"```csharp\n{symbol.ToDisplayString()}\n```{(string.IsNullOrWhiteSpace(doc) ? "" : "\n" + doc)}");
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Hover sem resultado para {File}", filePath);
            return null;
        }
    }

    // --------------------------------------------------------------- utilitários

    private static int ToOffset(string content, int line, int column)
    {
        if (line <= 0 || column <= 0)
        {
            return -1;
        }
        var currentLine = 1;
        var index = 0;
        while (index < content.Length && currentLine < line)
        {
            if (content[index] == '\n')
            {
                currentLine++;
            }
            index++;
        }
        var result = index + column - 1;
        return result > content.Length ? content.Length : result;
    }

    private static string KindOf(ISymbol s) => s.Kind switch
    {
        SymbolKind.NamedType => "type",
        SymbolKind.Method => "method",
        SymbolKind.Property => "property",
        SymbolKind.Field => "field",
        SymbolKind.Event => "event",
        SymbolKind.Namespace => "namespace",
        SymbolKind.Local or SymbolKind.Parameter => "variable",
        _ => "symbol",
    };
}
