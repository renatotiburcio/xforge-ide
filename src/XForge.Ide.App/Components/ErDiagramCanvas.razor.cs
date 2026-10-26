using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using XForge.Ide.Application.Dto;

namespace XForge.Ide.App.Components;

/// <summary>
/// Canvas visual do Diagrama ER: nós (entidades) posicionados em layout de
/// camadas determinístico e arestas SVG (bezier + notação pé de galinha) com a
/// cardinalidade rotulada na ponta.
/// <para>
/// O arrasto e o pan/zoom são feitos em JS puro (er-diagram.js): durante o
/// arrasto nenhum re-render do Blazor ocorre — só a posição final volta para o
/// C#, que persiste em localStorage por dicionário.
/// </para>
/// </summary>
public sealed partial class ErDiagramCanvas : ComponentBase, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    // Caminho do módulo ES servido como static web asset da assembly XForge.Ide.App.
    private const string ModulePath = "/_content/XForge.Ide.App/js/er-diagram.js";

    // Métricas do auto-layout (espaçado, estilo dbdiagram/pgAdmin).
    private const double NodeWidth = 236d;
    private const double ColumnGap = 108d;
    private const double RowGap = 30d;
    private const double CanvasPadding = 56d;
    private const int MaxFieldsInNode = 14;

    private IJSObjectReference? _module;
    private DotNetObjectReference<ErDiagramCanvas>? _self;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly string _viewportId = "er-canvas-" + Guid.NewGuid().ToString("N")[..10];
    private bool _attached;
    private bool _modelDirty;
    private bool _dragging;

    private ErDiagramDto? _applied;
    private Guid? _appliedDictionary;
    private List<NodeView> _nodes = new();
    private Dictionary<string, (double X, double Y)> _saved = new(StringComparer.OrdinalIgnoreCase);
    private double _worldWidth = 800;
    private double _worldHeight = 600;
    private bool _autoFit = true;

    /// <summary>Dicionário dono do layout (chave do localStorage).</summary>
    [Parameter] public Guid? DictionaryId { get; set; }

    /// <summary>Dados do diagrama (contrato estável: <see cref="ErDiagramDto"/>).</summary>
    [Parameter] public ErDiagramDto? Diagram { get; set; }

    /// <summary>Disparado quando a seleção (nó ou aresta) muda.</summary>
    [Parameter] public EventCallback<ErDiagramSelection> SelectionChanged { get; set; }

    /// <summary>Disparado quando o zoom percentual muda (scroll ou botões).</summary>
    [Parameter] public EventCallback<int> ZoomPercentChanged { get; set; }

    /// <summary>Seleção atual, para destaque no restante da página.</summary>
    public ErDiagramSelection Selection { get; private set; } = ErDiagramSelection.Empty;

    // ------------------------------------------------------------- apresentação

    /// <summary>Formata uma coordenada em px (invariante, sem decimais).</summary>
    private static string Px(double value) => Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + "px";

    private static RenderFragment Glyph(string name) => builder =>
        builder.AddMarkupContent(0, XForge.DesignSystem.Components.Icon.XFSvgIcons.Get(name));

    private static RenderFragment TableGlyph => Glyph("table-cells");

    private static RenderFragment KeyGlyph => Glyph("key");

    private static RenderFragment LinkGlyph => Glyph("link");

    // ------------------------------------------------------------------ ciclo

    protected override async Task OnParametersSetAsync()
    {
        if (ReferenceEquals(Diagram, _applied) && DictionaryId == _appliedDictionary)
        {
            return;
        }

        _applied = Diagram;
        _appliedDictionary = DictionaryId;
        await RebuildAsync();
    }

    /// <summary>
    /// Durante o arrasto de um nó o Blazor NÃO re-renderiza: o transform é
    /// controlado pelo JS e a posição final volta pelo callback
    /// <see cref="OnNodeMoved"/>. Sem isso o nó "voltaria" ao valor do C#.
    /// </summary>
    protected override bool ShouldRender() => !_dragging;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_attached)
        {
            _self = DotNetObjectReference.Create(this);
            var module = await EnsureModuleAsync();
            if (module is not null)
            {
                // Devolve o tamanho visível (o JS guarda; o encaixe é feito abaixo).
                await module.InvokeAsync<ViewportSize?>("attach", _viewportId, _self);
                _attached = true;
            }
        }

        if (!_attached || !_modelDirty)
        {
            return;
        }

        _modelDirty = false;
        await PushModelAsync();

        if (_autoFit)
        {
            await FitAsync();
        }
    }

    private async Task<IJSObjectReference?> EnsureModuleAsync()
    {
        if (_module is not null)
        {
            return _module;
        }

        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }
        catch (JSDisconnectedException)
        {
            _module = null;
        }
        catch (InvalidOperationException)
        {
            _module = null;
        }

        return _module;
    }

    /// <summary>Reconstrói o layout (posições salvas + auto-layout) e re-renderiza.</summary>
    private async Task RebuildAsync()
    {
        var nodes = Diagram?.Nodes ?? Array.Empty<ErNodeDto>();
        var edges = Diagram?.Edges ?? Array.Empty<ErEdgeDto>();

        _saved = await LoadPositionsAsync();
        var laid = AutoLayout(nodes, edges);
        var entityNames = new HashSet<string>(nodes.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);

        _nodes = nodes
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(n => new NodeView(
                n,
                _saved.TryGetValue(n.Name, out var sp) ? sp.X : laid.GetValueOrDefault(n.Name).X,
                _saved.TryGetValue(n.Name, out var spy) ? spy.Y : laid.GetValueOrDefault(n.Name).Y,
                BuildFields(n, entityNames)))
            .ToList();

        _worldWidth = Math.Max(
            600,
            _nodes.Count == 0 ? 0 : _nodes.Max(n => n.X) + NodeWidth + CanvasPadding);
        _worldHeight = Math.Max(
            480,
            _nodes.Count == 0 ? 0 : _nodes.Max(n => n.Y + EstimateHeight(n)) + CanvasPadding);

        _modelDirty = true;
        StateHasChanged();
    }

    private static List<FieldView> BuildFields(ErNodeDto node, HashSet<string> entityNames)
    {
        var result = new List<FieldView>(node.Fields.Count);
        foreach (var f in node.Fields)
        {
            var own = string.Equals(f.Name, node.Name + "Id", StringComparison.OrdinalIgnoreCase);
            var fk = !f.PrimaryKey && !own
                     && f.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                     && f.Name.Length > 2
                     && entityNames.Contains(f.Name[..^2]);
            result.Add(new FieldView(f.Name, f.ClrType, f.PrimaryKey, fk, f.Required));
        }

        return result;
    }

    /// <summary>
    /// Auto-layout determinístico: camadas (colunas) pela profundidade de
    /// relacionamento e empilhamento por nome dentro de cada coluna.
    /// </summary>
    private static Dictionary<string, (double X, double Y)> AutoLayout(
        IReadOnlyList<ErNodeDto> nodes, IReadOnlyList<ErEdgeDto> edges)
    {
        var unique = nodes
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var depth = unique.ToDictionary(n => n.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var links = edges
            .Where(e => depth.ContainsKey(e.From) && depth.ContainsKey(e.To)
                        && !string.Equals(e.From, e.To, StringComparison.OrdinalIgnoreCase))
            .Select(e => (From: e.From, To: e.To))
            .ToList();

        // Relaxação (Bellman-Ford) limitada ao número de nós: um DAG converge
        // rapidamente e ciclos ficam estáveis dentro do limite.
        for (var i = 0; i < unique.Count; i++)
        {
            var changed = false;
            foreach (var (from, to) in links)
            {
                if (depth[from] + 1 > depth[to])
                {
                    depth[to] = depth[from] + 1;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var columns = new List<List<string>>();
        foreach (var group in unique.GroupBy(n => depth[n.Name]).OrderBy(g => g.Key))
        {
            while (columns.Count <= group.Key)
            {
                columns.Add(new List<string>());
            }

            columns[group.Key].AddRange(
                group.Select(n => n.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        var byName = unique.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        var x = CanvasPadding;
        foreach (var column in columns)
        {
            var y = CanvasPadding;
            foreach (var name in column)
            {
                positions[name] = (x, y);
                // Altura estimada pelo número de campos (o JS mede a real depois).
                var fields = Math.Max(1, byName[name].Fields.Count);
                y += Math.Min(300d, 52d + fields * 24d) + RowGap;
            }

            x += NodeWidth + ColumnGap;
        }

        return positions;
    }

    private static double EstimateHeight(NodeView node)
    {
        var count = Math.Max(1, Math.Min(node.Fields.Count, MaxFieldsInNode));
        return Math.Min(320d, 52d + count * 24d + 10d);
    }

    // --------------------------------------------------------------- modelo JS

    private string BuildModelJson()
    {
        var model = new CanvasModel(
            _nodes.Select(n => new CanvasNode(
                n.Name, n.X, n.Y, NodeWidth,
                n.Name, n.Source.TableName, n.Fields.Count,
                n.Fields.Select(f => new CanvasField(f.Name, f.ClrType, f.PrimaryKey, f.ForeignKey, f.Required))
                    .ToArray()))
                .ToArray(),
            _nodes.Count == 0 ? Array.Empty<CanvasEdge>() : BuildEdges().ToArray());

        return JsonSerializer.Serialize(model, Json);
    }

    private IEnumerable<CanvasEdge> BuildEdges()
    {
        var edges = Diagram?.Edges ?? Array.Empty<ErEdgeDto>();
        foreach (var e in edges)
        {
            // Auto-relação e aresta sem ponta válida não têm como ser desenhadas.
            if (string.IsNullOrEmpty(e.From) || string.IsNullOrEmpty(e.To)
                || string.Equals(e.From, e.To, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (from, to) = SplitCardinality(e.Cardinality);
            yield return new CanvasEdge(e.Id.ToString("N"), e.From, e.To, from, to, Tone(e.Cardinality));
        }
    }

    private static (string From, string To) SplitCardinality(string? cardinality)
    {
        var parts = (cardinality ?? string.Empty).Split(':', 2);
        if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
        {
            return (parts[0].Trim(), parts[1].Trim());
        }

        var single = parts.Length > 0 ? parts[0].Trim() : "1";
        return string.IsNullOrEmpty(single) ? ("1", "N") : (single, single);
    }

    private static string Tone(string? cardinality) => (cardinality ?? string.Empty).Trim() switch
    {
        "1:1" => "one",
        "N:N" => "multi",
        _ => "many"
    };

    private async Task PushModelAsync()
    {
        var module = await EnsureModuleAsync();
        if (module is null || !_attached)
        {
            return;
        }

        await module.InvokeVoidAsync("applyModel", _viewportId, BuildModelJson());
        await module.InvokeVoidAsync("setSelection", _viewportId, SelectionJson());
    }

    private string SelectionJson()
    {
        var payload = new
        {
            node = Selection.NodeName,
            edge = Selection.EdgeId?.ToString("N"),
            relatedNodes = Selection.RelatedNodeNames.ToArray(),
            relatedEdges = Selection.RelatedEdgeIds.ToArray()
        };
        return JsonSerializer.Serialize(payload, Json);
    }

    // -------------------------------------------------------------- interação

    /// <summary>Aproxima o zoom.</summary>
    public Task ZoomInAsync() => ZoomByAsync(1.2d);

    /// <summary>Afasta o zoom.</summary>
    public Task ZoomOutAsync() => ZoomByAsync(1d / 1.2d);

    private async Task ZoomByAsync(double factor)
    {
        var module = await EnsureModuleAsync();
        if (module is null || !_attached)
        {
            return;
        }

        _autoFit = false;
        await module.InvokeVoidAsync("zoomBy", _viewportId, factor);
    }

    /// <summary>Ajusta o diagrama inteiro à área visível.</summary>
    public async Task FitAsync()
    {
        var module = await EnsureModuleAsync();
        if (module is null || !_attached)
        {
            return;
        }

        _autoFit = true;
        await module.InvokeVoidAsync("fit", _viewportId, 24);
    }

    /// <summary>Volta o zoom para 100% sem translação.</summary>
    public async Task ResetZoomAsync()
    {
        var module = await EnsureModuleAsync();
        if (module is null || !_attached)
        {
            return;
        }

        _autoFit = false;
        await module.InvokeVoidAsync("resetView", _viewportId);
    }

    /// <summary>Descarta as posições salvas e refaz o auto-layout.</summary>
    public async Task ResetLayoutAsync()
    {
        if (DictionaryId is not null)
        {
            _saved = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
            await SavePositionsAsync();
        }

        // O encaixe acontece depois do render, em OnAfterRenderAsync.
        _autoFit = true;
        await RebuildAsync();
    }

    /// <summary>Seleciona um nó pelo nome da entidade (null limpa a seleção).</summary>
    public Task SelectNodeByNameAsync(string? nodeName) => SelectNodeAsync(nodeName);

    /// <summary>Seleciona uma aresta pelo id do relacionamento.</summary>
    public Task SelectEdgeByIdAsync(Guid edgeId) => SelectEdgeAsync(edgeId.ToString("N"));

    // --------------------------------------------------- callbacks vindos do JS

    /// <summary>Chamado pelo JS ao terminar de arrastar um nó.</summary>
    [JSInvokable]
    public async Task OnNodeMoved(string nodeName, double x, double y)
    {
        var node = _nodes.FirstOrDefault(n => string.Equals(n.Name, nodeName, StringComparison.OrdinalIgnoreCase));
        if (node is not null)
        {
            node.X = x;
            node.Y = y;
        }

        _saved[nodeName] = (x, y);
        await SavePositionsAsync();
    }

    /// <summary>Chamado pelo JS ao clicar num nó (sem arrastar).</summary>
    [JSInvokable]
    public Task OnNodeSelected(string nodeName) => SelectNodeAsync(nodeName);

    /// <summary>Chamado pelo JS ao clicar numa aresta.</summary>
    [JSInvokable]
    public Task OnEdgeSelected(string edgeId) => SelectEdgeAsync(edgeId);

    /// <summary>Chamado pelo JS ao clicar no fundo (limpa a seleção).</summary>
    [JSInvokable]
    public Task OnBackgroundSelected() => SelectNodeAsync(null);

    /// <summary>Chamado pelo JS quando o zoom muda (scroll ou botões).</summary>
    [JSInvokable]
    public async Task OnZoomChanged(int percent)
    {
        await ZoomPercentChanged.InvokeAsync(percent);
    }

    /// <summary>Chamado pelo JS quando o usuário começa a arrastar um nó.</summary>
    [JSInvokable]
    public Task OnNodeDragStarted()
    {
        _dragging = true;
        return Task.CompletedTask;
    }

    /// <summary>Chamado pelo JS quando o arrasto de um nó termina.</summary>
    [JSInvokable]
    public Task OnNodeDragEnded()
    {
        _dragging = false;
        return Task.CompletedTask;
    }

    /// <summary>Chamado pelo JS quando o usuário arrasta o fundo.</summary>
    [JSInvokable]
    public Task OnViewMoved()
    {
        _autoFit = false;
        return Task.CompletedTask;
    }

    /// <summary>Chamado pelo JS quando a área visível muda de tamanho.</summary>
    [JSInvokable]
    public Task OnViewportResized(double width, double height)
    {
        _ = width;
        _ = height;
        return Task.CompletedTask;
    }

    private async Task SelectNodeAsync(string? nodeName)
    {
        var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edgeIds = new List<string>();
        foreach (var e in Diagram?.Edges ?? Array.Empty<ErEdgeDto>())
        {
            if (string.Equals(e.From, nodeName, StringComparison.OrdinalIgnoreCase))
            {
                related.Add(e.To);
                edgeIds.Add(e.Id.ToString("N"));
            }
            else if (string.Equals(e.To, nodeName, StringComparison.OrdinalIgnoreCase))
            {
                related.Add(e.From);
                edgeIds.Add(e.Id.ToString("N"));
            }
        }

        Selection = nodeName is null
            ? ErDiagramSelection.Empty
            : new ErDiagramSelection(nodeName, null, nodeName, related, edgeIds);

        await PushSelectionAsync();
        await SelectionChanged.InvokeAsync(Selection);
    }

    private async Task SelectEdgeAsync(string? edgeId)
    {
        if (!Guid.TryParseExact(edgeId ?? string.Empty, "N", out var id))
        {
            await SelectNodeAsync(null);
            return;
        }

        var edge = (Diagram?.Edges ?? Array.Empty<ErEdgeDto>()).FirstOrDefault(e => e.Id == id);
        if (edge is null)
        {
            await SelectNodeAsync(null);
            return;
        }

        Selection = new ErDiagramSelection(
            edge.From,
            edge.Id,
            $"{edge.From} {edge.Cardinality} {edge.To}",
            new[] { edge.From, edge.To },
            new[] { edge.Id.ToString("N") });

        await PushSelectionAsync();
        await SelectionChanged.InvokeAsync(Selection);
    }

    private async Task PushSelectionAsync()
    {
        var module = await EnsureModuleAsync();
        if (module is null || !_attached)
        {
            return;
        }

        await module.InvokeVoidAsync("setSelection", _viewportId, SelectionJson());
    }

    // ------------------------------------------------------------ persistência

    private static string StorageKey(Guid dictionaryId) => $"xf-er-layout-{dictionaryId:N}";

    private async Task SavePositionsAsync()
    {
        if (DictionaryId is not { } id)
        {
            return;
        }

        try
        {
            var payload = _saved.ToDictionary(
                kv => kv.Key,
                kv => new ErSavedPoint(kv.Value.X, kv.Value.Y),
                StringComparer.Ordinal);
            await JS.InvokeVoidAsync("localStorage.setItem", StorageKey(id), JsonSerializer.Serialize(payload, Json));
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task<Dictionary<string, (double X, double Y)>> LoadPositionsAsync()
    {
        var empty = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
        if (DictionaryId is not { } id)
        {
            return empty;
        }

        try
        {
            var json = await JS.InvokeAsync<string?>("localStorage.getItem", StorageKey(id));
            if (string.IsNullOrWhiteSpace(json))
            {
                return empty;
            }

            var raw = JsonSerializer.Deserialize<Dictionary<string, ErSavedPoint>>(json, Json);
            if (raw is null)
            {
                return empty;
            }

            var result = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, p) in raw)
            {
                result[name] = (p.X, p.Y);
            }

            return result;
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (JsonException)
        {
        }

        return empty;
    }

    public async ValueTask DisposeAsync()
    {
        if (_attached && _module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose", _viewportId);
            }
            catch (JSDisconnectedException)
            {
            }
        }

        try
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
    }

    // ------------------------------------------------------------------ tipos

    private sealed record ErSavedPoint(double X, double Y);

    private sealed record ViewportSize(double Width, double Height);

    private sealed record FieldView(string Name, string ClrType, bool PrimaryKey, bool ForeignKey, bool Required);

    private sealed class NodeView
    {
        public NodeView(ErNodeDto source, double x, double y, List<FieldView> fields)
        {
            Source = source;
            X = x;
            Y = y;
            Fields = fields;
        }

        public ErNodeDto Source { get; }

        public string Name => Source.Name;

        public double X { get; set; }

        public double Y { get; set; }

        public List<FieldView> Fields { get; }
    }

    private sealed record CanvasNode(
        string Id, double X, double Y, double W,
        string Name, string Table, int Count, CanvasField[] Fields);

    private sealed record CanvasField(string Name, string ClrType, bool Pk, bool Fk, bool Req);

    private sealed record CanvasEdge(
        string Id, string From, string To, string FromCard, string ToCard, string Tone);

    private sealed record CanvasModel(CanvasNode[] Nodes, CanvasEdge[] Edges);
}

/// <summary>Seleção corrente do diagrama (nó ou aresta) e seus relacionados.</summary>
public sealed record ErDiagramSelection(
    string? NodeName,
    Guid? EdgeId,
    string Label,
    IReadOnlyCollection<string> RelatedNodeNames,
    IReadOnlyCollection<string> RelatedEdgeIds)
{
    public static ErDiagramSelection Empty { get; } = new(null, null, string.Empty,
        Array.Empty<string>(), Array.Empty<string>());
}
