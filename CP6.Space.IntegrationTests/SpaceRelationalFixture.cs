using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;
using CP6.Core.Services.Common;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

/// <summary>Explicit WP5 inputs use a runner-owned database; legacy SQL tests retain their own lifecycle.</summary>
public sealed class SpaceRelationalFixture : IAsyncLifetime
{
    public const string ProviderVariable = "CP6_SPACE_TEST_PROVIDER";
    public const string ConnectionVariable = "CP6_SPACE_TEST_CONNECTION";
    public const string OwnerVariable = "CP6_TEST_DATABASE_OWNER";
    private const string TaskName = "DB-COMPAT-01-WP5";
    private readonly DatabaseOptions? database;
    private readonly string? connectionString;
    private readonly string? owner;
    private readonly OwnedTestDatabase? ownedDatabase;

    public static bool IsSelected => Environment.GetEnvironmentVariable(ProviderVariable) is not null
        || Environment.GetEnvironmentVariable(ConnectionVariable) is not null
        || OwnedTestDatabase.IsRequestedForRole(DatabaseFixtureRole.Space);

    public SpaceRelationalFixture()
    {
        if (!IsSelected) return;
        database = new(Environment.GetEnvironmentVariable(ProviderVariable) switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("CP6_SPACE_TEST_PROVIDER must explicitly select SqlServer or PostgreSql; selected tests never skip.")
        });
        owner = Environment.GetEnvironmentVariable(OwnerVariable) ?? "";
        var supplied = Environment.GetEnvironmentVariable(ConnectionVariable) ?? "";
        ownedDatabase = OwnedTestDatabase.FromEnvironment(database, supplied,
            [DatabaseFixtureRole.Space], "CP6Compat.WP6.SpaceTests");
        if (ownedDatabase is not null)
        {
            connectionString = ownedDatabase.ConnectionString;
            return;
        }
        Require(Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "A WP5 database ownership receipt is required.");
        Require(!string.IsNullOrWhiteSpace(supplied), "CP6_SPACE_TEST_CONNECTION must identify the selected WP5 database.");
        connectionString = ValidateConnection(supplied);
    }

    public DatabaseOptions Database => database ?? throw new InvalidOperationException("No explicit Space database was selected.");
    public string ConnectionString => connectionString ?? throw new InvalidOperationException("No explicit Space database was selected.");
    public string SetupSummary { get; private set; } = "Space relational setup has not completed.";
    public IReadOnlyList<string> CoreMigrations { get; private set; } = [];
    public IReadOnlyList<string> SpaceMigrations { get; private set; } = [];

    public async Task InitializeAsync()
    {
        if (database is null) return;
        await VerifyOwnerAsync();
        await using var core = CreateCoreContext(TenantContext.DefaultTenant,
            new SpaceNativeFailureObserver(Database.Provider, Console.WriteLine, "core-migration"));
        await using var space = CreateSpaceContext(new FixtureExecution(), new FixtureClock(),
            new SpaceNativeFailureObserver(Database.Provider, Console.WriteLine, "space-migration"));

        // Reject either mixed/foreign history before applying any migration to the shared physical database.
        var coreExpected = await PreflightAsync(core, DatabaseContextKind.Core);
        var spaceExpected = await PreflightAsync(space, DatabaseContextKind.Space);
        CoreMigrations = await MigrateAsync(core, coreExpected);
        SpaceMigrations = await MigrateAsync(space, spaceExpected);
        SetupSummary = $"Provider={Database.Provider}; Core migrations={CoreMigrations.Count}; Space migrations={SpaceMigrations.Count}; pending=0; {(ownedDatabase is null ? "WP5" : "WP6")} owner verified; shared database.";
    }

    public SpaceContext CreateSpaceContext(ISpaceExecutionContext execution, ISpaceClock clock, params IInterceptor[] interceptors)
    {
        var context = new SpaceContext(Configure<SpaceContext>(DatabaseContextKind.Space, interceptors), execution, clock);
        context.Database.SetCommandTimeout(180);
        return context;
    }

    public CP6Context CreateCoreContext(Guid tenant, params IInterceptor[] interceptors)
    {
        var context = new CP6Context(Configure<CP6Context>(DatabaseContextKind.Core, interceptors),
            new TenantContext { CurrentTenantId = tenant });
        context.Database.SetCommandTimeout(180);
        return context;
    }

    public void WriteSetupEvidence(ITestOutputHelper output)
    {
        output.WriteLine(SetupSummary);
        output.WriteLine("CoreMigrationIds=" + string.Join(',', CoreMigrations));
        output.WriteLine("SpaceMigrationIds=" + string.Join(',', SpaceMigrations));
    }

    // Creation and deletion belong exclusively to the runner that holds the ownership receipt.
    public Task DisposeAsync() => Task.CompletedTask;

    private DbContextOptions<T> Configure<T>(DatabaseContextKind kind, IInterceptor[] interceptors) where T : DbContext
    {
        var profile = DatabaseMigrationProfile.For(Database, kind);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<T>(), Database, ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        return options.Options;
    }

    private async Task VerifyOwnerAsync()
    {
        if (ownedDatabase is not null)
        {
            await ownedDatabase.VerifyAsync();
            return;
        }
        await using var connection = new DatabaseConnectionFactory(Database).Create(ConnectionString);
        await connection.OpenAsync();
        var postgres = Database.Provider == DatabaseProvider.PostgreSql;
        var actual = await connection.QuerySingleOrDefaultAsync<string>(postgres
            ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
            : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP5')",
            commandTimeout: 30);
        Require(actual == (postgres ? TaskName + ":" + owner : owner),
            "Actual database task and owner must match the WP5 receipt before migrations or writes.");
    }

    private static async Task<string[]> PreflightAsync(DbContext context, DatabaseContextKind kind)
    {
        var available = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.Length > 0 && applied.Length <= available.Length && applied.SequenceEqual(available.Take(applied.Length)),
            $"The selected {kind} profile must have known provider migrations and an unmixed history.");
        return available;
    }

    private static async Task<string[]> MigrateAsync(DbContext context, string[] expected)
    {
        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(expected.SequenceEqual(applied) && !(await context.Database.GetPendingMigrationsAsync()).Any(),
            "The selected Space fixture context must finish all actual provider migrations.");
        return applied;
    }

    private string ValidateConnection(string supplied)
    {
        var expectedName = "CP6Compat_WP5_20261003_" + owner![..8];
        if (Database.Provider == DatabaseProvider.PostgreSql)
        {
            var pg = new NpgsqlConnectionStringBuilder(supplied);
            Require(pg.Host is "localhost" or "127.0.0.1" or "::1", "A literal loopback PostgreSQL host is required.");
            Require(pg.Database == expectedName, "The dedicated WP5 database name must match the ownership receipt.");
            Require(!pg.Multiplexing, "Space transaction tests require dedicated PostgreSQL connections.");
            pg.IncludeErrorDetail = false;
            pg.ApplicationName = "CP6Compat.WP5.SpaceTests";
            pg.Timeout = 15;
            pg.CommandTimeout = 60;
            using var canonical = new DatabaseConnectionFactory(Database).Create(pg.ConnectionString);
            return canonical.ConnectionString;
        }

        var sql = new SqlConnectionStringBuilder(supplied);
        var source = sql.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? sql.DataSource[4..] : sql.DataSource;
        var host = source.Split('\\', ',')[0];
        Require(host is "localhost" or "127.0.0.1" or "::1" or "[::1]", "A literal loopback SQL Server host is required.");
        Require(sql.InitialCatalog == expectedName, "The dedicated WP5 database name must match the ownership receipt.");
        Require(string.IsNullOrEmpty(sql.AttachDBFilename), "Attached database files are not Space test inputs.");
        Require(!sql.MultipleActiveResultSets, "Space transaction tests require SQL Server savepoints with MARS disabled.");
        sql.ApplicationName = "CP6Compat.WP5.SpaceTests";
        sql.ConnectTimeout = 15;
        sql.CommandTimeout = 60;
        return sql.ConnectionString;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixtureExecution : ISpaceExecutionContext
    {
        public Guid TenantId => TenantContext.DefaultTenant;
        public Guid ActorId => Guid.Empty;
        public bool IsExternal => false;
    }

    private sealed class FixtureClock : ISpaceClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}

/// <summary>Test-scoped native error evidence, including errors handled inside the clone processor.</summary>
internal sealed class SpaceNativeFailureObserver(DatabaseProvider provider, Action<string> report, string stage = "space-command")
    : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<string, byte> reported = new();

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Observe(command, eventData.Exception);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Observe(command, eventData.Exception);
        return Task.CompletedTask;
    }

    private void Observe(DbCommand command, Exception error)
    {
        var failure = DatabaseFailureClassifier.Classify(error);
        var commandStage = command.CommandText.Contains("DECLARE @SourceMap", StringComparison.OrdinalIgnoreCase)
            ? "clone-snapshot" : stage;
        var evidence = $"Provider={provider}; Stage={commandStage}; SQLSTATE={failure.SqlState ?? "none"}; NativeCode={failure.DatabaseErrorCode?.ToString() ?? "none"}; Kind={failure.Kind}.";
        if (reported.TryAdd(evidence, 0)) report(evidence);
    }
}
