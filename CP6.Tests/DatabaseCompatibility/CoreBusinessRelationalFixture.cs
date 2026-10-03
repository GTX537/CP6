using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;
using CP6.Core.Services.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.DatabaseCompatibility;

/// <summary>Default unit discovery skips native tests; either explicit Core input makes them required.</summary>
public sealed class CoreBusinessFactAttribute : FactAttribute
{
    public const string ProviderVariable = "CP6_CORE_TEST_PROVIDER";
    public const string ConnectionVariable = "CP6_CORE_TEST_CONNECTION";
    public const string OwnerVariable = "CP6_TEST_DATABASE_OWNER";

    public CoreBusinessFactAttribute()
    {
        if (!IsSelected) Skip = "Select CP6_CORE_TEST_PROVIDER and CP6_CORE_TEST_CONNECTION for required native Core business tests.";
    }

    internal static bool IsSelected => Environment.GetEnvironmentVariable(ProviderVariable) is not null
        || Environment.GetEnvironmentVariable(ConnectionVariable) is not null
        || OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.CoreWms);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CoreBusinessRelationalCollection : ICollectionFixture<CoreBusinessRelationalFixture>
{
    public const string Name = "WP4 required Core business database";
}

/// <summary>One actual Core migration pass per collection; database creation and deletion belong to the root runner.</summary>
public sealed class CoreBusinessRelationalFixture : IAsyncLifetime
{
    private const string TaskName = "DB-COMPAT-01-WP4";
    private readonly DatabaseOptions? database;
    private readonly string? connectionString;
    private readonly string? owner;
    private readonly OwnedTestDatabase? ownedDatabase;

    public CoreBusinessRelationalFixture()
    {
        if (!CoreBusinessFactAttribute.IsSelected) return;
        database = new(Environment.GetEnvironmentVariable(CoreBusinessFactAttribute.ProviderVariable) switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("CP6_CORE_TEST_PROVIDER must explicitly select SqlServer or PostgreSql; selected native tests never skip.")
        });
        owner = Environment.GetEnvironmentVariable(CoreBusinessFactAttribute.OwnerVariable) ?? "";
        var supplied = Environment.GetEnvironmentVariable(CoreBusinessFactAttribute.ConnectionVariable) ?? "";
        ownedDatabase = OwnedTestDatabase.FromEnvironment(database, supplied,
            [DatabaseFixtureRole.CoreWms], "CP6Compat.WP6.CoreBusinessTests");
        if (ownedDatabase is not null)
        {
            connectionString = ownedDatabase.ConnectionString;
            return;
        }
        Require(Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "A WP4 database ownership receipt is required.");
        Require(!string.IsNullOrWhiteSpace(supplied), "CP6_CORE_TEST_CONNECTION must identify the selected WP4 database.");
        connectionString = ValidateConnection(supplied);
    }

    public DatabaseOptions Database => database ?? throw new InvalidOperationException("No Core business database was selected.");
    public string ConnectionString => connectionString ?? throw new InvalidOperationException("No Core business database was selected.");
    public string SetupSummary { get; private set; } = "Core business database setup has not completed.";

    public async Task InitializeAsync()
    {
        if (database is null) return;
        if (ownedDatabase is not null)
            await ownedDatabase.VerifyAsync();
        else
        await using (var connection = new DatabaseConnectionFactory(Database).Create(ConnectionString))
        {
            await connection.OpenAsync();
            var postgres = Database.Provider == DatabaseProvider.PostgreSql;
            var actual = await connection.QuerySingleOrDefaultAsync<string>(postgres
                ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
                : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP4')",
                commandTimeout: 30);
            Require(actual == (postgres ? TaskName + ":" + owner : owner),
                "Actual database task and owner must match the WP4 receipt before Core migrations or writes.");
        }

        await using var context = CreateContext(TenantContext.DefaultTenant);
        var available = context.Database.GetMigrations().ToArray();
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.Length > 0 && before.Length <= available.Length && before.SequenceEqual(available.Take(before.Length)),
            "The Core test database must have known provider migrations and an unmixed history.");
        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.SequenceEqual(applied) && !(await context.Database.GetPendingMigrationsAsync()).Any(),
            "The Core test database must finish all actual provider migrations.");
        SetupSummary = $"Provider={Database.Provider}; Core migrations={applied.Length}; pending=0; {(ownedDatabase is null ? "WP4" : "WP6")} owner verified.";
    }

    public CP6Context CreateContext(Guid tenant, params IInterceptor[] interceptors)
    {
        var profile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), Database,
            ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        var context = new CP6Context(options.Options, new TenantContext { CurrentTenantId = tenant });
        context.Database.SetCommandTimeout(180);
        return context;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string ValidateConnection(string supplied)
    {
        const string pattern = "\\ACP6Compat_WP4_[0-9]{8}_[a-f0-9]{8}\\z";
        if (Database.Provider == DatabaseProvider.PostgreSql)
        {
            var pg = new NpgsqlConnectionStringBuilder(supplied);
            Require(pg.Host is "localhost" or "127.0.0.1" or "::1", "A literal loopback PostgreSQL host is required.");
            Require(Regex.IsMatch(pg.Database ?? "", pattern), "A dedicated WP4 database name is required.");
            Require(pg.Database!.EndsWith("_" + owner![..8], StringComparison.Ordinal), "The WP4 database name must match the ownership receipt.");
            pg.IncludeErrorDetail = false;
            pg.ApplicationName = "CP6Compat.WP4.CoreBusinessTests";
            pg.Timeout = 15;
            pg.CommandTimeout = 60;
            return pg.ConnectionString;
        }

        var sql = new SqlConnectionStringBuilder(supplied);
        var source = sql.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? sql.DataSource[4..] : sql.DataSource;
        var host = source.Split('\\', ',')[0];
        Require(host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "." or "(local)", "A literal loopback SQL Server host is required.");
        Require(Regex.IsMatch(sql.InitialCatalog, pattern), "A dedicated WP4 database name is required.");
        Require(sql.InitialCatalog.EndsWith("_" + owner![..8], StringComparison.Ordinal), "The WP4 database name must match the ownership receipt.");
        Require(string.IsNullOrEmpty(sql.AttachDBFilename), "Attached database files are not Core test inputs.");
        Require(!sql.MultipleActiveResultSets, "Core transaction tests require SQL Server savepoints with MARS disabled.");
        sql.ApplicationName = "CP6Compat.WP4.CoreBusinessTests";
        sql.ConnectTimeout = 15;
        sql.CommandTimeout = 60;
        return sql.ConnectionString;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
