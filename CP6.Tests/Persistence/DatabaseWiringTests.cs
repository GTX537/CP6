using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Configuration;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CP6.Tests.Persistence;

[Collection(DatabaseDesignTimeEnvironmentCollection.Name)]
public sealed class DatabaseWiringTests
{
    private const string PostgreSqlConnection = "Host=database.cp6.test;Database=fixture;Username=fixture;Password=fixture-password;Search Path=public";
    private const string SqlServerConnection = "Server=database.cp6.test;Database=fixture;Integrated Security=True;MultipleActiveResultSets=False";
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Theory]
    [InlineData("Core", "__EFMigrationsHistory")]
    [InlineData("Space", "__EFMigrationsHistory_Space")]
    [InlineData("IdentityPriority", "__EFMigrationsHistory_IdentityPriority")]
    [InlineData("ErpIntegration", "__EFMigrationsHistory_ErpIntegration")]
    public void All_design_factories_select_postgresql_and_separate_history_without_connecting(string kind, string expectedHistory)
    {
        using var context = DesignContext(kind, ["--Database:Provider", "PostgreSql"]);
        var options = context.GetService<IDbContextOptions>();
        var relational = RelationalOptionsExtension.Extract(options);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.NpgsqlOptionsExtension", relational.GetType().FullName);
        Assert.Equal("CP6.Persistence.PostgreSql", relational.MigrationsAssembly);
        Assert.Equal(expectedHistory, relational.MigrationsHistoryTableName);
        Assert.Equal("public", relational.MigrationsHistoryTableSchema);
        Assert.Single(options.Extensions, extension => extension.Info.IsDatabaseProvider);
    }

    [Theory]
    [InlineData("Core", null)]
    [InlineData("Space", "__EFMigrationsHistory_Space")]
    [InlineData("IdentityPriority", null)]
    [InlineData("ErpIntegration", null)]
    public void Sql_server_design_factories_preserve_existing_migration_metadata(string kind, string? expectedHistory)
    {
        using var context = DesignContext(kind, ["--Database:Provider=SqlServer"]);
        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal.SqlServerOptionsExtension", relational.GetType().FullName);
        Assert.Null(relational.MigrationsAssembly);
        Assert.Equal(expectedHistory, relational.MigrationsHistoryTableName);
        Assert.Null(relational.MigrationsHistoryTableSchema);
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("Space")]
    [InlineData("IdentityPriority")]
    [InlineData("ErpIntegration")]
    public void Design_factories_reject_unknown_provider_without_echoing_input(string kind)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DesignContext(kind, ["--Database:Provider", "Password=do-not-print"]));

        Assert.Contains("Database:Provider", exception.Message);
        Assert.DoesNotContain("do-not-print", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Existing_space_registration_retains_sql_server_and_history()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISpaceExecutionContext, FixtureExecutionContext>();
        services.AddSpaceDesignV1Persistence(SqlServerConnection);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SpaceContext>());
        var relational = RelationalOptionsExtension.Extract(scope.ServiceProvider.GetRequiredService<DbContextOptions<SpaceContext>>());
        Assert.Contains("SqlServer", relational.GetType().Name);
        Assert.Equal("__EFMigrationsHistory_Space", relational.MigrationsHistoryTableName);
        Assert.Null(relational.MigrationsAssembly);
    }

    [Fact]
    public void Explicit_space_registration_uses_deployment_provider_and_postgresql_history()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISpaceExecutionContext, FixtureExecutionContext>();
        services.AddSpaceDesignV1Persistence(PostgreSqlConnection, new(DatabaseProvider.PostgreSql));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SpaceContext>());
        var relational = RelationalOptionsExtension.Extract(scope.ServiceProvider.GetRequiredService<DbContextOptions<SpaceContext>>());
        Assert.Contains("Npgsql", relational.GetType().Name);
        Assert.Equal("CP6.Persistence.PostgreSql", relational.MigrationsAssembly);
        Assert.Equal("__EFMigrationsHistory_Space", relational.MigrationsHistoryTableName);
    }

    [Fact]
    public void Runtime_accepts_postgresql_deployment()
    {
        DatabaseRuntimeSupport.EnsureSupported(new(DatabaseProvider.PostgreSql));
    }

    [Fact]
    public void Runtime_accepts_existing_sql_server_deployment()
    {
        DatabaseRuntimeSupport.EnsureSupported(new(DatabaseProvider.SqlServer));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Identity_dispatcher_factories_use_same_fixed_provider_and_context_metadata(DatabaseProvider provider)
    {
        var selected = new DatabaseOptions(provider);
        var connection = provider == DatabaseProvider.SqlServer ? SqlServerConnection : PostgreSqlConnection;
        using var ordinary = IdentityEventDispatchWorker.CreateOrdinaryContextFactory(selected, connection).CreateDbContext();
        using var priority = IdentityEventDispatchWorker.CreatePriorityContextFactory(selected, connection).CreateDbContext();
        var ordinaryOptions = RelationalOptionsExtension.Extract(ordinary.GetService<IDbContextOptions>());
        var priorityOptions = RelationalOptionsExtension.Extract(priority.GetService<IDbContextOptions>());

        Assert.Equal(ordinaryOptions.GetType(), priorityOptions.GetType());
        Assert.Equal(connection, ordinaryOptions.ConnectionString);
        Assert.Equal(connection, priorityOptions.ConnectionString);
        Assert.Equal(provider == DatabaseProvider.PostgreSql ? "CP6.Persistence.PostgreSql" : null, priorityOptions.MigrationsAssembly);
        Assert.Equal(provider == DatabaseProvider.PostgreSql ? "__EFMigrationsHistory_IdentityPriority" : null, priorityOptions.MigrationsHistoryTableName);
        Assert.Equal(provider == DatabaseProvider.PostgreSql ? "__EFMigrationsHistory" : null, ordinaryOptions.MigrationsHistoryTableName);
        Assert.Equal(provider == DatabaseProvider.PostgreSql ? "public" : null, priorityOptions.MigrationsHistoryTableSchema);
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("Space")]
    [InlineData("IdentityPriority")]
    [InlineData("ErpIntegration")]
    public void Design_factory_provider_environment_and_argument_precedence_are_shared(string kind)
    {
        using var environment = new DesignEnvironment("PostgreSql", null);
        using var fromEnvironment = DesignContext(kind, []);
        Assert.Contains("Npgsql", RelationalOptionsExtension.Extract(fromEnvironment.GetService<IDbContextOptions>()).GetType().Name);

        using var fromArguments = DesignContext(kind, ["--Database:Provider=SqlServer"]);
        Assert.Contains("SqlServer", RelationalOptionsExtension.Extract(fromArguments.GetService<IDbContextOptions>()).GetType().Name);
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("Space")]
    [InlineData("IdentityPriority")]
    [InlineData("ErpIntegration")]
    public void Missing_design_provider_environment_keeps_sql_server_default(string kind)
    {
        using var environment = new DesignEnvironment(null, null);
        using var context = DesignContext(kind, []);
        Assert.Contains("SqlServer", RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).GetType().Name);
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("Space")]
    [InlineData("IdentityPriority")]
    [InlineData("ErpIntegration")]
    public void Explicit_design_connection_arguments_are_used_without_connecting(string kind)
    {
        using var environment = new DesignEnvironment(null, null);
        using var context = DesignContext(kind, ["--Database:Provider=PostgreSql", "--ConnectionStrings:DefaultConnection", PostgreSqlConnection]);
        Assert.Equal(PostgreSqlConnection, RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).ConnectionString);
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("IdentityPriority")]
    [InlineData("ErpIntegration")]
    public void Core_design_factories_do_not_read_application_connection_credentials(string kind)
    {
        using var environment = new DesignEnvironment("PostgreSql", "Password=do-not-print;do-not-print=invalid");
        using var context = DesignContext(kind, []);

        Assert.DoesNotContain("do-not-print", RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).ConnectionString!);
    }

    [Fact]
    public void Space_design_factory_keeps_explicit_environment_connection_and_safe_failure()
    {
        using var environment = new DesignEnvironment("PostgreSql", PostgreSqlConnection);
        using var context = DesignContext("Space", []);
        Assert.Equal(PostgreSqlConnection, RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).ConnectionString);

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Password=do-not-print;do-not-print=invalid");
        var exception = Assert.Throws<InvalidOperationException>(() => DesignContext("Space", []));
        Assert.DoesNotContain("do-not-print", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Standalone_identity_registration_constructs_dispatch_worker_with_selected_provider()
    {
        var configuration = IntegrationConfiguration("PostgreSql", PostgreSqlConnection);
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(configuration);
        services.AddCrmIdentityEvents(configuration, Oidc());
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IHostedService>().OfType<IdentityEventDispatchWorker>());
    }

    [Fact]
    public void Explicit_integration_provider_stays_fixed_when_configuration_provider_changes()
    {
        var configuration = IntegrationConfiguration("PostgreSql", PostgreSqlConnection);
        var selected = DatabaseOptions.FromConfiguration(configuration);
        configuration["Database:Provider"] = "SqlServer";
        var services = new ServiceCollection();

        services.AddCrmIdentityEvents(configuration, Oidc(), selected);
        services.AddErpIntegration(configuration, Oidc(), selected);
        using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<IDbContextFactory<ErpIntegrationContext>>().CreateDbContext();
        Assert.Contains("Npgsql", RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).GetType().Name);
    }

    [Fact]
    public void Identity_registration_accepts_postgresql_without_sql_server_connection_parsing()
    {
        var configuration = IntegrationConfiguration("PostgreSql", PostgreSqlConnection);
        var services = new ServiceCollection();

        services.AddCrmIdentityEvents(configuration, Oidc());
        using var provider = services.BuildServiceProvider();

        Assert.Equal("us", provider.GetRequiredService<CrmIdentityRuntime>().Options.Tenants[Tenant]);
    }

    [Fact]
    public void Erp_registration_uses_postgresql_queue_factory_without_sql_server_connection_parsing()
    {
        var configuration = IntegrationConfiguration("PostgreSql", PostgreSqlConnection);
        var services = new ServiceCollection();

        services.AddErpIntegration(configuration, Oidc());
        using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<IDbContextFactory<ErpIntegrationContext>>().CreateDbContext();
        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());

        Assert.Contains("Npgsql", relational.GetType().Name);
        Assert.Equal("CP6.Persistence.PostgreSql", relational.MigrationsAssembly);
        Assert.Equal("__EFMigrationsHistory_ErpIntegration", relational.MigrationsHistoryTableName);
    }

    [Theory]
    [InlineData("identity", "C02_REQUIRES_SQL_SAVEPOINTS_DISABLE_MARS")]
    [InlineData("erp", "C03_REQUIRES_SQL_SAVEPOINTS_DISABLE_MARS")]
    public void Sql_server_integrations_still_reject_mars(string kind, string expectedCode)
    {
        var configuration = IntegrationConfiguration("SqlServer", SqlServerConnection.Replace("False", "True", StringComparison.Ordinal));
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            if (kind == "identity") services.AddCrmIdentityEvents(configuration, Oidc());
            else services.AddErpIntegration(configuration, Oidc());
        });

        Assert.Equal(expectedCode, exception.Message);
    }

    private static DbContext DesignContext(string kind, string[] args) => kind switch
    {
        "Core" => new CP6ContextDesignFactory().CreateDbContext(args),
        "Space" => new SpaceContextDesignFactory().CreateDbContext(args),
        "IdentityPriority" => new IdentityMessagingContextDesignFactory().CreateDbContext(args),
        "ErpIntegration" => new ErpIntegrationContextDesignFactory().CreateDbContext(args),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static IConfiguration IntegrationConfiguration(string provider, string connection) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["CrmIdentity:Enabled"] = "true",
            ["CrmIdentity:Tenants:" + Tenant] = "us",
            ["CrmIdentity:ProjectionReaderClientIds:0"] = "reader",
            ["ErpIntegration:Enabled"] = "true",
            ["ErpIntegration:Tenants:" + Tenant] = "us",
            ["ErpIntegration:ReaderClientIds:0"] = "reader",
            ["ErpIntegration:DaprAppToken"] = new string('a', 32),
            ["ErpIntegration:DaprApiToken"] = new string('b', 32)
        }).Build();

    private static CrmOidcOptions Oidc() => new()
    {
        Enabled = true,
        Issuer = "https://identity.cp6.test",
        Organizations = [new() { TenantId = Tenant, Region = "us", CrmEnabled = true }],
        ServiceClients = [new() { ClientId = "reader", TenantId = Tenant, Enabled = true, AllowedScopes = ["cp6.services"] }]
    };

    private sealed class FixtureExecutionContext : ISpaceExecutionContext
    {
        public Guid TenantId => Tenant;
        public Guid ActorId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    }

    private sealed class DesignEnvironment : IDisposable
    {
        private readonly string? _provider = Environment.GetEnvironmentVariable("Database__Provider");
        private readonly string? _connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        public DesignEnvironment(string? provider, string? connection)
        {
            Environment.SetEnvironmentVariable("Database__Provider", provider);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connection);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("Database__Provider", _provider);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connection);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseDesignTimeEnvironmentCollection
{
    public const string Name = "Database design-time environment";
}
