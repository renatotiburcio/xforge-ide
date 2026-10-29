using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using XForge.Ide.Application.Services;
using XForge.Ide.Core.Abstractions;

namespace XForge.Ide.Core.Tests;

public sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XFORGE_TEST_POSTGRESQL")))
        {
            Skip = "Set XFORGE_TEST_POSTGRESQL to run PostgreSQL integration tests.";
        }
    }
}

public sealed class MySqlIntegrationFactAttribute : FactAttribute
{
    public MySqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XFORGE_TEST_MYSQL")))
        {
            Skip = "Set XFORGE_TEST_MYSQL to run MySQL integration tests.";
        }
    }
}

public sealed class SqlServerIntegrationFactAttribute : FactAttribute
{
    public SqlServerIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XFORGE_TEST_SQLSERVER")))
        {
            Skip = "Set XFORGE_TEST_SQLSERVER to run SQL Server integration tests.";
        }
    }
}

public sealed class VisualQueryDatabaseIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public Task GeneratedQuery_ExecutesAgainstPostgreSql() =>
        ExecuteGeneratedQueryAsync(
            "PostgreSql",
            "XFORGE_TEST_POSTGRESQL",
            connectionString => new NpgsqlConnection(connectionString));

    [MySqlIntegrationFact]
    public Task GeneratedQuery_ExecutesAgainstMySql() =>
        ExecuteGeneratedQueryAsync(
            "MySql",
            "XFORGE_TEST_MYSQL",
            connectionString => new MySqlConnection(connectionString));

    [SqlServerIntegrationFact]
    public Task GeneratedQuery_ExecutesAgainstSqlServer() =>
        ExecuteGeneratedQueryAsync(
            "SqlServer",
            "XFORGE_TEST_SQLSERVER",
            connectionString => new SqlConnection(connectionString));

    private static async Task ExecuteGeneratedQueryAsync(
        string provider,
        string connectionStringVariable,
        Func<string, DbConnection> createConnection)
    {
        var connectionString = Environment.GetEnvironmentVariable(connectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string environment variable '{connectionStringVariable}' is required.");
        }

        await using var connection = createConnection(connectionString);
        await OpenWhenReadyAsync(connection);

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var tablePrefix = provider == "SqlServer" ? "#" : string.Empty;
        var customersTable = $"{tablePrefix}Customers_{suffix}";
        var ordersTable = $"{tablePrefix}Orders_{suffix}";
        var quote = provider switch
        {
            "MySql" => "`",
            "SqlServer" => "[",
            _ => "\""
        };
        var closeQuote = provider switch
        {
            "MySql" => "`",
            "SqlServer" => "]",
            _ => "\""
        };

        string Quote(string identifier) =>
            $"{quote}{identifier.Replace(closeQuote, closeQuote + closeQuote, StringComparison.Ordinal)}{closeQuote}";

        var textType = provider == "SqlServer" ? "NVARCHAR(200)" : provider == "MySql" ? "VARCHAR(200)" : "TEXT";
        var createTemporaryTable = provider == "SqlServer" ? "CREATE TABLE" : "CREATE TEMPORARY TABLE";

        await ExecuteNonQueryAsync(connection, $"""
            {createTemporaryTable} {Quote(customersTable)} (
                {Quote("Id")} INTEGER PRIMARY KEY,
                {Quote("Display Name")} {textType} NOT NULL
            )
            """);
        await ExecuteNonQueryAsync(connection, $"""
            {createTemporaryTable} {Quote(ordersTable)} (
                {Quote("Id")} INTEGER PRIMARY KEY,
                {Quote("Customer Id")} INTEGER NOT NULL,
                {Quote("Order Code")} {textType} NOT NULL
            )
            """);

        await InsertCustomerAsync(connection, Quote(customersTable), Quote, 1, "O'Brien");
        await InsertCustomerAsync(connection, Quote(customersTable), Quote, 2, "O'Brien");
        await InsertCustomerAsync(connection, Quote(customersTable), Quote, 3, "Other");
        await InsertOrderAsync(connection, Quote(ordersTable), Quote, 11, 1, "A");
        await InsertOrderAsync(connection, Quote(ordersTable), Quote, 12, 1, "B");
        await InsertOrderAsync(connection, Quote(ordersTable), Quote, 13, 3, "C");

        var model = new VisualQueryDesignModel
        {
            DatabaseProvider = provider,
            PrimaryTable = "Customer",
            PhysicalTableNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Customer"] = customersTable,
                ["Order"] = ordersTable
            },
            PhysicalFieldNames = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Customer"] = new(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Display Name" },
                ["Order"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["CustomerId"] = "Customer Id",
                    ["Code"] = "Order Code"
                }
            },
            TableProviders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Customer"] = provider,
                ["Order"] = provider
            },
            SelectedFields =
            [
                new VisualQueryFieldModel
                {
                    TableName = "Customer",
                    FieldName = "Name",
                    Alias = "CustomerName"
                },
                new VisualQueryFieldModel
                {
                    TableName = "Order",
                    FieldName = "Code",
                    Alias = "OrderCode"
                }
            ],
            Joins =
            [
                new VisualQueryJoinModel
                {
                    LeftTable = "Customer",
                    LeftField = "Id",
                    RightTable = "Order",
                    RightField = "CustomerId",
                    JoinType = "LEFT JOIN"
                }
            ],
            Filters =
            [
                new VisualQueryFilterModel
                {
                    TableName = "Customer",
                    FieldName = "Name",
                    Value = "O'Brien",
                    ClrType = "string"
                }
            ],
            OrderBy =
            [
                new VisualQuerySortModel { TableName = "Customer", FieldName = "Id" },
                new VisualQuerySortModel { TableName = "Order", FieldName = "Id" }
            ],
            TopRows = 3
        };

        var query = new VisualQueryEngine().GenerateQuery(model);
        await using var command = connection.CreateCommand();
        command.CommandText = query.SqlQuery;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("O'Brien", reader.GetString(0));
        Assert.Equal("A", reader.GetString(1));

        Assert.True(await reader.ReadAsync());
        Assert.Equal("O'Brien", reader.GetString(0));
        Assert.Equal("B", reader.GetString(1));

        Assert.True(await reader.ReadAsync());
        Assert.Equal("O'Brien", reader.GetString(0));
        Assert.True(await reader.IsDBNullAsync(1));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task OpenWhenReadyAsync(DbConnection connection)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await connection.OpenAsync();
                return;
            }
            catch (DbException) when (attempt < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static async Task InsertCustomerAsync(
        DbConnection connection,
        string quotedTableName,
        Func<string, string> quoteIdentifier,
        int id,
        string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {quotedTableName} ({quoteIdentifier("Id")}, {quoteIdentifier("Display Name")}) VALUES (@id, @name)";
        command.Parameters.Add(CreateParameter(command, "@id", id));
        command.Parameters.Add(CreateParameter(command, "@name", name));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertOrderAsync(
        DbConnection connection,
        string quotedTableName,
        Func<string, string> quoteIdentifier,
        int id,
        int customerId,
        string code)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {quotedTableName} ({quoteIdentifier("Id")}, {quoteIdentifier("Customer Id")}, {quoteIdentifier("Order Code")}) VALUES (@id, @customerId, @code)";
        command.Parameters.Add(CreateParameter(command, "@id", id));
        command.Parameters.Add(CreateParameter(command, "@customerId", customerId));
        command.Parameters.Add(CreateParameter(command, "@code", code));
        await command.ExecuteNonQueryAsync();
    }

    private static DbParameter CreateParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }
}
