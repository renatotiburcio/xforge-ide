namespace XForge.Ide.Core.Abstractions;

/// <summary>
/// Representa a definição de uma consulta visual gerada por arrastar/soltar tabelas e campos do DCT.
/// </summary>
public sealed class VisualQueryDesignModel
{
    public string QueryName { get; set; } = "NovaConsulta";
    public string PrimaryTable { get; set; } = string.Empty;
    public string DatabaseProvider { get; set; } = "Sqlite";
    public Dictionary<string, string> PhysicalTableNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> PhysicalFieldNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> TableProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<VisualQueryFieldModel> SelectedFields { get; set; } = new();
    public List<VisualQueryJoinModel> Joins { get; set; } = new();
    public List<VisualQueryFilterModel> Filters { get; set; } = new();
    public List<VisualQuerySortModel> OrderBy { get; set; } = new();
    public int? TopRows { get; set; }
}

public sealed class VisualQueryFieldModel
{
    public string TableName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string ClrType { get; set; } = "string";
    public string? AggregateFunction { get; set; }
}

public sealed class VisualQueryJoinModel
{
    public string LeftTable { get; set; } = string.Empty;
    public string LeftField { get; set; } = string.Empty;
    public string RightTable { get; set; } = string.Empty;
    public string RightField { get; set; } = string.Empty;
    public string JoinType { get; set; } = "INNER JOIN";
}

public sealed class VisualQueryFilterModel
{
    public string TableName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Operator { get; set; } = "=";
    public string Value { get; set; } = string.Empty;
    public string LogicalConnector { get; set; } = "AND";
    public string ClrType { get; set; } = "string";
}

public sealed class VisualQuerySortModel
{
    public string TableName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public bool Ascending { get; set; } = true;
}

public sealed class VisualQueryResult
{
    public string SqlQuery { get; set; } = string.Empty;
    public string LinqEfCoreQuery { get; set; } = string.Empty;
    public string ClarionEquivalentView { get; set; } = string.Empty;
}

public interface IVisualQueryEngine
{
    VisualQueryResult GenerateQuery(VisualQueryDesignModel model);
}
