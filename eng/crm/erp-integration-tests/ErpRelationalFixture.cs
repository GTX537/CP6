using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.ErpIntegration.SqlTests;

public interface IErpScenarioDatabase : IDbContextFactory<ErpIntegrationContext>
{
    CP6Context CreateBusinessContext(Guid tenant);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ErpRelationalCollection : ICollectionFixture<ErpRelationalFixture>
{
    public const string Name = "C03 required relational database";
}

/// <summary>Uses the root-owned WP4 database and actual provider migrations, once per selected-provider test process.</summary>
public sealed class ErpRelationalFixture : IAsyncLifetime, IErpScenarioDatabase
{
    private const string TaskName = "DB-COMPAT-01-WP4";
    private readonly string owner;
    private readonly OwnedTestDatabase? ownedDatabase;

    public ErpRelationalFixture()
    {
        Database = new DatabaseOptions(Environment.GetEnvironmentVariable("CP6_ERP_TEST_PROVIDER") switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("CP6_ERP_TEST_PROVIDER must select SqlServer or PostgreSql; required ERP database tests never skip.")
        });
        owner = Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER") ?? "";
        var connection = Environment.GetEnvironmentVariable("CP6_ERP_TEST_CONNECTION") ?? "";
        ownedDatabase = OwnedTestDatabase.FromEnvironment(Database, connection,
            [DatabaseFixtureRole.Erp], "CP6Compat.WP6.ErpTests");
        if (ownedDatabase is not null)
        {
            ConnectionString = ownedDatabase.ConnectionString;
            return;
        }
        Require(Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "A WP4 database ownership receipt is required.");
        Require(!string.IsNullOrWhiteSpace(connection), "CP6_ERP_TEST_CONNECTION must identify the selected WP4 test database.");
        ConnectionString = ValidateConnection(connection);
    }

    public DatabaseOptions Database { get; }
    public string ConnectionString { get; }
    public bool IsPostgreSql => Database.Provider == DatabaseProvider.PostgreSql;
    public string SetupSummary { get; private set; } = "ERP relational setup has not completed.";

    public async Task InitializeAsync()
    {
        await VerifyOwnerAsync();
        var migrations = new List<string>();
        await using (var core = CreateBusinessContext(TenantContext.DefaultTenant))
            migrations.Add("Core=" + await MigrateAsync(core));

        var priorityProfile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.IdentityPriority);
        if (priorityProfile.MigrationOwner == DatabaseContextKind.IdentityPriority)
        {
            var options = Configure<IdentityMessagingContext>(DatabaseContextKind.IdentityPriority);
            await using var priority = new IdentityMessagingContext(options);
            migrations.Add("IdentityPriority=" + await MigrateAsync(priority));
        }
        else migrations.Add("IdentityPriority=Core-owned");

        var erpProfile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.ErpIntegration);
        if (erpProfile.MigrationOwner == DatabaseContextKind.ErpIntegration)
        {
            await using var erp = CreateDbContext();
            migrations.Add("ErpIntegration=" + await MigrateAsync(erp));
        }
        else migrations.Add("ErpIntegration=Core-owned");

        // A reused root-owned database can contain prior tenants; readiness does not mean an empty global Inbox.
        await using var ready = CreateDbContext();
        _ = await ready.Inbox.AsNoTracking().AnyAsync();
        SetupSummary = $"Provider={Database.Provider}; migrations {string.Join(", ", migrations)}; pending=0; ERP Inbox readable; owner verified.";
    }

    public CP6Context CreateBusinessContext(Guid tenant)
    {
        var context = new CP6Context(Configure<CP6Context>(DatabaseContextKind.Core),
            new TenantContext { CurrentTenantId = tenant });
        context.Database.SetCommandTimeout(180);
        return context;
    }

    public ErpIntegrationContext CreateDbContext()
    {
        var context = new ErpIntegrationContext(Configure<ErpIntegrationContext>(DatabaseContextKind.ErpIntegration));
        context.Database.SetCommandTimeout(60);
        return context;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DbContextOptions<TContext> Configure<TContext>(DatabaseContextKind kind) where TContext : DbContext
    {
        var profile = DatabaseMigrationProfile.For(Database, kind);
        return DatabaseContextOptions.Configure(new DbContextOptionsBuilder<TContext>(), Database, ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
    }

    private DbConnection CreateConnection() => new DatabaseConnectionFactory(Database).Create(ConnectionString);

    private async Task VerifyOwnerAsync()
    {
        if (ownedDatabase is not null)
        {
            await ownedDatabase.VerifyAsync();
            return;
        }
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var actual = await connection.QuerySingleOrDefaultAsync<string>(IsPostgreSql
            ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
            : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP4')",
            commandTimeout: 30);
        Require(actual == (IsPostgreSql ? TaskName + ":" + owner : owner),
            "Actual database task and owner must match the WP4 receipt before ERP migrations or writes.");
    }

    private static async Task<int> MigrateAsync(DbContext context)
    {
        context.Database.SetCommandTimeout(180);
        var available = context.Database.GetMigrations().ToArray();
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.Length > 0 && before.Length <= available.Length && before.SequenceEqual(available.Take(before.Length)),
            "The selected ERP fixture context must have known migrations and an unmixed migration history.");
        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.SequenceEqual(applied) && !(await context.Database.GetPendingMigrationsAsync()).Any(),
            "The selected ERP fixture context must finish all actual provider migrations.");
        return applied.Length;
    }

    private string ValidateConnection(string connection)
    {
        const string databasePattern = "\\ACP6Compat_WP4_[0-9]{8}_[a-f0-9]{8}\\z";
        if (IsPostgreSql)
        {
            var builder = new NpgsqlConnectionStringBuilder(connection);
            Require(builder.Host is "localhost" or "127.0.0.1" or "::1", "A literal loopback PostgreSQL host is required.");
            Require(Regex.IsMatch(builder.Database ?? "", databasePattern), "A dedicated WP4 database name is required.");
            builder.IncludeErrorDetail = false;
            builder.ApplicationName = "CP6Compat.WP4.ErpTests";
            builder.Timeout = 15;
            builder.CommandTimeout = 60;
            return builder.ConnectionString;
        }

        var sql = new SqlConnectionStringBuilder(connection);
        var source = sql.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? sql.DataSource[4..] : sql.DataSource;
        var host = source.Split('\\', ',')[0];
        Require(host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "." or "(local)", "A literal loopback SQL Server host is required.");
        Require(Regex.IsMatch(sql.InitialCatalog, databasePattern), "A dedicated WP4 database name is required.");
        Require(string.IsNullOrEmpty(sql.AttachDBFilename), "Attached database files are not ERP test inputs.");
        Require(!sql.MultipleActiveResultSets, "ERP transaction tests require SQL Server savepoints with MARS disabled.");
        sql.ApplicationName = "CP6Compat.WP4.ErpTests";
        sql.ConnectTimeout = 15;
        sql.CommandTimeout = 60;
        return sql.ConnectionString;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
