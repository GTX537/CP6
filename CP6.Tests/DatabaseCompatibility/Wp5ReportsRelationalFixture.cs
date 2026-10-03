using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.EntityFramework;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.DatabaseCompatibility;

public sealed class Wp5ReportsFactAttribute : FactAttribute
{
    public const string ProviderVariable = "CP6_WP5_CORE_TEST_PROVIDER";
    public const string ConnectionVariable = "CP6_WP5_CORE_TEST_CONNECTION";
    public const string OwnerVariable = "CP6_TEST_DATABASE_OWNER";

    public Wp5ReportsFactAttribute()
    {
        if (!IsSelected) Skip = "Select the WP5 Core provider and connection for required native reports/GDPR tests.";
    }

    internal static bool IsSelected => Environment.GetEnvironmentVariable(ProviderVariable) is not null
        || Environment.GetEnvironmentVariable(ConnectionVariable) is not null;
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wp5ReportsRelationalCollection : ICollectionFixture<Wp5ReportsRelationalFixture>
{
    public const string Name = "WP5 required reports and GDPR database";
}

/// <summary>The root owns the database lifecycle; this fixture verifies ownership before migrating or writing.</summary>
public sealed class Wp5ReportsRelationalFixture : IAsyncLifetime
{
    private const string TaskName = "DB-COMPAT-01-WP5";
    private readonly DatabaseOptions? database;
    private readonly string? connectionString;
    private readonly string? owner;

    public Wp5ReportsRelationalFixture()
    {
        if (!Wp5ReportsFactAttribute.IsSelected) return;
        database = new(Environment.GetEnvironmentVariable(Wp5ReportsFactAttribute.ProviderVariable) switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("WP5_REPORTS_PROVIDER_INVALID: explicitly select SqlServer or PostgreSql.")
        });
        owner = Environment.GetEnvironmentVariable(Wp5ReportsFactAttribute.OwnerVariable) ?? "";
        Require(Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "WP5_REPORTS_OWNER_REQUIRED");
        var supplied = Environment.GetEnvironmentVariable(Wp5ReportsFactAttribute.ConnectionVariable) ?? "";
        Require(!string.IsNullOrWhiteSpace(supplied), "WP5_REPORTS_CONNECTION_REQUIRED");
        connectionString = ValidateConnection(supplied);
    }

    public DatabaseOptions Database => database ?? throw new InvalidOperationException("WP5 reports database was not selected.");
    public string ConnectionString => connectionString ?? throw new InvalidOperationException("WP5 reports database was not selected.");
    public string SetupSummary { get; private set; } = "WP5 reports database setup has not completed.";

    public async Task InitializeAsync()
    {
        if (database is null) return;
        await using (var connection = CreateConnection())
        {
            await connection.OpenAsync();
            var postgres = Database.Provider == DatabaseProvider.PostgreSql;
            var actual = await connection.QuerySingleOrDefaultAsync<string>(postgres
                ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
                : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=@task)",
                new { task = TaskName }, commandTimeout: 30);
            Require(actual == (postgres ? TaskName + ":" + owner : owner), "WP5_REPORTS_ACTUAL_TASK_OWNER_MISMATCH");
        }

        await using var core = CreateContext(TenantContext.DefaultTenant);
        var coreCount = await MigrateAsync(core);
        var priorityProfile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.IdentityPriority);
        await using var priority = CreatePriorityContext();
        var priorityCount = priorityProfile.MigrationOwner == DatabaseContextKind.IdentityPriority
            ? await MigrateAsync(priority) : coreCount;
        // SQL Server's priority tables belong to Core migrations; use the real priority mapping to verify installation.
        _ = await priority.Set<Cp6OutboxMessage>().AsNoTracking().Take(1).CountAsync();
        SetupSummary = $"Provider={Database.Provider}; Core migrations={coreCount}; IdentityPriority migration owner={priorityProfile.MigrationOwner}, migrations={priorityCount}; pending=0; WP5 owner verified.";
    }

    public CP6Context CreateContext(Guid tenant, params IInterceptor[] interceptors)
        => CreateCoreContext(new TenantContext { CurrentTenantId = tenant }, null, interceptors);

    public CP6Context CreateIdentityContext(Guid tenant, CrmIdentityRuntime identity)
        => CreateIdentityContext(new TenantContext { CurrentTenantId = tenant }, identity);

    public CP6Context CreateIdentityContext(TenantContext tenant, CrmIdentityRuntime identity) => CreateCoreContext(tenant, identity, []);

    private CP6Context CreateCoreContext(ITenantContext tenant, CrmIdentityRuntime? identity, IInterceptor[] interceptors)
    {
        var profile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), Database,
            ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        var context = new CP6Context(options.Options, tenant, identity: identity);
        context.Database.SetCommandTimeout(180);
        return context;
    }

    public IdentityMessagingContext CreatePriorityContext()
    {
        var profile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.IdentityPriority);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<IdentityMessagingContext>(), Database,
            ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        var context = new IdentityMessagingContext(options.Options);
        context.Database.SetCommandTimeout(180);
        return context;
    }

    public DbConnection CreateConnection() => new DatabaseConnectionFactory(Database).Create(ConnectionString);

    public async Task RequireEmptyReportTablesAsync()
    {
        await using var db = CreateContext(TenantContext.DefaultTenant);
        Require(!await db.Orders.IgnoreQueryFilters().AnyAsync()
            && !await db.WorkOrders.IgnoreQueryFilters().AnyAsync()
            && !await db.WorkOrderProcesses.IgnoreQueryFilters().AnyAsync()
            && !await db.ProductionResults.IgnoreQueryFilters().AnyAsync()
            && !await db.OutboundOrders.IgnoreQueryFilters().AnyAsync()
            && !await db.Stocks.IgnoreQueryFilters().AnyAsync()
            && !await db.ProductMasters.IgnoreQueryFilters().AnyAsync()
            && !await db.StockTakes.IgnoreQueryFilters().AnyAsync(),
            "WP5_REPORTS_EMPTY_DATABASE_REQUIRED: run the four empty-report facts before seeded business suites; no cleanup is performed.");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<int> MigrateAsync(DbContext context)
    {
        var available = context.Database.GetMigrations().ToArray();
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.Length > 0 && before.Length <= available.Length && before.SequenceEqual(available.Take(before.Length)),
            "WP5_REPORTS_MIGRATION_HISTORY_INVALID");
        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.SequenceEqual(applied) && !(await context.Database.GetPendingMigrationsAsync()).Any(),
            "WP5_REPORTS_MIGRATIONS_INCOMPLETE");
        return applied.Length;
    }

    private string ValidateConnection(string supplied)
    {
        const string pattern = "\\ACP6Compat_WP5_20261003_[a-f0-9]{8}\\z";
        if (Database.Provider == DatabaseProvider.PostgreSql)
        {
            var pg = new NpgsqlConnectionStringBuilder(supplied);
            Require(pg.Host is "localhost" or "127.0.0.1" or "::1", "WP5_REPORTS_LITERAL_LOOPBACK_REQUIRED");
            Require(Regex.IsMatch(pg.Database ?? "", pattern) && pg.Database!.EndsWith("_" + owner![..8], StringComparison.Ordinal),
                "WP5_REPORTS_DATABASE_NAME_OWNER_MISMATCH");
            pg.IncludeErrorDetail = false;
            pg.ApplicationName = "CP6Compat.WP5.ReportsTests";
            pg.Timeout = 15;
            pg.CommandTimeout = 60;
            return pg.ConnectionString;
        }

        var sql = new SqlConnectionStringBuilder(supplied);
        var source = sql.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? sql.DataSource[4..] : sql.DataSource;
        var host = source.Split('\\', ',')[0];
        Require(host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "." or "(local)", "WP5_REPORTS_LITERAL_LOOPBACK_REQUIRED");
        Require(Regex.IsMatch(sql.InitialCatalog, pattern) && sql.InitialCatalog.EndsWith("_" + owner![..8], StringComparison.Ordinal),
            "WP5_REPORTS_DATABASE_NAME_OWNER_MISMATCH");
        Require(string.IsNullOrEmpty(sql.AttachDBFilename), "WP5_REPORTS_ATTACHED_DATABASE_FORBIDDEN");
        Require(!sql.MultipleActiveResultSets, "WP5_REPORTS_MARS_MUST_BE_DISABLED");
        sql.ApplicationName = "CP6Compat.WP5.ReportsTests";
        sql.ConnectTimeout = 15;
        sql.CommandTimeout = 60;
        return sql.ConnectionString;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
