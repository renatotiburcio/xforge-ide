using XForge.Ide.Core.Enums;

namespace XForge.Ide.Templates.Templates;

/// <summary>Templates das camadas Domain, Application e Infrastructure do app gerado.</summary>
internal static class LayerTemplates
{
    // ------------------------------------------------------------------ Domain
    public static string DomainCsproj(NewAppOptions o) => Tpl.Render(o, """
<Project Sdk="Microsoft.NET.Sdk">
</Project>
""");

    public static string BaseEntity(NewAppOptions o) => Tpl.Render(o, """
namespace {{NS}}.Domain.Common;

/// <summary>Base de todas as entidades de negócio.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
}
""");

    public static string DomainResult(NewAppOptions o) => Tpl.Render(o, """
namespace {{NS}}.Domain.Common;

/// <summary>Resultado de operação (padrão Result).</summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public IReadOnlyList<string> Errors { get; }

    protected Result(bool isSuccess, string? error, IReadOnlyList<string> errors)
    {
        IsSuccess = isSuccess;
        Error = error;
        Errors = errors;
    }

    public static Result Success() => new(true, null, Array.Empty<string>());
    public static Result Failure(string error) => new(false, error, new[] { error });
    public static Result Failure(IReadOnlyList<string> errors) => new(false, errors.FirstOrDefault(), errors);
}

public class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, string? error, IReadOnlyList<string> errors)
        : base(isSuccess, error, errors) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Resultado sem valor: " + Error);

    public static Result<T> Success(T value) => new(true, value, null, Array.Empty<string>());
    public static new Result<T> Failure(string error) => new(false, default, error, new[] { error });
}
""");

    // ------------------------------------------------------------- Application
    public static string ApplicationCsproj(NewAppOptions o) => Tpl.Render(o, """
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\{{NS}}.Domain\{{NS}}.Domain.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.7" />
  </ItemGroup>

</Project>
""");

    public static string IRepository(NewAppOptions o) => Tpl.Render(o, """
using {{NS}}.Domain.Common;

namespace {{NS}}.Application.Abstractions;

/// <summary>Repositório genérico (pattern DDD).</summary>
public interface IRepository<TEntity> where TEntity : BaseEntity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> ListAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
    Task AddAsync(TEntity entity, CancellationToken ct = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
}

/// <summary>Unidade de trabalho -?" persiste o agregado de alterações.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
""");

    public static string Messaging(NewAppOptions o) => Tpl.Render(o, """
using Microsoft.Extensions.DependencyInjection;

namespace {{NS}}.Application.Messaging;

/// <summary>Comando que retorna uma resposta.</summary>
public interface ICommand<TResponse>;

/// <summary>Consulta que retorna uma resposta.</summary>
public interface IQuery<TResponse>;

/// <summary>Handler de comando.</summary>
public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<TResponse> Handle(TCommand command, CancellationToken ct = default);
}

/// <summary>Handler de consulta.</summary>
public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<TResponse> Handle(TQuery query, CancellationToken ct = default);
}

/// <summary>Despachante de comandos/consultas (mediator leve, sem dependências).</summary>
public interface IDispatcher
{
    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
    Task<TResponse> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}

/// <inheritdoc/>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResponse));
        dynamic handler = _serviceProvider.GetRequiredService(handlerType);
        return await handler.Handle((dynamic)command, ct);
    }

    public async Task<TResponse> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResponse));
        dynamic handler = _serviceProvider.GetRequiredService(handlerType);
        return await handler.Handle((dynamic)query, ct);
    }
}
""");

    public static string ApplicationDependencyInjection(NewAppOptions o) => Tpl.Render(o, """
using {{NS}}.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace {{NS}}.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        return services;
    }
}
""");

    // ----------------------------------------------------------- Infrastructure
    public static string InfrastructureCsproj(NewAppOptions o)
    {
        var providerPackage = o.Provider switch
        {
            ProviderKind.PostgreSql => "Npgsql.EntityFrameworkCore.PostgreSQL",
            ProviderKind.MySql => "MySql.EntityFrameworkCore",
            ProviderKind.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
            _ => "Microsoft.EntityFrameworkCore.Sqlite",
        };
        var providerVersion = o.Provider switch
        {
            ProviderKind.PostgreSql => "10.0.3",
            ProviderKind.MySql => "10.0.9",
            _ => "10.0.7",
        };
        var identity = o.UseIdentity ? """
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.13" />
  </ItemGroup>

""" : string.Empty;

        return Tpl.Render(o, """
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\{{NS}}.Application\{{NS}}.Application.csproj" />
  </ItemGroup>

{{IDENTITY}}
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.7" PrivateAssets="all" />
    <PackageReference Include="{{PROVIDER_PACKAGE}}" Version="{{PROVIDER_VERSION}}" />
  </ItemGroup>

</Project>
""", ("IDENTITY", identity), ("PROVIDER_PACKAGE", providerPackage), ("PROVIDER_VERSION", providerVersion));
    }

    public static string DbContext(NewAppOptions o)
    {
        if (o.UseIdentity)
        {
            return Tpl.Render(o, """
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using {{NS}}.Infrastructure.Identity;

namespace {{NS}}.Infrastructure.Persistence;

/// <summary>DbContext principal (negócio + Identity).</summary>
public class {{NS}}DbContext(DbContextOptions<{{NS}}DbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Entidades de negócio são registradas aqui (wizards da XForge IDE geram este mapeamento).
    }
}
""");
        }

        return Tpl.Render(o, """
using Microsoft.EntityFrameworkCore;

namespace {{NS}}.Infrastructure.Persistence;

/// <summary>DbContext principal do aplicativo.</summary>
public class {{NS}}DbContext(DbContextOptions<{{NS}}DbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Entidades de negócio são registradas aqui (wizards da XForge IDE geram este mapeamento).
    }
}
""");
    }

    public static string ApplicationUser(NewAppOptions o) => Tpl.Render(o, """
using Microsoft.AspNetCore.Identity;

namespace {{NS}}.Infrastructure.Identity;

/// <summary>Usuário do aplicativo (ASP.NET Core Identity).</summary>
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
""");

    public static string RepositoryImpl(NewAppOptions o) => Tpl.Render(o, """
using {{NS}}.Application.Abstractions;
using {{NS}}.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace {{NS}}.Infrastructure.Persistence;

/// <summary>Implementação do repositório genérico sobre EF Core.</summary>
public class Repository<TEntity>({{NS}}DbContext context) : IRepository<TEntity> where TEntity : BaseEntity
{
    private readonly DbSet<TEntity> _set = context.Set<TEntity>();

    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken ct = default)
        => await _set.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<TEntity>> ListAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => await _set.AsNoTracking().Where(predicate).ToListAsync(ct);

    public async Task AddAsync(TEntity entity, CancellationToken ct = default)
        => await _set.AddAsync(entity, ct);

    public void Update(TEntity entity) => _set.Update(entity);

    public void Remove(TEntity entity) => _set.Remove(entity);
}

/// <inheritdoc/>
public class UnitOfWork({{NS}}DbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => context.SaveChangesAsync(ct);
}
""");

    public static string InfrastructureDependencyInjection(NewAppOptions o)
    {
        var providerArm = o.Provider switch
        {
            ProviderKind.PostgreSql => "\"postgresql\" => options.UseNpgsql(connectionString),",
            ProviderKind.MySql => "\"mysql\" => options.UseMySQL(connectionString),",
            ProviderKind.SqlServer => "\"sqlserver\" => options.UseSqlServer(connectionString),",
            _ => "\"sqlite\" => options.UseSqlite(connectionString),",
        };

        return Tpl.Render(o, """
using {{NS}}.Application.Abstractions;
using {{NS}}.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace {{NS}}.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra o DbContext com o provider configurado e os repositórios genéricos.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string provider, string connectionString)
    {
        services.AddDbContext<{{NS}}DbContext>(options => _ = provider.Trim().ToLowerInvariant() switch
        {
{{PROVIDER_SWITCH}}
            _ => options.UseSqlite(connectionString),
        });

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
""", ("PROVIDER_SWITCH", providerArm));
    }

    public static string DesignTimeFactory(NewAppOptions o) => Tpl.Render(o, """
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace {{NS}}.Infrastructure.Persistence;

/// <summary>Fábrica para ferramentas de design (dotnet-ef).</summary>
public class {{NS}}DbContextFactory : IDesignTimeDbContextFactory<{{NS}}DbContext>
{
    public {{NS}}DbContext CreateDbContext(string[] args)
    {
        // Ajuste a connection string abaixo ou leia de variável de ambiente.
        var connectionString = Environment.GetEnvironmentVariable("{{NS}}_CONNECTION_STRING")
            ?? "Data Source=app.db";

        var options = new DbContextOptionsBuilder<{{NS}}DbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new {{NS}}DbContext(options);
    }
}
""");
}
