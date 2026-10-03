using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Bug150RelationalCollection : ICollectionFixture<Bug150RelationalFixture>
{
    public const string Name = "BUG-150 mapping relational";
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class Bug150RelationalFactAttribute : FactAttribute
{
    public Bug150RelationalFactAttribute()
    {
        if (!Bug150RelationalFixture.IsSelected)
            Skip = "Set CP6_BUG150_TEST_PROVIDER and CP6_BUG150_TEST_CONNECTION to run the owned database regression.";
    }
}

public sealed class Bug150RelationalFixture : IAsyncLifetime
{
    private const string ProviderVariable = "CP6_BUG150_TEST_PROVIDER";
    private const string ConnectionVariable = "CP6_BUG150_TEST_CONNECTION";
    private readonly DatabaseOptions? database;
    private readonly string? connectionString;
    private readonly string? owner;
    private string[] coreMigrations = [];
    private string[] spaceMigrations = [];

    public static bool IsSelected => Environment.GetEnvironmentVariable(ProviderVariable) is not null
        || Environment.GetEnvironmentVariable(ConnectionVariable) is not null;

    public Bug150RelationalFixture()
    {
        if (!IsSelected) return;
        database = new(Environment.GetEnvironmentVariable(ProviderVariable) switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("CP6_BUG150_TEST_PROVIDER must explicitly select SqlServer or PostgreSql; selected tests cannot skip.")
        });
        owner = Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER") ?? "";
        Require(Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "A BUG-150 database owner token is required.");
        var supplied = Environment.GetEnvironmentVariable(ConnectionVariable) ?? "";
        Require(!string.IsNullOrWhiteSpace(supplied), "CP6_BUG150_TEST_CONNECTION is required for the selected provider.");
        try
        {
            connectionString = ValidateConnection(supplied);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException("The BUG-150 connection string is invalid for the selected provider.");
        }
    }

    public DatabaseOptions Database => database ?? throw new InvalidOperationException("No BUG-150 provider was selected.");
    private string ConnectionString => connectionString ?? throw new InvalidOperationException("No BUG-150 connection was selected.");

    public async Task InitializeAsync()
    {
        if (database is null) return;
        await VerifyOwnerAsync();
        await using var core = new CP6Context(Options<CP6Context>(DatabaseContextKind.Core),
            new TenantContext { CurrentTenantId = TenantContext.DefaultTenant });
        await using var space = CreateContext(new MigrationExecution(), new MigrationClock());
        core.Database.SetCommandTimeout(180);
        space.Database.SetCommandTimeout(180);
        // Check both histories before either context changes the shared database.
        var coreExpected = await PreflightAsync(core);
        var spaceExpected = await PreflightAsync(space);
        coreMigrations = await MigrateAsync(core, coreExpected);
        spaceMigrations = await MigrateAsync(space, spaceExpected);
    }

    public SpaceContext CreateContext(ISpaceExecutionContext execution, ISpaceClock clock, params IInterceptor[] interceptors)
    {
        var context = new SpaceContext(Options<SpaceContext>(DatabaseContextKind.Space, interceptors), execution, clock);
        context.Database.SetCommandTimeout(45);
        return context;
    }

    public void WriteEvidence(ITestOutputHelper output)
    {
        output.WriteLine($"Task=BUG-150; Provider={Database.Provider}; owner verified; Core={coreMigrations.Length}; Space={spaceMigrations.Length}; pending=0.");
        output.WriteLine("CoreMigrationIds=" + string.Join(',', coreMigrations));
        output.WriteLine("SpaceMigrationIds=" + string.Join(',', spaceMigrations));
    }

    // The runner owns the database lifecycle; this fixture never creates or drops it.
    public Task DisposeAsync() => Task.CompletedTask;

    private DbContextOptions<T> Options<T>(DatabaseContextKind kind, params IInterceptor[] interceptors) where T : DbContext
    {
        var profile = DatabaseMigrationProfile.For(Database, kind);
        var builder = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<T>(), Database, ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        if (interceptors.Length != 0) builder.AddInterceptors(interceptors);
        return builder.Options;
    }

    private async Task VerifyOwnerAsync()
    {
        await using var connection = new DatabaseConnectionFactory(Database).Create(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var postgres = Database.Provider == DatabaseProvider.PostgreSql;
        command.CommandText = postgres
            ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
            : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'BUG-150')";
        command.CommandTimeout = 30;
        var actual = await command.ExecuteScalarAsync() as string;
        Require(actual == (postgres ? "BUG-150:" + owner : owner), "The actual database task and owner must match BUG-150 before migrations or writes.");
    }

    private static async Task<string[]> PreflightAsync(DbContext context)
    {
        var available = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.Length > 0 && applied.Length <= available.Length && applied.SequenceEqual(available.Take(applied.Length)),
            "The BUG-150 database must have an unmixed history belonging to its actual provider migration profile.");
        return available;
    }

    private static async Task<string[]> MigrateAsync(DbContext context, string[] expected)
    {
        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(expected.SequenceEqual(applied) && !(await context.Database.GetPendingMigrationsAsync()).Any(),
            "All actual BUG-150 provider migrations must be applied, with none pending.");
        return applied;
    }

    private string ValidateConnection(string supplied)
    {
        var expectedName = "CP6Bug150_20261003_" + owner![..8];
        if (Database.Provider == DatabaseProvider.PostgreSql)
        {
            var pg = new NpgsqlConnectionStringBuilder(supplied);
            Require(pg.Host is "localhost" or "127.0.0.1" or "::1", "BUG-150 requires a literal loopback PostgreSQL host.");
            Require(pg.Database == expectedName, "The BUG-150 database name must match the owner token.");
            Require(!pg.Multiplexing, "Concurrency regression requires dedicated PostgreSQL sessions.");
            pg.IncludeErrorDetail = false;
            pg.ApplicationName = "CP6.BUG150.MappingTests";
            pg.Timeout = 15;
            pg.CommandTimeout = 45;
            using var canonical = new DatabaseConnectionFactory(Database).Create(pg.ConnectionString);
            return canonical.ConnectionString;
        }

        var sql = new SqlConnectionStringBuilder(supplied);
        var source = sql.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? sql.DataSource[4..] : sql.DataSource;
        var host = source.Split('\\', ',')[0];
        Require(host is "localhost" or "127.0.0.1" or "::1" or "[::1]", "BUG-150 requires a literal loopback SQL Server host.");
        Require(sql.InitialCatalog == expectedName, "The BUG-150 database name must match the owner token.");
        Require(string.IsNullOrEmpty(sql.AttachDBFilename) && string.IsNullOrEmpty(sql.FailoverPartner), "Attached files and failover partners are not BUG-150 inputs.");
        Require(!sql.MultipleActiveResultSets, "BUG-150 requires MARS disabled for transaction rollback.");
        sql.ApplicationName = "CP6.BUG150.MappingTests";
        sql.ConnectTimeout = 15;
        sql.CommandTimeout = 45;
        return sql.ConnectionString;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record MigrationExecution : ISpaceExecutionContext
    {
        public Guid TenantId => TenantContext.DefaultTenant;
        public Guid ActorId => Guid.Empty;
    }

    private sealed class MigrationClock : ISpaceClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
