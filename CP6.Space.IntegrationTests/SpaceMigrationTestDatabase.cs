using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

/// <summary>Space-only migration inputs whose database lifecycle belongs to the runner.</summary>
public sealed class SpaceMigrationTestDatabase
{
    public const string ProviderVariable = "CP6_SPACE_MIGRATION_TEST_PROVIDER";
    public const string ConnectionVariable = "CP6_SPACE_MIGRATION_TEST_CONNECTION";
    private const string OwnerVariable = "CP6_TEST_DATABASE_OWNER";
    private const string TaskName = "DB-COMPAT-01-WP5";
    private readonly string owner;
    private readonly ITestOutputHelper output;

    private SpaceMigrationTestDatabase(DatabaseOptions database, string connection, string owner, ITestOutputHelper output)
    {
        Database = database;
        ConnectionString = connection;
        this.owner = owner;
        this.output = output;
    }

    public static bool IsSelected => Environment.GetEnvironmentVariable(ProviderVariable) is not null
        || Environment.GetEnvironmentVariable(ConnectionVariable) is not null;

    public DatabaseOptions Database { get; }
    public string ConnectionString { get; }

    public static async Task<SpaceMigrationTestDatabase?> OpenIfSelectedAsync(
        ITestOutputHelper output, bool allowPostgreSql = false)
    {
        if (!IsSelected) return null;
        var (database, connection, owner) = ValidateSelectedInputs();
        Require(allowPostgreSql || database.Provider == DatabaseProvider.SqlServer,
            "This historical SQL script case requires SqlServer; PostgreSql has its own Space-only schema case.");
        var selected = new SpaceMigrationTestDatabase(database, connection, owner, output);
        await selected.VerifyOwnerAsync();
        return selected;
    }

    public SpaceContext CreateContext(ISpaceExecutionContext execution, ISpaceClock clock)
    {
        var profile = DatabaseMigrationProfile.For(Database, DatabaseContextKind.Space);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<SpaceContext>(), Database,
            ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        options.AddInterceptors(new SpaceNativeFailureObserver(Database.Provider, output.WriteLine, "space-only-migration"));
        var context = new SpaceContext(options.Options, execution, clock);
        context.Database.SetCommandTimeout(180);
        return context;
    }

    public async Task MigrateAsync(SpaceContext context, string? historicalStart = null)
    {
        Require(context.Database.GetConnectionString() == ConnectionString,
            "Migration contexts must use the exact runner-owned connection.");
        var available = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(available.Length > 0 && applied.Length <= available.Length
            && applied.SequenceEqual(available.Take(applied.Length)), "Unknown or mixed Space migration history is forbidden.");
        var targetCount = historicalStart is null ? available.Length : Array.IndexOf(available, historicalStart) + 1;
        Require(targetCount > 0, "The historical start must belong to this provider's actual Space profile.");
        Require(applied.Length <= targetCount, "A migration test must never downgrade an already later database.");
        Require(historicalStart is null || applied.Length == 0 || applied.Length == targetCount,
            "Historical cases require an empty database or the exact historical start.");

        await using (var connection = new DatabaseConnectionFactory(Database).Create(ConnectionString))
        {
            await connection.OpenAsync();
            var tables = (await connection.QueryAsync<string>(Database.Provider == DatabaseProvider.PostgreSql
                ? "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname='public'"
                : "SELECT [name] FROM sys.tables WHERE is_ms_shipped=0", commandTimeout: 30)).ToArray();
            Require(tables.All(name => name == SpaceContext.MigrationsHistoryTable
                || name.StartsWith("Space_", StringComparison.Ordinal)), "Only a Space-only catalog may enter the migration lane.");
            Require(!tables.Contains("Space_Site", StringComparer.Ordinal), "Core runtime tables are forbidden in the Space-only lane.");
            Require(applied.Length != 0 || tables.All(name => name == SpaceContext.MigrationsHistoryTable),
                "An unmigrated runner-owned database may contain only its empty Space history table.");
        }

        output.WriteLine($"Provider={Database.Provider}; Task={TaskName}; Owner={owner}; Target={historicalStart ?? "latest"}; Lifecycle=runner-owned; CoreProfile=not-applied.");
        await RecordMigrationSourcesAsync(available);
        await RecordStateAsync(context, "before-migration");
        await context.Database.GetService<IMigrator>().MigrateAsync(historicalStart);
        var actual = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(actual.SequenceEqual(available.Take(targetCount)), "Actual Space migration history must equal the requested provider target.");
        await RecordStateAsync(context, "after-migration");
    }

    public async Task RecordStateAsync(SpaceContext context, string stage)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        output.WriteLine($"Provider={Database.Provider}; Stage={stage}; AppliedCount={applied.Length}; PendingCount={pending.Length}.");
        output.WriteLine("AppliedMigrationIds=" + string.Join(',', applied));
        output.WriteLine("PendingMigrationIds=" + string.Join(',', pending));
    }

    public static async Task<string> ReadScriptAsync(string relativePath, ITestOutputHelper output)
    {
        var path = Path.Combine(RepositoryRoot, relativePath);
        var bytes = await File.ReadAllBytesAsync(path);
        output.WriteLine($"Script={relativePath}; SHA256={Convert.ToHexString(SHA256.HashData(bytes))}.");
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    // Pure input validation also runs during discovery, before a mixed shared fixture could migrate Core.
    internal static (DatabaseOptions Database, string Connection, string Owner) ValidateSelectedInputs()
    {
        foreach (var prefix in new[] { "CP6_SPACE_TEST_", "CP6_WP5_CORE_TEST_", "CP6_CORE_TEST_" })
            Require(Environment.GetEnvironmentVariable(prefix + "PROVIDER") is null
                && Environment.GetEnvironmentVariable(prefix + "CONNECTION") is null,
                "Space migration inputs must not be mixed with shared Space or Core relational inputs.");
        var database = new DatabaseOptions(Environment.GetEnvironmentVariable(ProviderVariable) switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("CP6_SPACE_MIGRATION_TEST_PROVIDER must select SqlServer or PostgreSql.")
        });
        var owner = Environment.GetEnvironmentVariable(OwnerVariable) ?? "";
        Require(Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "A WP5 ownership receipt is required.");
        var supplied = Environment.GetEnvironmentVariable(ConnectionVariable) ?? "";
        Require(!string.IsNullOrWhiteSpace(supplied), "The selected migration lane requires its runner-owned connection.");
        var expectedName = "CP6Compat_WP5_20261003_" + owner[..8];
        if (database.Provider == DatabaseProvider.PostgreSql)
        {
            var pg = new NpgsqlConnectionStringBuilder(supplied);
            Require(pg.Host is "localhost" or "127.0.0.1" or "::1", "A literal loopback PostgreSQL host is required.");
            Require(pg.Database == expectedName && !pg.Multiplexing, "The migration database must match its owner and use dedicated connections.");
            pg.IncludeErrorDetail = false;
            pg.ApplicationName = "CP6Compat.WP5.SpaceMigrations";
            pg.Timeout = 15;
            pg.CommandTimeout = 60;
            using var canonical = new DatabaseConnectionFactory(database).Create(pg.ConnectionString);
            return (database, canonical.ConnectionString, owner);
        }

        var sql = new SqlConnectionStringBuilder(supplied);
        var source = sql.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? sql.DataSource[4..] : sql.DataSource;
        Require(source.Split('\\', ',')[0] is "localhost" or "127.0.0.1" or "::1" or "[::1]", "A literal loopback SQL Server host is required.");
        Require(sql.InitialCatalog == expectedName && string.IsNullOrEmpty(sql.AttachDBFilename)
            && !sql.MultipleActiveResultSets, "The migration database must match its owner, without attached files or MARS.");
        sql.ApplicationName = "CP6Compat.WP5.SpaceMigrations";
        sql.ConnectTimeout = 15;
        sql.CommandTimeout = 60;
        return (database, sql.ConnectionString, owner);
    }

    private async Task VerifyOwnerAsync()
    {
        await using var connection = new DatabaseConnectionFactory(Database).Create(ConnectionString);
        await connection.OpenAsync();
        var postgres = Database.Provider == DatabaseProvider.PostgreSql;
        var actual = await connection.QuerySingleOrDefaultAsync<string>(postgres
            ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
            : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP5')",
            commandTimeout: 30);
        Require(actual == (postgres ? TaskName + ":" + owner : owner), "Actual database task and owner must match before migration writes.");
    }

    private async Task RecordMigrationSourcesAsync(IEnumerable<string> migrations)
    {
        var directory = Database.Provider == DatabaseProvider.PostgreSql
            ? "CP6.Persistence.PostgreSql/Migrations/Space" : "CP6.Space.Infrastructure/Migrations";
        foreach (var migration in migrations)
        {
            var path = Path.Combine(directory, migration + ".cs");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(RepositoryRoot, path));
            output.WriteLine($"MigrationSource={path}; SHA256={Convert.ToHexString(SHA256.HashData(bytes))}.");
        }
    }

    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class SpaceMigrationFactAttribute : FactAttribute
{
    public SpaceMigrationFactAttribute(bool allowPostgreSql = false)
    {
        AllowPostgreSql = allowPostgreSql;
        if (SpaceMigrationTestDatabase.IsSelected)
            _ = SpaceMigrationTestDatabase.ValidateSelectedInputs();
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar)))
            Skip = "Select the owned Space migration lane or CP6_TEST_SQLSERVER for the legacy SQL lifecycle.";
    }

    // Provider eligibility is enforced when the selected case opens its database, not while discovering other cases.
    public bool AllowPostgreSql { get; }
}
