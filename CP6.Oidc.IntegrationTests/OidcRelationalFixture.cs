using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Oidc.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OidcRelationalCollection : ICollectionFixture<OidcRelationalFixture>
{
    public const string Name = "OIDC required relational database";
}

/// <summary>Uses an existing task-owned database; never creates or removes databases.</summary>
public sealed class OidcRelationalFixture : IAsyncLifetime
{
    private const string TaskName = "DB-COMPAT-01-WP4";
    private readonly string _owner;
    private readonly OwnedTestDatabase? ownedDatabase;

    public OidcRelationalFixture()
    {
        Database = new DatabaseOptions(Environment.GetEnvironmentVariable("CP6_OIDC_TEST_PROVIDER") switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("CP6_OIDC_TEST_PROVIDER must select SqlServer or PostgreSql; required database tests never skip.")
        });
        _owner = Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER") ?? "";
        var connection = Environment.GetEnvironmentVariable("CP6_OIDC_TEST_CONNECTION") ?? "";
        ownedDatabase = OwnedTestDatabase.FromEnvironment(Database, connection,
            [DatabaseFixtureRole.OidcIdentity], "CP6Compat.WP6.OidcTests");
        if (ownedDatabase is not null)
        {
            ConnectionString = ownedDatabase.ConnectionString;
            return;
        }
        Require(Regex.IsMatch(_owner, "\\A[0-9a-f]{32}\\z"), "A WP4 database ownership receipt is required.");
        Require(!string.IsNullOrWhiteSpace(connection), "CP6_OIDC_TEST_CONNECTION must identify the selected WP4 test database.");
        ConnectionString = ValidateConnection(connection);
    }

    public DatabaseOptions Database { get; }
    public string ConnectionString { get; }
    public bool IsPostgreSql => Database.Provider == DatabaseProvider.PostgreSql;

    public async Task InitializeAsync()
    {
        await VerifyOwnerAsync();
        await using (var core = CreateContext(new TenantContext()))
            await MigrateAsync(core);
        var priorityProfile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.IdentityPriority);
        if (priorityProfile.MigrationOwner == DatabaseContextKind.IdentityPriority)
        {
            var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<IdentityMessagingContext>(),
                Database, ConnectionString, priorityProfile.MigrationsAssembly,
                priorityProfile.HistoryTable, priorityProfile.HistorySchema);
            await using var priority = new IdentityMessagingContext(options.Options);
            await MigrateAsync(priority);
        }
    }

    public CP6Context CreateContext(TenantContext tenant, IInterceptor? interceptor = null)
    {
        var profile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), Database,
            ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new CP6Context(options.Options, tenant);
    }

    public DbConnection CreateConnection() => new DatabaseConnectionFactory(Database).Create(ConnectionString);

    public ICrmOidcGrantStore CreateStore() => new SqlCrmOidcGrantStore(Database, ConnectionString);

    public string Table(string name) => IsPostgreSql ? "public." + Column(name) : "dbo." + Column(name);
    public string Column(string name) => IsPostgreSql ? '"' + name + '"' : '[' + name + ']';

    public Task DisposeAsync() => Task.CompletedTask;

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
        Require(actual == (IsPostgreSql ? TaskName + ":" + _owner : _owner),
            "Actual database task and owner must match the WP4 receipt before migrations or test writes.");
    }

    private static async Task MigrateAsync(DbContext context)
    {
        context.Database.SetCommandTimeout(180);
        var available = context.Database.GetMigrations().ToArray();
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.Length > 0 && before.Length <= available.Length && before.SequenceEqual(available.Take(before.Length)),
            "The selected context must have known migrations and an unmixed migration history.");
        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.SequenceEqual(applied) && !(await context.Database.GetPendingMigrationsAsync()).Any(),
            "The selected context must finish all actual provider migrations.");
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
            builder.ApplicationName = "CP6Compat.WP4.OidcTests";
            builder.Timeout = 15;
            builder.CommandTimeout = 30;
            return builder.ConnectionString;
        }

        var sql = new SqlConnectionStringBuilder(connection);
        var source = sql.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? sql.DataSource[4..] : sql.DataSource;
        var host = source.Split('\\', ',')[0];
        Require(host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "." or "(local)", "A literal loopback SQL Server host is required.");
        Require(Regex.IsMatch(sql.InitialCatalog, databasePattern), "A dedicated WP4 database name is required.");
        Require(!sql.MultipleActiveResultSets, "OIDC transaction tests require SQL Server savepoints with MARS disabled.");
        sql.ApplicationName = "CP6Compat.WP4.OidcTests";
        sql.ConnectTimeout = 15;
        sql.CommandTimeout = 30;
        return sql.ConnectionString;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
