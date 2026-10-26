using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using XForge.Ide.Application.Dto;
using XForge.Ide.Application.Services;
using XForge.Ide.Core.Abstractions;
using XForge.Ide.Core.Entities;
using XForge.Ide.Core.Enums;
using XForge.Ide.Infrastructure.Introspection;
using XForge.Ide.Infrastructure.Persistence;
using XForge.Ide.Templates;

namespace XForge.Ide.Core.Tests;

/// <summary>Base com EF InMemory + serviços reais.</summary>
public abstract class ServiceTestBase : IDisposable
{
    protected readonly IdeDbContext Db;
    protected readonly ProjectService Projects;
    protected readonly DictionaryService Dictionaries;

    protected ServiceTestBase()
    {
        var options = new DbContextOptionsBuilder<IdeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        Db = new IdeDbContext(options);

        IProjectRepository projects = new ProjectRepository(Db);
        IDictionaryRepository dictionaries = new DictionaryRepository(Db);
        Projects = new ProjectService(projects);
        Dictionaries = new DictionaryService(dictionaries);
    }

    public void Dispose() => Db.Dispose();
}

public class DictionaryServiceTests : ServiceTestBase
{
    private async Task<Guid> CreateProjectAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ide-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir); // a IDE exige um diretorio existente
        var project = await Projects.CreateAsync("Projeto Teste", dir, null, null);
        return project.Value.Id;
    }

    [Fact]
    public async Task Criar_dicionario_e_entidade_com_chave_padrao()
    {
        var projectId = await CreateProjectAsync();

        var dict = await Dictionaries.CreateAsync(new CreateDictionaryRequest(projectId, "Principal"));
        Assert.True(dict.IsSuccess);

        var entity = await Dictionaries.AddEntityAsync(new CreateEntityRequest(dict.Value.Id, "Customer", "", "Sqlite", "CUS", null));
        Assert.True(entity.IsSuccess);
        Assert.Equal("Customers", entity.Value.TableName);           // tabela derivada
        Assert.Equal("Id", entity.Value.Fields[0].Name);             // PK padrão
        Assert.True(entity.Value.Keys.Single(k => k.IsPrimary).IsPrimary);

        var campo = await Dictionaries.AddFieldAsync(new CreateFieldRequest(
            entity.Value.Id, "Descricao", "string", null, 50, null, true, null, "@s50", "Descrição", null));
        Assert.True(campo.IsSuccess);
        Assert.Equal("NUMERIC".Replace("NUMERIC", "TEXT"), campo.Value.DbType); // string → TEXT
        Assert.Equal("@s50", campo.Value.Picture);                               // picture Clarion preservada

        var duplicado = await Dictionaries.AddFieldAsync(new CreateFieldRequest(
            entity.Value.Id, "Descricao", "string", null, null, null, false, null, null, null, null));
        Assert.False(duplicado.IsSuccess);                                        // nome único
    }

    [Fact]
    public async Task Nao_permite_entidade_duplicada_ni_dicionario()
    {
        var projectId = await CreateProjectAsync();
        var dict = await Dictionaries.CreateAsync(new CreateDictionaryRequest(projectId, "D1"));
        await Dictionaries.AddEntityAsync(new CreateEntityRequest(dict.Value.Id, "Order", "", "Sqlite", null, null));

        var again = await Dictionaries.AddEntityAsync(new CreateEntityRequest(dict.Value.Id, "order", "", "Sqlite", null, null));
        Assert.False(again.IsSuccess);
    }

    [Fact]
    public async Task Inferencia_sqlite_cria_dicionario_completo()
    {
        // Cria um banco SQLite real de teste
        var dbPath = Path.Combine(Path.GetTempPath(), "ide-infer-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        var connString = "Data Source=" + dbPath;

        await using (var conn = new SqliteConnection(connString))
        {
            await conn.OpenAsync();
            var ddl = """
                CREATE TABLE Clients (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Nome TEXT NOT NULL,
                    Saldo REAL,
                    Ativo INTEGER
                );
                CREATE INDEX IX_Clients_Nome ON Clients (Nome);
                CREATE TABLE Pedidos (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    ClientId TEXT NOT NULL REFERENCES Clients (Id)
                );
                """;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync();
        }

        IDatabaseIntrospector introspector = new SqliteIntrospector();
        var tables = await introspector.InferAsync(connString);

        Assert.Equal(2, tables.Count);

        var projectId = await CreateProjectAsync();
        var result = await Dictionaries.SaveInferredAsync(projectId, "Importado", tables);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.EntityCount);

        var entities = await Dictionaries.ListEntitiesAsync(result.Value.Id);
        var clients = entities.Single(e => e.TableName == "Clients");
        Assert.Equal("long", clients.Fields.Single(f => f.Name == "Ativo").ClrType);   // INTEGER → long
        Assert.Equal("double", clients.Fields.Single(f => f.Name == "Saldo").ClrType); // REAL → double
        Assert.Equal(2, clients.Keys.Count);
        // diagnóstico: quais chaves foram inferidas
        Assert.True(clients.Keys.Count == 2,
            "Chaves inferidas: " + string.Join("; ", clients.Keys.Select(k => k.Name + " [" + k.FieldsJson + "]")));

        // Libera pools antes de remover o arquivo temporário
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(dbPath);
    }
}

public class NewAppGeneratorTests
{
    [Fact]
    public void Gerador_produz_estrutura_clean_architecture_completa()
    {
        var generator = new NewAppGenerator();
        var options = new NewAppOptions
        {
            DisplayName = "Sistema Fiscal",
            RootNamespace = "SistemaFiscal",
            OutputPath = Path.Combine(Path.GetTempPath(), "ide-gen-" + Guid.NewGuid().ToString("N")[..8]),
            Provider = ProviderKind.Sqlite,
            UseIdentity = true,
            ValidateBuild = false,
        };

        var files = generator.Generate(options);

        var paths = files.Select(f => f.RelativePath).ToList();
        Assert.Contains("SistemaFiscal.sln", paths);
        Assert.Contains("Directory.Build.props", paths);
        Assert.DoesNotContain("Directory.Packages.props", paths); // M-14: sem CPM
        Assert.Contains($"src/SistemaFiscal.Domain/Common/BaseEntity.cs", paths);
        Assert.Contains($"src/SistemaFiscal.Application/Messaging/Messaging.cs", paths);
        Assert.Contains($"src/SistemaFiscal.Infrastructure/Persistence/SistemaFiscalDbContext.cs", paths);
        Assert.Contains($"src/SistemaFiscal.Infrastructure/Identity/ApplicationUser.cs", paths);
        Assert.Contains($"src/SistemaFiscal.Api/Program.cs", paths);
        Assert.Contains($"src/SistemaFiscal.Api/Endpoints/AccountEndpoints.cs", paths);
        Assert.Contains($"src/SistemaFiscal.Client/Program.cs", paths);
        Assert.Contains("tests/SistemaFiscal.Tests/SmokeTests.cs", paths);

        // Program.cs da API deve conter Identity + JWT + persistência
        var program = files.Single(f => f.RelativePath == "src/SistemaFiscal.Api/Program.cs").Content;
        Assert.Contains("AddIdentityCore", program);
        Assert.Contains("AddJwtBearer", program);
        Assert.Contains("AddPersistence", program);

        // Nenhum token {{...}} pode restar nos arquivos
        Assert.All(files, f => Assert.DoesNotContain("{{", f.Content));
    }

    [Fact]
    public void Gerador_valida_diretorio_existente()
    {
        var generator = new NewAppGenerator();
        var temp = Path.Combine(Path.GetTempPath(), "ide-gen-exists-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);

        var options = new NewAppOptions
        {
            DisplayName = "App", RootNamespace = "App", OutputPath = temp,
            Provider = ProviderKind.Sqlite, ValidateBuild = false,
        };

        // primeira escrita cria App/
        var root = generator.WriteToDisk(options);
        Assert.True(Directory.Exists(root));

        // segunda escrita no mesmo lugar deve falhar (não sobrescreve por padrão)
        Assert.Throws<InvalidOperationException>(() => generator.WriteToDisk(options));
    }
}
