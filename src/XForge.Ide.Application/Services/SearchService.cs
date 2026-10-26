using System.Text;
using System.Text.RegularExpressions;
using XForge.Ide.Core.Abstractions;

namespace XForge.Ide.Application.Services;

/// <summary>Uma ocorrência encontrada pela busca global.</summary>
public sealed record SearchMatch(
    string FilePath,
    string FileName,
    int Line,
    int Column,
    string LineText,
    string? Preview,
    /// <summary>
    /// Todas as ocorrências do termo na MESMA linha (início 0-based e tamanho,
    /// relativos a <see cref="LineText"/>) — usadas para destacar cada match
    /// sem repetir a linha (marcação multi-occurrence do VS Code).
    /// </summary>
    IReadOnlyList<SearchMatchSpan>? LineSpans = null);

/// <summary>Intervalo (início 0-based e tamanho) de uma ocorrência dentro da linha.</summary>
public sealed record SearchMatchSpan(int Start, int Length);

/// <summary>Resultado agregado por arquivo.</summary>
public sealed record SearchFileResult(
    string FilePath,
    string FileName,
    IReadOnlyList<SearchMatch> Matches)
{
    /// <summary>
    /// Total de ocorrências no arquivo (uma linha pode ter várias ocorrências).
    /// </summary>
    public int Count => Matches.Sum(m => m.LineSpans?.Count ?? 1);
}

/// <summary>Opções da busca (equivalentes ao painel de busca do VS Code).</summary>
public sealed record SearchOptions
{
    public string Term { get; init; } = string.Empty;
    public bool CaseSensitive { get; init; }
    public bool WholeWord { get; init; }
    public bool Regex { get; init; }

    /// <summary>
    /// Filtro de inclusão (glob simplificado: "*.cs", "src/**/*.razor", "!bin/**").
    /// Aceita uma lista separada por vírgula ou ponto-e-vírgula; itens com prefixo "!" excluem.
    /// </summary>
    public string? Include { get; init; }

    /// <summary>Filtro de exclusão (glob): "bin/**", "obj/**", "**/*.min.js".</summary>
    public string? Exclude { get; init; }

    public int MaxResults { get; init; } = 500;
}

/// <summary>
/// Busca textual/regex em todos os arquivos do projeto, no estilo VS Code
/// (Ctrl+Shift+F): resultados agrupados por arquivo, com linha/coluna e prévia
/// destacando todas as ocorrências da linha. Substituição usa exatamente as
/// mesmas regras de casamento da busca (inclusive palavra inteira).
/// </summary>
public sealed class SearchService
{
    private readonly IProjectRepository _projects;

    // diretórios/padrões ignorados (binário e build)
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".vscode", "Temp", "packages", ".nuget",
    };

    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".cache", ".db", ".db-wal", ".db-shm", ".png", ".jpg", ".jpeg",
        ".gif", ".ico", ".woff", ".woff2", ".ttf", ".zip", ".gz", ".nupkg", ".snk", ".pfx",
    };

    public SearchService(IProjectRepository projects) => _projects = projects;

    public async Task<IReadOnlyList<SearchFileResult>> SearchAsync(
        string rootPath,
        SearchOptions options,
        CancellationToken ct = default)
    {
        var results = new List<SearchFileResult>();
        if (string.IsNullOrWhiteSpace(options.Term) || !Directory.Exists(rootPath))
        {
            return results;
        }

        // regex inválida: sem resultados em vez de estourar (a UI avisa antes)
        if (options.Regex && ValidateRegex(options.Term) is not null)
        {
            return results;
        }

        Regex? rx = null;
        string? literal = null;
        if (options.Regex)
        {
            rx = new Regex(options.Term,
                options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        }
        else
        {
            literal = options.Term;
        }

        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var glob = GlobFilter.Parse(options.Include, options.Exclude);
        var total = 0;

        foreach (var file in EnumerateFiles(rootPath))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }
            var relative = RelativeTo(rootPath, file);
            if (!glob.IsMatch(relative))
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch
            {
                continue; // binário ou sem permissão
            }

            var matches = new List<SearchMatch>();
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                // a prévia é a linha sem espaços laterais; os intervalos passam a ser
                // relativos ao texto exibido (o destaque sai no lugar certo)
                var text = raw.Trim();
                var offset = raw.Length - raw.TrimStart().Length;
                var spans = new List<SearchMatchSpan>();
                foreach (var (index, length) in FindIn(raw, rx, literal, options.WholeWord, comparison))
                {
                    var start = Math.Max(0, index - offset);
                    var size = Math.Min(length, Math.Max(0, text.Length - start));
                    if (size <= 0)
                    {
                        continue;
                    }
                    spans.Add(new SearchMatchSpan(start, size));
                    total++;
                    if (total >= options.MaxResults)
                    {
                        goto done;
                    }
                }

                if (spans.Count == 0)
                {
                    continue;
                }

                var first = spans[0];
                matches.Add(new SearchMatch(
                    file,
                    Path.GetFileName(file),
                    i + 1,
                    first.Start + 1,
                    text,
                    Preview(text, first.Start, first.Length),
                    spans));
            }

            if (matches.Count > 0)
            {
                results.Add(new SearchFileResult(file, Path.GetFileName(file), matches));
            }
        }

    done:
        await Task.CompletedTask;
        return results.OrderByDescending(r => r.Count).ToList();
    }

    /// <summary>Substitui o termo em um arquivo (usado pelo "substituir em arquivo").</summary>
    public async Task<int> ReplaceInFileAsync(
        string path,
        SearchOptions options,
        string replacement,
        CancellationToken ct = default)
    {
        var file = Path.GetFullPath(path);
        if (!File.Exists(file))
        {
            return 0;
        }

        var text = await File.ReadAllTextAsync(file, ct);
        var matches = MatchesIn(text, options).ToList();
        if (matches.Count == 0)
        {
            return 0;
        }

        var sb = new StringBuilder(text.Length);
        var last = 0;
        foreach (var m in matches)
        {
            sb.Append(text, last, m.Index - last).Append(replacement);
            last = m.Index + m.Length;
        }
        sb.Append(text, last, text.Length - last);

        await File.WriteAllTextAsync(file, sb.ToString(), ct);
        return matches.Count;
    }

    /// <summary>
    /// Quantas ocorrências uma substituição alteraria no arquivo — espelha
    /// <see cref="ReplaceInFileAsync"/> para o modal de confirmação não mentir.
    /// </summary>
    public static async Task<int> CountInFileAsync(
        string path,
        SearchOptions options,
        CancellationToken ct = default)
    {
        var file = Path.GetFullPath(path);
        if (!File.Exists(file) || string.IsNullOrWhiteSpace(options.Term))
        {
            return 0;
        }
        string text;
        try
        {
            text = await File.ReadAllTextAsync(file, ct);
        }
        catch
        {
            return 0;
        }
        return MatchesIn(text, options).Count();
    }

    /// <summary>Valida a regex informada: null quando válida, senão a mensagem de erro.</summary>
    public static string? ValidateRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        try
        {
            _ = new Regex(pattern, RegexOptions.None);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Ocorrências que uma busca/substituição encontra no texto inteiro do arquivo.
    /// Expressão regular respeita CaseSensitive; o modo literal respeita
    /// CaseSensitive e palavra inteira.
    /// </summary>
    private static IEnumerable<TextSpan> MatchesIn(string text, SearchOptions options)
    {
        if (options.Regex)
        {
            Regex rx;
            try
            {
                rx = new Regex(options.Term, options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                yield break;
            }
            foreach (Match m in rx.Matches(text))
            {
                if (m.Success && m.Length > 0)
                {
                    yield return new TextSpan(m.Index, m.Length);
                }
            }
            yield break;
        }

        var needle = options.Term;
        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(needle, start, comparison);
            if (index < 0)
            {
                yield break;
            }
            if (!options.WholeWord || IsWholeWord(text, index, needle.Length))
            {
                yield return new TextSpan(index, needle.Length);
            }
            start = index + Math.Max(1, needle.Length);
        }
    }

    /// <summary>Intervalo (início 0-based e tamanho) dentro de um texto.</summary>
    private readonly record struct TextSpan(int Index, int Length);

    private static IEnumerable<(int Index, int Length)> FindIn(
        string line, Regex? rx, string? literal, bool wholeWord, StringComparison comparison)
    {
        if (rx is not null)
        {
            foreach (Match m in rx.Matches(line))
            {
                if (m.Success && m.Length > 0)
                {
                    yield return (m.Index, m.Length);
                }
            }
            yield break;
        }

        var needle = literal!;
        var start = 0;
        while (true)
        {
            var i = line.IndexOf(needle, start, comparison);
            if (i < 0)
            {
                yield break;
            }
            if (!wholeWord || IsWholeWord(line, i, needle.Length))
            {
                yield return (i, needle.Length);
            }
            start = i + Math.Max(1, needle.Length);
        }
    }

    private static bool IsWholeWord(string text, int index, int length)
    {
        var before = index > 0 && IsWordChar(text[index - 1]);
        var after = index + length < text.Length && IsWordChar(text[index + length]);
        return !before && !after;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string Preview(string line, int index, int length)
    {
        const int context = 30;
        var from = Math.Max(0, index - context);
        var to = Math.Min(line.Length, index + length + context);
        var slice = line[from..to].Trim();
        return (from > 0 ? "…" : string.Empty) + slice + (to < line.Length ? "…" : string.Empty);
    }

    private static string RelativeTo(string root, string fullPath)
    {
        try
        {
            var relative = Path.GetRelativePath(root, fullPath);
            return relative.Replace('\\', '/');
        }
        catch
        {
            return fullPath.Replace('\\', '/');
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subDirs;
            string[] files;
            try
            {
                subDirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var d in subDirs)
            {
                if (!SkipDirs.Contains(Path.GetFileName(d)))
                {
                    stack.Push(d);
                }
            }
            foreach (var f in files)
            {
                if (!SkipExtensions.Contains(Path.GetExtension(f)) &&
                    !Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal))
                {
                    yield return f;
                }
            }
        }
    }

    /// <summary>
    /// Converte globs (inclusão e exclusão) em um filtro único.
    /// Regras (estilo VS Code): padrão sem "/" casa o nome do arquivo em qualquer
    /// profundidade ("*.cs"); com "/" casa o caminho relativo à raiz ("src/**/*.razor");
    /// "**" casa zero ou mais segmentos.
    /// </summary>
    private sealed class GlobFilter
    {
        private readonly List<Rule> _rules = new();

        private GlobFilter() { }

        private sealed record Rule(Regex Pattern, bool Exclude);

        public static GlobFilter Parse(params string?[] specs)
        {
            var filter = new GlobFilter();
            foreach (var spec in specs)
            {
                foreach (var raw in Split(spec))
                {
                    var exclude = raw.StartsWith("!", StringComparison.Ordinal);
                    var pattern = exclude ? raw[1..] : raw;
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        continue;
                    }
                    filter._rules.Add(new Rule(ParseGlob(pattern), exclude));
                }
            }
            return filter;
        }

        public bool IsMatch(string relativePath)
        {
            foreach (var rule in _rules)
            {
                if (rule.Exclude && rule.Pattern.IsMatch(relativePath))
                {
                    return false;
                }
            }
            if (!_rules.Any(r => !r.Exclude))
            {
                return true; // sem inclusão: tudo que não foi excluído
            }
            foreach (var rule in _rules)
            {
                if (!rule.Exclude && rule.Pattern.IsMatch(relativePath))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<string> Split(string? spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
            {
                yield break;
            }
            foreach (var part in spec.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    yield return part;
                }
            }
        }

        /// <summary>Converte um glob simples (** e *) em Regex; padrão sem "/" casa só o nome.</summary>
        private static Regex ParseGlob(string glob)
        {
            var pattern = glob.Replace('\\', '/').Trim().Trim('/');
            var bare = !pattern.Contains('/');
            var rx = new StringBuilder("^");
            if (bare)
            {
                rx.Append("(?:.*/)?");
            }

            var i = 0;
            while (i < pattern.Length)
            {
                var c = pattern[i];
                if (c == '*')
                {
                    var j = i;
                    while (j < pattern.Length && pattern[j] == '*')
                    {
                        j++;
                    }
                    if (j - i >= 2)
                    {
                        // "**": zero ou mais segmentos inteiros
                        if (j < pattern.Length && pattern[j] == '/')
                        {
                            rx.Append("(?:.*/)?");
                            i = j + 1;
                        }
                        else
                        {
                            rx.Append(".*");
                            i = j;
                        }
                    }
                    else
                    {
                        rx.Append("[^/]*");
                        i = j;
                    }
                    continue;
                }
                if (c == '?')
                {
                    rx.Append("[^/]");
                    i++;
                    continue;
                }
                if (c == '/')
                {
                    rx.Append('/');
                    i++;
                    continue;
                }
                rx.Append(Regex.Escape(c.ToString()));
                i++;
            }

            rx.Append('$');
            return new Regex(rx.ToString(), RegexOptions.IgnoreCase);
        }
    }
}
