using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("WP2 real full migration probe: --provider SqlServer|PostgreSql --output PATH [--source-sha SHA].");
    Console.WriteLine("Requires task-owned loopback WP2 test database; applies forward migrations there only.");
    return 0;
}
string? Arg(string name) => Array.IndexOf(args, name) is var i && i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
var providerName = Arg("--provider");
var pg = providerName == "PostgreSql";
var output = Path.GetFullPath(Arg("--output") ?? throw new ArgumentException("Report destination is required."));
var started = DateTime.UtcNow;
var selectedModes = new[] { "--seed-mode", "--raw-order-only", "--constraint-writes-only", "--order-relations-only", "--quotation-audit-only", "--sql-upgrade", "--catalog-only", "--history-negative-only" }
    .Where(args.Contains).ToArray();
var conflictingModes = selectedModes.Length > 1;
var argumentsValidated = false;
var checks = new List<MigrationCheck>();
var modelCounts = new List<object>();
string? databaseVersion = null;
async Task Check(string name, Func<Task<string>> action)
{
    try { checks.Add(new(name, "Passed", await action())); }
    catch (Exception exception) { checks.Add(new(name, "Failed", SafeError(exception))); }
    Console.WriteLine($"{checks[^1].Status}: {name}: {checks[^1].Detail}");
}
try
{
    Require(!conflictingModes, "Probe modes are mutually exclusive; conflicting modes are rejected before database access.");
    Require(providerName is "SqlServer" or "PostgreSql", "Exact database provider is required.");
    if (args.Contains("--seed-mode"))
    {
        Require(Arg("--seed-mode") is "prepare" or "capture" or "verify", "An explicit prepare, capture or verify seed mode is required before database access.");
        Require(Arg("--seed-state") is { Length: > 0 } seedState && !seedState.StartsWith("--", StringComparison.Ordinal),
            "An explicit seed state path is required before database access.");
    }
    argumentsValidated = true;
    var owner = Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER");
    var connectionString = Environment.GetEnvironmentVariable(pg ? "CP6_TEST_POSTGRES" : "CP6_TEST_SQLSERVER");
    var database = new DatabaseOptions(pg ? DatabaseProvider.PostgreSql : DatabaseProvider.SqlServer);
    var ownedDatabase = OwnedTestDatabase.FromEnvironment(database, connectionString,
        [DatabaseFixtureRole.Schema, DatabaseFixtureRole.SqlUpgrade, DatabaseFixtureRole.Application, DatabaseFixtureRole.Restore],
        "CP6Compat.WP6.MigrationProbe");
    if (ownedDatabase is not null)
    {
        connectionString = ownedDatabase.ConnectionString;
        await ownedDatabase.VerifyAsync();
    }
    else
    {
        Require(owner is not null && Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "Task owner receipt is required before connection.");
        Require(!string.IsNullOrWhiteSpace(connectionString), "Task test connection environment is required.");
        if (pg)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            Require(builder.Host is "localhost" or "127.0.0.1" or "::1", "Literal loopback is required.");
            Require(Regex.IsMatch(builder.Database ?? "", "\\ACP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}\\z"), "Dedicated WP2 database is required.");
            builder.IncludeErrorDetail = false; builder.ApplicationName = "CP6Compat.WP2.MigrationProbe";
            connectionString = builder.ConnectionString;
        }
        else
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            Require(builder.DataSource == "localhost\\KOUSQLSERVER", "Recorded loopback SQL instance is required.");
            Require(Regex.IsMatch(builder.InitialCatalog, "\\ACP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}\\z"), "Dedicated WP2 database is required.");
        }
    }
    await using var connection = new DatabaseConnectionFactory(database).Create(connectionString!);
    await connection.OpenAsync();
    if (ownedDatabase is null)
    {
        var actualOwner = await connection.QuerySingleOrDefaultAsync<string>(pg
            ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
            : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')");
        Require(actualOwner == (pg ? $"DB-COMPAT-01-WP2:{owner}" : owner), "Database ownership metadata must match before migration.");
    }
    databaseVersion = connection.ServerVersion;
    if (pg) Require(int.Parse(databaseVersion.Split('.')[0]) == 18, "This baseline requires the accepted PostgreSQL 18 target.");
    if (pg) Require(await connection.QuerySingleAsync<string>("SELECT current_setting('search_path')") == "public",
        "The actual application connection must pin PostgreSQL search_path to public.");
    checks.Add(new("Isolation.OwnerAndProvider", "Passed", OwnedTestDatabase.IsRequested() ? "Recorded WP6 loopback, dedicated name, role and task ownership verified before migration." : "Recorded loopback, dedicated name and task ownership verified before migration."));

    var seedMode = Arg("--seed-mode");
    if (seedMode is not null)
    {
        await Check($"Seed.{seedMode}", () => Wp2SeedGates.RunAsync(connection, pg, seedMode,
            Path.GetFullPath(Arg("--seed-state") ?? throw new ArgumentException("Seed state destination required."))));
    }
    else if (args.Contains("--raw-order-only"))
    {
        await Check("Catalog.RawOrderDefaultsAndGlobalUniqueness", () => Wp2RawOrderGates.VerifyAsync(connection, pg));
    }
    else
    {
    var catalogOnly = args.Contains("--catalog-only");
    DbContextOptions<T> Options<T>(DatabaseContextKind kind) where T : DbContext
    {
        var profile = DatabaseMigrationProfile.For(database, kind);
        return DatabaseContextOptions.Configure(new DbContextOptionsBuilder<T>(), database, connection,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
    }
    await using var core = new CP6Context(Options<CP6Context>(DatabaseContextKind.Core), new ProbeTenant());
    await using var space = new SpaceContext(Options<SpaceContext>(DatabaseContextKind.Space), new ProbeExecution(), new SystemSpaceClock());
    await using var identity = new IdentityMessagingContext(Options<IdentityMessagingContext>(DatabaseContextKind.IdentityPriority));
    await using var erp = new ErpIntegrationContext(Options<ErpIntegrationContext>(DatabaseContextKind.ErpIntegration));
    if (args.Contains("--history-negative-only"))
    {
        await Check("History.UnknownIdentityRollbackNegativeControl", () => Wp2MigrationHistoryGates.VerifyNegativeControlAsync(core, connection, pg));
    }
    else if (args.Contains("--sql-upgrade"))
    {
        Require(!pg && !catalogOnly, "The supported legacy SQL upgrade mode requires SQL Server and forward migration mode.");
        await Check("Upgrade.SqlServer.Populated136To140", () => Wp2SqlUpgradeGates.VerifyAsync(connection, core));
    }
    else if (args.Contains("--constraint-writes-only"))
    {
        await Check("Write.FinancialMoneyAndForeignKey", () => Wp2ConstraintWriteGates.MoneyAndForeignKeyAsync(core, connection, pg));
        await Check("Write.SpaceTenantForeignKeyAndUnicode", () => Wp2ConstraintWriteGates.SpaceTenantForeignKeyAndUnicodeAsync(space, connection, pg));
    }
    else if (args.Contains("--order-relations-only"))
    {
        await Check("Write.OrderForeignKeysAndCascades", () => Wp2OrderRelationWriteGates.VerifyAsync(core, connection, pg));
    }
    else if (args.Contains("--quotation-audit-only"))
    {
        await Check("Write.QuotationAuditUnicodeCapacity", () => Wp2QuotationAuditWriteGates.VerifyAsync(core, connection, pg));
    }
    else
    {
    (string Name, DbContext Context)[] contexts = [("Core", core), ("Space", space), ("IdentityPriority", identity), ("ErpIntegration", erp)];
    foreach (var (name, context) in contexts)
    {
        var coreOwnsHistory = !pg && name is "IdentityPriority" or "ErpIntegration";
        if (catalogOnly)
        {
            checks.Add(new($"Catalog.{name}.Mode", "Passed", "Read-only catalog mode; migration history was not changed or claimed current."));
        }
        else if (coreOwnsHistory)
        {
            await Check($"Migration.{name}.CoreOwnership", async () =>
            {
                var applied = (await core.Database.GetAppliedMigrationsAsync()).ToHashSet(StringComparer.Ordinal);
                var expected = name == "IdentityPriority" ? new[] { "20260910043605_CrmIdentityEvents" }
                    : new[] { "20260912071606_CrmErpIntegration", "20260912074643_CrmErpReplayAudit", "20260912085508_CrmErpDeliveryReplayAudit" };
                Require(expected.All(applied.Contains), "Immutable SQL Core owner history must contain every queue installation wrapper.");
                return "SQL Core canonical history owns installed queue operations; standalone template identities are not falsely inserted as applied.";
            });
        }
        else
        {
        await Check($"Migration.{name}.Forward", async () =>
        {
            var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var available = context.Database.GetMigrations().ToArray();
            Wp2MigrationHistoryGates.RequireSupportedPrefix(before, available);
            await context.Database.MigrateAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
            Wp2MigrationHistoryGates.RequireExact(applied, available);
            Require(pending.Length == 0, "Expected exact applied migration set and zero pending migrations.");
            return $"Applied={applied.Length}, previouslyApplied={before.Length}, pending=0.";
        });
        if (checks[^1].Status != "Passed") break;
        await Check($"Migration.{name}.Repeat", async () =>
        {
            var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Wp2MigrationHistoryGates.RequireExact(before, context.Database.GetMigrations().ToArray());
            await context.Database.MigrateAsync();
            Require((await context.Database.GetAppliedMigrationsAsync()).SequenceEqual(before), "Repeated migration must keep the exact history set.");
            return "Repeated real migration retained the same applied history.";
        });
        }
        var model = context.GetService<IDesignTimeModel>().Model;
        var tables = model.GetRelationalModel().Tables.ToArray();
        var tokens = model.GetEntityTypes().SelectMany(entity => entity.GetProperties()).Where(property =>
            property.ClrType == typeof(byte[]) && property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate)
            .Select(property => (Schema: ((IEntityType)property.DeclaringType).GetSchema() ?? (pg ? "public" : "dbo"), Table: ((IEntityType)property.DeclaringType).GetTableName()!, Column: property.GetColumnName()))
            .Distinct().ToArray();
        modelCounts.Add(new { Context = name, Tables = tables.Length, TokenTables = tokens.Length });
        await Check($"Catalog.{name}.EveryTableAndColumn", async () =>
        {
            foreach (var table in tables)
            {
                var schema = table.Schema ?? (pg ? "public" : "dbo");
                var columns = (await connection.QueryAsync<string>(pg
                    ? "SELECT a.attname FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema AND c.relname=@table AND a.attnum>0 AND NOT a.attisdropped"
                    : "SELECT c.name FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=@schema AND t.name=@table", new { schema, table = table.Name })).Order().ToArray();
                Require(columns.SequenceEqual(table.Columns.Select(column => column.Name).Order()), "Every model table must have exactly its expected catalog columns.");
            }
            return $"Verified every column in {tables.Length} actual model tables.";
        });
        await Check($"Catalog.{name}.EveryDatabaseToken", async () =>
        {
            foreach (var token in tokens)
            {
                var count = await connection.QuerySingleAsync<int>(pg
                    ? "SELECT count(*)::integer FROM pg_trigger tr JOIN pg_class t ON t.oid=tr.tgrelid JOIN pg_namespace s ON s.oid=t.relnamespace WHERE s.nspname=@Schema AND t.relname=@Table AND tr.tgname='cp6_rowversion_v1' AND tr.tgenabled='O' AND NOT tr.tgisinternal"
                    : "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE s.name=@Schema AND t.name=@Table AND c.name=@Column AND ty.name='timestamp' AND c.max_length=8",
                    new { token.Schema, token.Table, token.Column });
                Require(count == 1, "Every generated token table must have the actual expected database generator.");
            }
            return $"Verified {tokens.Length} native token generators; no application-only token substitute.";
        });
        await Check($"Catalog.{name}.ModelConstraintsAndStorage", async () =>
        {
            await Wp2CatalogGates.VerifyAsync(connection, pg, model);
            return "Actual model column types/capacity/precision/nullability, ordered keys/FKs/indexes, filters and check names/validation/column references verified; arbitrary check expression behavior is separate.";
        });
        if (args.Contains("--write-gates") && checks.All(check => check.Status == "Passed"))
            await Check($"Write.{name}.InstalledTokens", () => Wp2WriteGates.TokensAsync(context, connection, pg, name));
    }
    if (checks.All(check => check.Status == "Passed"))
    {
        await Check("Catalog.RawOrderDefaultsAndGlobalUniqueness", () => Wp2RawOrderGates.VerifyAsync(connection, pg));
        await Check("Catalog.ManagedOidcLedger", async () =>
        {
            var count = await connection.QuerySingleAsync<int>(pg
                ? "SELECT count(*)::integer FROM pg_tables WHERE schemaname='public' AND tablename IN('CrmOidcGrant','CrmOidcLogout')"
                : "SELECT COUNT(*) FROM sys.tables WHERE SCHEMA_NAME(schema_id)='dbo' AND name IN('CrmOidcGrant','CrmOidcLogout')");
            Require(count == 2, "Both operational OIDC tables outside EF snapshots must be installed.");
            return "Both OIDC ledger tables are installed.";
        });
        await Check("Catalog.ManagedJournalGuard", async () =>
        {
            var count = await connection.QuerySingleAsync<int>(pg
                ? "SELECT count(*)::integer FROM pg_trigger tr JOIN pg_class t ON t.oid=tr.tgrelid JOIN pg_namespace s ON s.oid=t.relnamespace WHERE s.nspname='public' AND t.relname='Fin_JournalLine' AND tr.tgname='trg_FinJournalLine_NoMutate' AND tr.tgenabled='O'"
                : "SELECT COUNT(*) FROM sys.triggers WHERE name='trg_FinJournalLine_NoMutate' AND is_disabled=0");
            Require(count == 1, "The finance journal database guard must be installed and enabled.");
            return "Posted-journal mutation guard is present and enabled; write behavior is a separate gate.";
        });
        if (args.Contains("--write-gates"))
        {
            await Check("Write.IdentityGeneration", () => Wp2WriteGates.GenerationAsync(connection, pg));
            await Check("Write.LanguageNullAndTenantUniqueness", () => Wp2WriteGates.LanguageUniqueAsync(connection, pg));
            await Check("Write.TimeDateAndNumericBoundaries", () => Wp2WriteGates.TimeAndNumbersAsync(core, connection, pg));
            await Check("Write.FinancialMoneyAndForeignKey", () => Wp2ConstraintWriteGates.MoneyAndForeignKeyAsync(core, connection, pg));
            await Check("Write.SpaceTenantForeignKeyAndUnicode", () => Wp2ConstraintWriteGates.SpaceTenantForeignKeyAndUnicodeAsync(space, connection, pg));
            await Check("Write.OrderForeignKeysAndCascades", () => Wp2OrderRelationWriteGates.VerifyAsync(core, connection, pg));
            await Check("Write.QuotationAuditUnicodeCapacity", () => Wp2QuotationAuditWriteGates.VerifyAsync(core, connection, pg));
            await Check("Write.FinancePostingMutationRace", () => Wp2FinanceGates.VerifyRaceAsync(connection, pg, connectionString!));
        }
        if (args.Contains("--catalog-negative-control"))
            await Check("Catalog.MissingIndexRollbackNegativeControl", () => Wp2CatalogNegativeControls.VerifyAsync(connection, pg, core.GetService<IDesignTimeModel>().Model));
    }
    }
    }
}
catch (Exception exception) { checks.Add(new("Probe.SetupOrRun", "Failed", SafeError(exception))); Console.WriteLine($"Failed: Probe.SetupOrRun: {SafeError(exception)}"); }
finally
{
    var binaries = new SortedDictionary<string, string>();
    foreach (var type in new[] { typeof(Wp2WriteGates), typeof(CP6.Platform.EntityFramework.Cp6InboxAggregateCheckpoint), typeof(CP6Context), typeof(SpaceContext), typeof(CP6.Persistence.PostgreSql.PostgreSqlMigrationsAssembly), typeof(DbContext), typeof(NpgsqlConnection), typeof(SqlConnection) })
        binaries[type.Assembly.GetName().Name!] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(type.Assembly.Location)));
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var scope = conflictingModes ? "Rejected conflicting probe modes before database access; no migration or write performed"
        : !argumentsValidated ? "Rejected invalid probe arguments before database access; no migration or write performed"
        : args.Contains("--history-negative-only") ? "Actual Core history API rejected a transaction-only unknown migration identity; exact complete original history restored, no migration applied"
        : Arg("--seed-mode") is not null ? "Actual application seed first/repeat native verification and retained custom fixtures"
        : args.Contains("--raw-order-only") ? "Independent native catalog/default-expression verification of 65 restored order defaults and four retained global unique indexes"
        : args.Contains("--constraint-writes-only") ? "Actual financial storage boundaries and tenant-composite Space FK/Unicode constraint writes; no migration currency or full business acceptance claimed"
        : args.Contains("--order-relations-only") ? "Actual Order EF graph, four native orphan rejections and four delete cascades with complete fixture rollback; no migration currency or full order business acceptance claimed"
        : args.Contains("--quotation-audit-only") ? "Actual three-row Quotation EF graph and six native Creator/Modifier UTF-16 storage boundaries with exact error identities, separate excess-trailing-space behavior and complete fixture rollback; no migration currency or full quotation business acceptance claimed"
        : args.Contains("--sql-upgrade") ? "Real populated SQL Server supported Core136-to140 generation/index/FK/quotation-capacity forward upgrade and retained data/token/generation checks"
        : args.Contains("--catalog-only") ? "Read-only installed catalog validation; migration currency not claimed"
        : "WP2 real four-context migration/catalog installation and explicitly named representative write gates; full business and restoration gates separate";
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { Scope = OwnedTestDatabase.IsRequested() ? "WP6 runner-owned database: " + scope : scope, Provider = providerName,
        StartedUtc = started, FinishedUtc = DateTime.UtcNow, SourceBase = Arg("--source-sha"), SourceState = OwnedTestDatabase.IsRequested() ? "WP6 runner task input; applicable build log, source-file and runtime-binary hashes required" : "WP2 uncommitted task input; applicable build log and file hashes required", DatabaseVersion = databaseVersion,
        RuntimeBinaries = binaries, ModelCounts = modelCounts, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
}
return checks.Any(check => check.Status == "Failed") ? 1 : 0;

static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
static string SafeError(Exception exception) => exception switch
{
    MigrationAssertionException assertion => assertion.Message,
    CatalogAssertionException catalog => catalog.Message,
    PostgresException postgres => $"PostgresException SQLSTATE={postgres.SqlState}; object={postgres.TableName ?? postgres.ConstraintName ?? "unspecified"}",
    SqlException sql => $"SqlException Number={sql.Number}",
    _ => exception.GetType().Name
};
sealed record MigrationCheck(string Name, string Status, string Detail);
sealed class MigrationAssertionException(string detail) : Exception(detail);
sealed class ProbeTenant : ITenantContext { public Guid CurrentTenantId { get; set; } = Guid.Parse("bc5db77c-57ae-4e3f-880a-abba5c06aa1b"); }
sealed class ProbeExecution : ISpaceExecutionContext
{
    public Guid TenantId => Guid.Parse("bc5db77c-57ae-4e3f-880a-abba5c06aa1b");
    public Guid ActorId => Guid.Parse("8f690e8f-c991-4568-bffd-7564625800aa");
}
