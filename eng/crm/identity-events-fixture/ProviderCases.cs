using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class IdentityProviderCase
{
    internal const string SetupCase = "current-provider-core-and-identity-priority-installed";
    internal const string FirstCase = "business-save-produces-valid-versioned-snapshots";

    public static async Task<int> RunAsync(string[] args)
    {
        var provider = args[3] switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new ArgumentException("An explicit SqlServer or PostgreSql provider is required.")
        };
        if (args[4] != FirstCase) throw new ArgumentException("This provider entry currently supports only the exact production snapshot/outbox case.");
        var evidence = new Evidence(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), args[4], provider);
        var fixture = new IdentitySqlFixture(new(provider));
        var exit = 0;
        try
        {
            await evidence.Case(SetupCase, fixture.InitializeCurrentProviderAsync);
            // Preserve the existing 23-case fixture's actual assertions and production Save pipeline.
            await evidence.Case(FirstCase, fixture.ValidSnapshotsAsync);
            evidence.MarkComplete();
        }
        catch (Exception) { exit = 1; }
        finally
        {
            // The external WP4 receipt owns the physical database. This fixture cannot drop it.
            try { await fixture.DisposeAsync(); }
            catch (Exception exception) { evidence.Failure("owned-provider-fixture-dispose", exception); exit = 1; }
            evidence.Save();
        }
        return exit;
    }
}

sealed partial class IdentitySqlFixture
{
    private readonly DatabaseOptions database = new(DatabaseProvider.SqlServer);

    public IdentitySqlFixture() { }
    public IdentitySqlFixture(DatabaseOptions database) => this.database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task InitializeCurrentProviderAsync()
    {
        var owner = Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER") ?? "";
        Require(Regex.IsMatch(owner, "\\A[a-f0-9]{32}\\z"), "C02_WP4_OWNER_REQUIRED");
        var expectedName = "CP6Compat_WP4_20261003_" + owner[..8];
        var configured = Environment.GetEnvironmentVariable(database.Provider == DatabaseProvider.SqlServer
            ? "CP6_C02_TEST_SQL" : "CP6_C02_TEST_POSTGRES");
        Require(!string.IsNullOrWhiteSpace(configured), "C02_SELECTED_PROVIDER_CONNECTION_REQUIRED");
        if (database.Provider == DatabaseProvider.PostgreSql)
        {
            var settings = new NpgsqlConnectionStringBuilder(configured!);
            Require(settings.Host is "localhost" or "127.0.0.1" or "::1"
                && settings.Database == expectedName, "C02_WP4_LOOPBACK_DATABASE_REQUIRED");
            settings.IncludeErrorDetail = false;
            settings.Pooling = false;
            connection = settings.ConnectionString;
        }
        else
        {
            var settings = new SqlConnectionStringBuilder(configured!);
            var source = settings.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? settings.DataSource[4..] : settings.DataSource;
            var host = source.Split('\\', ',')[0];
            Require(host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "." or "(local)"
                && settings.InitialCatalog == expectedName && !settings.MultipleActiveResultSets,
                "C02_WP4_LOOPBACK_DATABASE_REQUIRED");
            settings.Pooling = false;
            connection = settings.ConnectionString;
        }
        await using (var native = new DatabaseConnectionFactory(database).Create(connection))
        {
            await native.OpenAsync();
            var actual = await native.QuerySingleOrDefaultAsync<string>(database.Provider == DatabaseProvider.PostgreSql
                ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
                : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP4')");
            Require(actual == (database.Provider == DatabaseProvider.PostgreSql ? "DB-COMPAT-01-WP4:" + owner : owner),
                "C02_WP4_DATABASE_OWNER_MISMATCH");
        }

        await using var core = Create(Guid.NewGuid(), enabled: false);
        var coreAvailable = core.Database.GetMigrations().ToArray();
        Require(coreAvailable.Length == (database.Provider == DatabaseProvider.PostgreSql ? 1 : 140), "C02_CURRENT_CORE_MIGRATION_CHAIN_REQUIRED");
        if (database.Provider == DatabaseProvider.PostgreSql)
            Require(coreAvailable[0] == "20261002161715_PostgreSqlCoreBaselineV1", "C02_CURRENT_PG_CORE_BASELINE_REQUIRED");
        await MigrateKnownPrefixAsync(core, coreAvailable);
        Require(!core.Database.HasPendingModelChanges(), "C02_CORE_MODEL_HAS_UNMIGRATED_CHANGES");

        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.IdentityPriority);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<IdentityMessagingContext>(), database,
            connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        await using var priority = new IdentityMessagingContext(options);
        if (profile.MigrationOwner == DatabaseContextKind.IdentityPriority)
        {
            var available = priority.Database.GetMigrations().ToArray();
            Require(available.SequenceEqual(new[] { "20261002151342_PostgreSqlIdentityPriorityBaselineV1" }, StringComparer.Ordinal),
                "C02_CURRENT_PRIORITY_MIGRATION_CHAIN_REQUIRED");
            await MigrateKnownPrefixAsync(priority, available);
        }
        Require(!priority.Database.HasPendingModelChanges(), "C02_PRIORITY_MODEL_HAS_UNMIGRATED_CHANGES");
        // The real priority model/table must be usable even when SQL's Core owns its migration.
        _ = await priority.Set<CP6.Platform.EntityFramework.Cp6OutboxMessage>().CountAsync();
    }

    private static async Task MigrateKnownPrefixAsync(DbContext context, string[] available)
    {
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(before.Length <= available.Length && before.SequenceEqual(available.Take(before.Length), StringComparer.Ordinal),
            "C02_UNKNOWN_PROVIDER_MIGRATION_HISTORY");
        await context.Database.MigrateAsync();
        var after = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Require(after.SequenceEqual(available, StringComparer.Ordinal) && !(await context.Database.GetPendingMigrationsAsync()).Any(),
            "C02_PROVIDER_MIGRATION_HISTORY_NOT_EXACT");
    }
}
