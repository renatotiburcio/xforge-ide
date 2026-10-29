using System.Globalization;
using System.Text;
using System.Text.Json;
using XForge.Ide.Core.Abstractions;

namespace XForge.Ide.Application.Services;

public sealed class VisualQueryEngine : IVisualQueryEngine
{
    private static readonly HashSet<string> FilterOperators =
        new(StringComparer.OrdinalIgnoreCase) { "=", "!=", ">", "<", ">=", "<=", "LIKE", "IS NULL", "IS NOT NULL" };

    private static readonly HashSet<string> JoinTypes =
        new(StringComparer.OrdinalIgnoreCase) { "INNER JOIN", "LEFT JOIN" };

    private static readonly HashSet<string> AggregateFunctions =
        new(StringComparer.OrdinalIgnoreCase) { "COUNT", "SUM", "AVG", "MIN", "MAX" };

    private enum SqlDialect
    {
        Sqlite,
        PostgreSql,
        MySql,
        SqlServer
    }

    public VisualQueryResult GenerateQuery(VisualQueryDesignModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(model.PrimaryTable))
        {
            throw new ArgumentException("Selecione a entidade principal da consulta.", nameof(model));
        }

        if (model.TopRows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(model), "O limite de linhas não pode ser negativo.");
        }

        var dialect = GetSqlDialect(model.DatabaseProvider);
        var joins = model.Joins.Select(join => ValidateJoin(model, join)).ToArray();
        ValidateProviderCompatibility(model, joins, dialect);
        var aliases = CreateTableAliases(model.PrimaryTable, joins);
        var filters = model.Filters.Select(filter => ValidateFilter(model, aliases, filter)).ToArray();
        var selectedFields = model.SelectedFields.Select(field => ValidateField(model, aliases, field.TableName, field.FieldName)).ToArray();
        var sorts = model.OrderBy.Select(sort => ValidateField(model, aliases, sort.TableName, sort.FieldName)).ToArray();
        var aggregates = model.SelectedFields.Select(field => field.AggregateFunction).ToArray();
        ValidateAggregates(model, aggregates);

        return new VisualQueryResult
        {
            SqlQuery = GenerateSql(model, joins, filters, selectedFields, sorts, dialect),
            LinqEfCoreQuery = GenerateLinq(model, joins, aliases, filters, selectedFields, sorts, aggregates),
            ClarionEquivalentView = GenerateClarionView(model, joins, selectedFields, filters)
        };
    }

    private static void ValidateProviderCompatibility(
        VisualQueryDesignModel model,
        IReadOnlyList<ValidatedJoin> joins,
        SqlDialect primaryDialect)
    {
        var includedTables = new[] { model.PrimaryTable }.Concat(joins.Select(join => join.RightTable));
        foreach (var table in includedTables)
        {
            if (model.TableProviders.TryGetValue(table, out var provider)
                && GetSqlDialect(provider) != primaryDialect)
            {
                throw new ArgumentException(
                    $"A entidade '{table}' usa o provedor '{provider}', incompatível com o provedor '{model.DatabaseProvider}' da consulta.",
                    nameof(model));
            }
        }
    }

    private static SqlDialect GetSqlDialect(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Informe um provedor SQL válido.", nameof(provider));
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            "sqlite" => SqlDialect.Sqlite,
            "postgresql" or "postgres" => SqlDialect.PostgreSql,
            "mysql" or "mariadb" => SqlDialect.MySql,
            "sqlserver" or "mssql" => SqlDialect.SqlServer,
            _ => throw new ArgumentException($"Provedor SQL não suportado: '{provider}'.", nameof(provider))
        };
    }

    private static ValidatedJoin ValidateJoin(VisualQueryDesignModel model, VisualQueryJoinModel join)
    {
        if (!JoinTypes.Contains(join.JoinType))
        {
            throw new ArgumentException($"Tipo de junção não suportado: '{join.JoinType}'.", nameof(model));
        }

        if (string.Equals(join.LeftTable, join.RightTable, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Uma junção precisa ligar entidades diferentes.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(join.LeftTable) || string.IsNullOrWhiteSpace(join.RightTable)
            || string.IsNullOrWhiteSpace(join.LeftField) || string.IsNullOrWhiteSpace(join.RightField))
        {
            throw new ArgumentException("Selecione as entidades e os campos de cada junção.", nameof(model));
        }

        return new ValidatedJoin(join.LeftTable, join.LeftField, join.RightTable, join.RightField, join.JoinType.ToUpperInvariant());
    }

    private static ValidatedFilter ValidateFilter(
        VisualQueryDesignModel model,
        IReadOnlyDictionary<string, string> aliases,
        VisualQueryFilterModel filter)
    {
        if (!FilterOperators.Contains(filter.Operator))
        {
            throw new ArgumentException($"Operador de filtro não suportado: '{filter.Operator}'.", nameof(model));
        }

        if (!string.Equals(filter.LogicalConnector, "AND", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(filter.LogicalConnector, "OR", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Conector lógico não suportado: '{filter.LogicalConnector}'.", nameof(model));
        }

        var field = ValidateField(model, aliases, filter.TableName, filter.FieldName);
        var op = filter.Operator.ToUpperInvariant();
        if (op == "LIKE" && !string.Equals(filter.ClrType, "string", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("O operador LIKE só pode ser usado com campos de texto.", nameof(model));
        }

        return new ValidatedFilter(filter.TableName, field.Name, op, filter.Value, filter.LogicalConnector, filter.ClrType);
    }

    private static ValidatedField ValidateField(
        VisualQueryDesignModel model,
        IReadOnlyDictionary<string, string> aliases,
        string tableName,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(tableName) || !aliases.ContainsKey(tableName))
        {
            throw new ArgumentException($"A entidade '{tableName}' não está incluída na consulta.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Selecione um campo para a consulta.", nameof(model));
        }

        return new ValidatedField(tableName, fieldName);
    }

    private static Dictionary<string, string> CreateTableAliases(string primaryTable, IReadOnlyList<ValidatedJoin> joins)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [primaryTable] = "e0"
        };

        foreach (var join in joins)
        {
            if (!aliases.ContainsKey(join.LeftTable))
            {
                throw new ArgumentException($"A entidade '{join.LeftTable}' precisa ser incluída antes de sua junção.");
            }

            if (aliases.ContainsKey(join.RightTable))
            {
                throw new ArgumentException($"A entidade '{join.RightTable}' já está incluída na consulta.");
            }

            aliases.Add(join.RightTable, $"e{aliases.Count}");
        }

        return aliases;
    }

    private static void ValidateAggregates(VisualQueryDesignModel model, IReadOnlyList<string?> aggregates)
    {
        if (aggregates.Count == 0 || aggregates.All(string.IsNullOrWhiteSpace))
        {
            return;
        }

        if (aggregates.Any(string.IsNullOrWhiteSpace) || aggregates.Any(value => !AggregateFunctions.Contains(value!)))
        {
            throw new ArgumentException("Selecione uma função de agregação válida para todos os campos da consulta.", nameof(model));
        }

        if (model.TopRows is > 0 || model.OrderBy.Count > 0)
        {
            throw new ArgumentException("Consultas agregadas não podem definir limite nem ordenação.", nameof(model));
        }

        for (var index = 0; index < aggregates.Count; index++)
        {
            if (aggregates[index] is "SUM" or "AVG" && !IsAggregateNumericType(model.SelectedFields[index].ClrType))
            {
                throw new ArgumentException(
                    $"A função {aggregates[index]} exige um campo numérico.",
                    nameof(model));
            }
        }
    }

    private static string GenerateSql(
        VisualQueryDesignModel model,
        IReadOnlyList<ValidatedJoin> joins,
        IReadOnlyList<ValidatedFilter> filters,
        IReadOnlyList<ValidatedField> selectedFields,
        IReadOnlyList<ValidatedField> sorts,
        SqlDialect dialect)
    {
        var aggregateFunctions = model.SelectedFields.Select(field => field.AggregateFunction).ToArray();
        var aliases = GetProjectionAliases(model.SelectedFields);
        var sql = new StringBuilder("SELECT ");
        if (dialect == SqlDialect.SqlServer && model.TopRows is > 0)
        {
            sql.Append("TOP (").Append(model.TopRows.Value).Append(") ");
        }

        if (selectedFields.Count == 0)
        {
            sql.Append(QuoteIdentifier(GetPhysicalTable(model, model.PrimaryTable), dialect)).Append(".*");
        }
        else
        {
            var projection = selectedFields.Select((field, index) =>
            {
                var name = SqlColumn(model, field.TableName, field.Name, dialect);
                var aggregate = aggregateFunctions[index];
                var expression = string.IsNullOrWhiteSpace(aggregate)
                    ? name
                    : $"{aggregate.ToUpperInvariant()}({name})";

                return $"{expression} AS {QuoteIdentifier(aliases[index], dialect)}";
            });

            sql.Append(string.Join(", ", projection));
        }

        sql.AppendLine();
        sql.Append("FROM ").Append(QuoteIdentifier(GetPhysicalTable(model, model.PrimaryTable), dialect));

        foreach (var join in joins)
        {
            sql.AppendLine();
            sql.Append(join.JoinType)
                .Append(' ')
                .Append(QuoteIdentifier(GetPhysicalTable(model, join.RightTable), dialect))
                .Append(" ON ")
                .Append(SqlColumn(model, join.LeftTable, join.LeftField, dialect))
                .Append(" = ")
                .Append(SqlColumn(model, join.RightTable, join.RightField, dialect));
        }

        if (filters.Count > 0)
        {
            sql.AppendLine();
            sql.Append("WHERE ").Append(BuildSqlFilterExpression(model, filters, dialect));
        }

        if (sorts.Count > 0)
        {
            sql.AppendLine();
            sql.Append("ORDER BY ")
                .Append(string.Join(", ", sorts.Select((sort, index) =>
                    $"{SqlColumn(model, sort.TableName, sort.Name, dialect)} {(model.OrderBy[index].Ascending ? "ASC" : "DESC")}")));
        }

        if (model.TopRows is > 0)
        {
            sql.AppendLine();
            if (dialect == SqlDialect.PostgreSql)
            {
                sql.Append("FETCH FIRST ").Append(model.TopRows.Value).Append(" ROWS ONLY");
            }
            else if (dialect is SqlDialect.Sqlite or SqlDialect.MySql)
            {
                sql.Append("LIMIT ").Append(model.TopRows.Value);
            }
        }

        return sql.ToString();
    }

    private static string GenerateLinq(
        VisualQueryDesignModel model,
        IReadOnlyList<ValidatedJoin> joins,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyList<ValidatedFilter> filters,
        IReadOnlyList<ValidatedField> selectedFields,
        IReadOnlyList<ValidatedField> sorts,
        IReadOnlyList<string?> aggregates)
    {
        var query = new StringBuilder("using Microsoft.EntityFrameworkCore;\nusing System.Globalization;\n\n");
        query.AppendLine("var query =");
        query.Append("    from ").Append(aliases[model.PrimaryTable])
            .Append(" in dbContext.").Append(CSharpIdentifier(model.PrimaryTable)).AppendLine(".AsNoTracking()");

        for (var index = 0; index < joins.Count; index++)
        {
            var join = joins[index];
            var alias = aliases[join.RightTable];
            var left = aliases[join.LeftTable];

            if (join.JoinType == "LEFT JOIN")
            {
                var groupAlias = $"joined{index}";
                query.Append("    join ").Append(alias).Append(" in dbContext.")
                    .Append(CSharpIdentifier(join.RightTable)).Append(".AsNoTracking() on ")
                    .Append(left).Append('.').Append(CSharpIdentifier(join.LeftField)).Append(" equals ")
                    .Append(alias).Append('.').Append(CSharpIdentifier(join.RightField))
                    .Append(" into ").Append(groupAlias).AppendLine();
                query.Append("    from ").Append(alias).Append(" in ").Append(groupAlias).AppendLine(".DefaultIfEmpty()");
            }
            else
            {
                query.Append("    join ").Append(alias).Append(" in dbContext.")
                    .Append(CSharpIdentifier(join.RightTable)).Append(".AsNoTracking() on ")
                    .Append(left).Append('.').Append(CSharpIdentifier(join.LeftField)).Append(" equals ")
                    .Append(alias).Append('.').Append(CSharpIdentifier(join.RightField)).AppendLine();
            }
        }

        if (filters.Count > 0)
        {
            query.Append("    where ").Append(BuildLinqFilterExpression(aliases, filters)).AppendLine();
        }

        if (sorts.Count > 0)
        {
            query.Append("    orderby ");
            for (var index = 0; index < sorts.Count; index++)
            {
                var sort = sorts[index];
                if (index > 0)
                {
                    query.Append(", ");
                }

                query.Append(aliases[sort.TableName]).Append('.').Append(CSharpIdentifier(sort.Name))
                    .Append(model.OrderBy[index].Ascending ? " ascending" : " descending");
            }

            query.AppendLine();
        }

        var projectionAliases = GetProjectionAliases(model.SelectedFields);
        if (selectedFields.Count == 0)
        {
            query.Append("    select ").Append(aliases[model.PrimaryTable]).AppendLine(";");
        }
        else
        {
            query.AppendLine("    select new");
            query.AppendLine("    {");
            for (var index = 0; index < selectedFields.Count; index++)
            {
                var field = selectedFields[index];
                query.Append("        ").Append(CSharpIdentifier(projectionAliases[index])).Append(" = ")
                    .Append(aliases[field.TableName]).Append('.').Append(CSharpIdentifier(field.Name))
                    .AppendLine(index == selectedFields.Count - 1 ? string.Empty : ",");
            }

            query.AppendLine("    };");
        }

        if (aggregates.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            query.AppendLine("var result = new");
            query.AppendLine("{");
            for (var index = 0; index < aggregates.Count; index++)
            {
                var aggregate = aggregates[index]!.ToUpperInvariant();
                var alias = CSharpIdentifier(projectionAliases[index]);
                var expression = aggregate switch
                {
                    "COUNT" => "await query.CountAsync()",
                    "SUM" => $"await query.SumAsync(x => x.{alias})",
                    "AVG" => $"await query.AverageAsync(x => x.{alias})",
                    "MIN" => $"await query.MinAsync(x => x.{alias})",
                    "MAX" => $"await query.MaxAsync(x => x.{alias})",
                    _ => throw new ArgumentException($"Função de agregação não suportada: '{aggregate}'.", nameof(model))
                };
                query.Append("    ").Append(alias).Append(" = ").Append(expression)
                    .AppendLine(index == aggregates.Count - 1 ? string.Empty : ",");
            }

            query.AppendLine("};");
        }
        else
        {
            if (model.TopRows is > 0)
            {
                query.Append("var results = await query.Take(").Append(model.TopRows.Value).AppendLine(").ToListAsync();");
            }
            else
            {
                query.AppendLine("var results = await query.ToListAsync();");
            }
        }

        return query.ToString().TrimEnd();
    }

    private static string GenerateClarionView(
        VisualQueryDesignModel model,
        IReadOnlyList<ValidatedJoin> joins,
        IReadOnlyList<ValidatedField> selectedFields,
        IReadOnlyList<ValidatedFilter> filters)
    {
        var view = new StringBuilder();
        view.Append(CSharpIdentifier(model.QueryName)).Append("View VIEW(")
            .Append(CSharpIdentifier(model.PrimaryTable)).AppendLine(")");

        foreach (var field in selectedFields)
        {
            view.Append("    PROJECT(").Append(CSharpIdentifier(field.TableName)).Append(':')
                .Append(CSharpIdentifier(field.Name)).AppendLine(")");
        }

        foreach (var join in joins)
        {
            view.Append("    JOIN(").Append(CSharpIdentifier(join.RightTable)).Append(':')
                .Append(CSharpIdentifier(join.RightField)).Append(", ")
                .Append(CSharpIdentifier(join.LeftTable)).Append(':').Append(CSharpIdentifier(join.LeftField)).AppendLine(")");
            view.AppendLine("    END");
        }

        if (filters.Count > 0)
        {
            view.Append("    FILTER(").Append(BuildClarionFilterExpression(filters)).AppendLine(")");
        }

        view.Append("END");
        return view.ToString();
    }

    private static string BuildSqlFilterExpression(
        VisualQueryDesignModel model,
        IReadOnlyList<ValidatedFilter> filters,
        SqlDialect dialect)
    {
        var expression = SqlFilter(model, filters[0], dialect);
        for (var index = 1; index < filters.Count; index++)
        {
            var filter = filters[index];
            expression = $"({expression} {filter.LogicalConnector.ToUpperInvariant()} {SqlFilter(model, filter, dialect)})";
        }

        return expression;
    }

    private static string SqlFilter(VisualQueryDesignModel model, ValidatedFilter filter, SqlDialect dialect)
    {
        var column = SqlColumn(model, filter.TableName, filter.FieldName, dialect);
        if (filter.Operator is "IS NULL" or "IS NOT NULL")
        {
            return $"{column} {filter.Operator}";
        }

        return $"{column} {filter.Operator} {SqlLiteral(filter.Value, filter.ClrType, dialect)}";
    }

    private static string BuildLinqFilterExpression(
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyList<ValidatedFilter> filters)
    {
        var expression = LinqFilter(aliases, filters[0]);
        for (var index = 1; index < filters.Count; index++)
        {
            var filter = filters[index];
            expression = $"({expression} {filter.LogicalConnector.ToLowerInvariant()} {LinqFilter(aliases, filter)})";
        }

        return expression;
    }

    private static string LinqFilter(IReadOnlyDictionary<string, string> aliases, ValidatedFilter filter)
    {
        var field = $"{aliases[filter.TableName]}.{CSharpIdentifier(filter.FieldName)}";
        if (filter.Operator == "IS NULL")
        {
            return $"{field} == null";
        }

        if (filter.Operator == "IS NOT NULL")
        {
            return $"{field} != null";
        }

        if (filter.Operator == "LIKE")
        {
            return $"EF.Functions.Like({field}, {JsonSerializer.Serialize(filter.Value)})";
        }

        var op = filter.Operator == "=" ? "==" : filter.Operator == "!=" ? "!=" : filter.Operator;
        return $"{field} {op} {CSharpLiteral(filter.Value, filter.ClrType)}";
    }

    private static string BuildClarionFilterExpression(IReadOnlyList<ValidatedFilter> filters)
    {
        var expression = ClarionFilter(filters[0]);
        for (var index = 1; index < filters.Count; index++)
        {
            var filter = filters[index];
            expression = $"({expression} {filter.LogicalConnector.ToUpperInvariant()} {ClarionFilter(filter)})";
        }

        return expression;
    }

    private static string ClarionFilter(ValidatedFilter filter)
    {
        if (filter.Operator is "IS NULL" or "IS NOT NULL")
        {
            return $"{CSharpIdentifier(filter.TableName)}:{CSharpIdentifier(filter.FieldName)} {filter.Operator}";
        }

        return $"{CSharpIdentifier(filter.TableName)}:{CSharpIdentifier(filter.FieldName)} {filter.Operator} {JsonSerializer.Serialize(filter.Value)}";
    }

    private static string[] GetProjectionAliases(IReadOnlyList<VisualQueryFieldModel> fields)
    {
        var aliases = new string[fields.Count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var preferred = string.IsNullOrWhiteSpace(field.Alias) ? field.FieldName : field.Alias;
            var identifier = CSharpIdentifier(preferred);
            if (!used.Add(identifier))
            {
                preferred = $"{field.TableName}{field.FieldName}";
                var unique = preferred;
                var suffix = 2;
                while (!used.Add(CSharpIdentifier(unique)))
                {
                    unique = $"{preferred}{suffix++}";
                }

                preferred = unique;
            }

            aliases[index] = preferred;
        }

        return aliases;
    }

    private static string SqlColumn(VisualQueryDesignModel model, string tableName, string fieldName, SqlDialect dialect) =>
        $"{QuoteIdentifier(GetPhysicalTable(model, tableName), dialect)}.{QuoteIdentifier(GetPhysicalField(model, tableName, fieldName), dialect)}";

    private static string GetPhysicalTable(VisualQueryDesignModel model, string tableName) =>
        model.PhysicalTableNames.TryGetValue(tableName, out var physicalName) && !string.IsNullOrWhiteSpace(physicalName)
            ? physicalName
            : tableName;

    private static string GetPhysicalField(VisualQueryDesignModel model, string tableName, string fieldName) =>
        model.PhysicalFieldNames.TryGetValue(tableName, out var fields)
        && fields.TryGetValue(fieldName, out var physicalName)
        && !string.IsNullOrWhiteSpace(physicalName)
            ? physicalName
            : fieldName;

    private static string QuoteIdentifier(string value, SqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identificadores SQL não podem ser vazios.", nameof(value));
        }

        return dialect switch
        {
            SqlDialect.SqlServer => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]",
            SqlDialect.MySql => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`",
            _ => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        };
    }

    private static string SqlLiteral(string value, string clrType, SqlDialect dialect)
    {
        var type = NormalizeClrType(clrType);
        if (IsNumericType(type))
        {
            return NumericLiteral(value, type, suffix: false);
        }

        if (type is "bool" or "boolean")
        {
            return bool.TryParse(value, out var boolean)
                ? dialect == SqlDialect.PostgreSql
                    ? boolean ? "TRUE" : "FALSE"
                    : boolean ? "1" : "0"
                : throw new ArgumentException($"'{value}' não é um valor booleano válido.", nameof(value));
        }

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static string CSharpLiteral(string value, string clrType)
    {
        var type = NormalizeClrType(clrType);
        if (type == "string")
        {
            return JsonSerializer.Serialize(value);
        }

        if (type is "bool" or "boolean")
        {
            return bool.TryParse(value, out var boolean)
                ? boolean ? "true" : "false"
                : throw new ArgumentException($"'{value}' não é um valor booleano válido.", nameof(value));
        }

        if (type == "guid")
        {
            return $"Guid.Parse({JsonSerializer.Serialize(value)})";
        }

        if (type == "dateonly")
        {
            return $"DateOnly.Parse({JsonSerializer.Serialize(value)}, CultureInfo.InvariantCulture)";
        }

        if (type == "timeonly")
        {
            return $"TimeOnly.Parse({JsonSerializer.Serialize(value)}, CultureInfo.InvariantCulture)";
        }

        if (type == "datetime")
        {
            return $"DateTime.Parse({JsonSerializer.Serialize(value)}, CultureInfo.InvariantCulture)";
        }

        if (IsNumericType(type))
        {
            return NumericLiteral(value, type, suffix: true);
        }

        throw new ArgumentException($"Tipo CLR não suportado para filtro: '{clrType}'.", nameof(clrType));
    }

    private static string NormalizeClrType(string clrType)
    {
        var type = clrType.Trim().ToLowerInvariant();
        return type.StartsWith("system.", StringComparison.Ordinal) ? type["system.".Length..] : type;
    }

    private static bool IsNumericType(string type) =>
        type is "byte" or "sbyte" or "short" or "int16" or "ushort" or "uint16"
            or "int" or "int32" or "uint" or "uint32" or "long" or "int64" or "ulong" or "uint64"
            or "float" or "single" or "double" or "decimal";

    private static bool IsAggregateNumericType(string clrType)
    {
        var type = NormalizeClrType(clrType);
        return type is "int" or "int32" or "long" or "int64" or "float" or "single" or "double" or "decimal";
    }

    private static string NumericLiteral(string value, string type, bool suffix)
    {
        var integerStyle = NumberStyles.Integer;
        var culture = CultureInfo.InvariantCulture;

        return type switch
        {
            "byte" when byte.TryParse(value, integerStyle, culture, out var number) => number.ToString(culture),
            "sbyte" when sbyte.TryParse(value, integerStyle, culture, out var number) => number.ToString(culture),
            "short" or "int16" when short.TryParse(value, integerStyle, culture, out var number) => number.ToString(culture),
            "ushort" or "uint16" when ushort.TryParse(value, integerStyle, culture, out var number) => number.ToString(culture),
            "int" or "int32" when int.TryParse(value, integerStyle, culture, out var number) => number.ToString(culture),
            "uint" or "uint32" when uint.TryParse(value, integerStyle, culture, out var number) => number.ToString(culture),
            "long" or "int64" when long.TryParse(value, integerStyle, culture, out var number) => number.ToString(culture),
            "ulong" or "uint64" when ulong.TryParse(value, integerStyle, culture, out var number) => number.ToString(culture),
            "float" or "single" when float.TryParse(value, NumberStyles.Float, culture, out var number) && float.IsFinite(number) =>
                number.ToString("R", culture) + (suffix ? "f" : string.Empty),
            "double" when double.TryParse(value, NumberStyles.Float, culture, out var number) && double.IsFinite(number) =>
                number.ToString("R", culture) + (suffix ? "d" : string.Empty),
            "decimal" when decimal.TryParse(value, NumberStyles.Float, culture, out var number) =>
                number.ToString(culture) + (suffix ? "m" : string.Empty),
            _ => throw new ArgumentException($"'{value}' não é um valor válido para o tipo {type}.", nameof(value))
        };
    }

    private static string CSharpIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identificadores C# não podem ser vazios.", nameof(value));
        }

        var identifier = new string(value.Select((character, index) =>
            (index == 0 ? char.IsLetter(character) || character == '_' : char.IsLetterOrDigit(character) || character == '_')
                ? character
                : '_').ToArray());

        if (char.IsDigit(identifier[0]))
        {
            identifier = $"_{identifier}";
        }

        return CSharpKeywords.Contains(identifier) ? $"@{identifier}" : identifier;
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "record", "required", "file", "scoped"
    };

    private sealed record ValidatedField(string TableName, string Name);

    private sealed record ValidatedJoin(
        string LeftTable,
        string LeftField,
        string RightTable,
        string RightField,
        string JoinType);

    private sealed record ValidatedFilter(
        string TableName,
        string FieldName,
        string Operator,
        string Value,
        string LogicalConnector,
        string ClrType);
}
