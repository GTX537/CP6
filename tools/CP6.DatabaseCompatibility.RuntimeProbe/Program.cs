using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.RuntimeProbe;
using CP6.DatabaseCompatibility.Testing;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

if (args.Contains("--identity-cursor-child"))
    return await IdentityCursorGates.ChildAsync(args);

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("WP3 native runtime probe: --provider SqlServer|PostgreSql --output PATH --suite orders|orders-extra|locks|failures|shared|cursor|claim [--case immediate|all] [--initialize] [--source-sha SHA]");
    Console.WriteLine("Requires an explicitly owned loopback WP3 test database; credentials are read only from test environment variables.");
    return 0;
}
string? Arg(string key) => Array.IndexOf(args, key) is var index && index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
var provider = Arg("--provider");
var output = Path.GetFullPath(Arg("--output") ?? throw new ArgumentException("Output is required."));
if (File.Exists(output)) throw new InvalidOperationException("Existing evidence must be preserved.");
var started = DateTime.UtcNow;
var checks = new List<RuntimeCheck>();
string? serverVersion = null;
async Task Check(string name, Func<Task<string>> action)
{
    try { checks.Add(new(name, "Passed", await action())); }
    catch (Exception exception) { checks.Add(new(name, "Failed", SafeError(exception))); }
    Console.WriteLine($"{checks[^1].Status}: {checks[^1].Name}: {checks[^1].Detail}");
}
try
{
    ProbeAssert.Require(provider is "SqlServer" or "PostgreSql", "An exact provider is required.");
    ProbeAssert.Require(Arg("--suite") is "orders" or "orders-extra" or "locks" or "failures" or "shared" or "cursor" or "claim", "An implemented suite must be selected before database access.");
    ProbeAssert.Require(Arg("--case") is null or "immediate" or "all", "Unknown order case rejected before database access.");
    var fixture = new RuntimeFixture(provider == "PostgreSql" ? DatabaseProvider.PostgreSql : DatabaseProvider.SqlServer);
    await Check("Isolation.Wp3Owner", async () => { serverVersion = await fixture.VerifyOwnerAsync(); return OwnedTestDatabase.IsRequested() ? "Dedicated loopback WP6 name, role, provider and owner verified." : "Dedicated loopback WP3 name, provider and owner verified."; });
    if (checks.Any(x => x.Status == "Failed")) throw new ProbeAssertionException("Ownership is required before migration or fixture writes.");
    if (args.Contains("--initialize")) await Check("Installation.Core", fixture.InitializeCoreAsync);
    else await Check("Installation.CoreExactHistory", fixture.VerifyCoreHistoryAsync);
    if (checks.Any(x => x.Status == "Failed")) throw new ProbeAssertionException("Exact installation is required before runtime gates.");
    if (Arg("--suite") == "orders")
    {
    await Check("Orders.ImmediateAllocation", () => OrderNumberGates.ImmediateAsync(fixture));
    if (Arg("--case") != "immediate")
    {
        await Check("Orders.FirstCreationConcurrency", () => OrderNumberGates.ConcurrentFirstCreationAsync(fixture));
        await Check("Orders.CallerRollbackAndVisibility", () => OrderNumberGates.RollbackAsync(fixture));
        await Check("Orders.GlobalCrossTenantAndDatePrefix", () => OrderNumberGates.GlobalScopeAsync(fixture));
        await Check("Orders.PendingManualChangeRejected", () => OrderNumberGates.PendingManualChangeAsync(fixture));
    }
    }
    else if (Arg("--suite") == "orders-extra")
    {
        await Check("Orders.ExistingRowConcurrency", () => OrderNumberGates.ExistingRowConcurrencyAsync(fixture));
        await Check("Orders.PendingModifiedAndDeleted", () => OrderNumberGates.PendingModifiedAndDeletedAsync(fixture));
        await Check("Orders.NonOrderLocalBatching", () => OrderNumberGates.NonOrderBatchingAsync(fixture));
    }
    else if (Arg("--suite") == "cursor")
    {
        await Check("Identity.GenerationCursor", () => IdentityCursorGates.GenerationAsync(fixture));
        if (fixture.IsPostgreSql)
            await Check("Identity.PostgreSqlPageSnapshot", () => IdentityPageSnapshotGate.ConsistentPageAsync(fixture));
    }
    else if (Arg("--suite") == "claim")
    {
        await Check("Installation.SpaceExactHistory", () => WorkSlotClaimGates.InitializeSpaceAsync(fixture));
        if (checks.All(x => x.Status == "Passed"))
        {
            await Check("Claims.WorkSlotFirstAcquire", () => WorkSlotClaimGates.FirstAcquireAsync(fixture));
            await Check("Claims.ConcurrentCapacityOne", () => WorkSlotClaimGates.ConcurrentMaxOneAsync(fixture));
            await Check("Claims.ConcurrentCapacityTwo", () => WorkSlotClaimGates.ConcurrentMaxTwoAsync(fixture));
            await Check("Claims.SameRunSingleton", () => WorkSlotClaimGates.SameRunSingletonAsync(fixture));
            await Check("Claims.LeaseFencesAndExpiry", () => WorkSlotClaimGates.LeaseFencesAndExpiryAsync(fixture));
            await Check("Claims.TenantIndependence", () => WorkSlotClaimGates.TenantIndependenceAsync(fixture));
            await Check("Claims.SkipLockedCandidate", () => WorkSlotClaimGates.SkipLockedAsync(fixture));
        }
    }
    else if (Arg("--suite") == "locks")
    {
        await Check("Locks.TransactionLifecycle", () => ResourceLockGates.TransactionAsync(fixture));
        await Check("Locks.SessionLifecycle", () => ResourceLockGates.SessionAsync(fixture));
        await Check("Clock.AdvancingDatabaseUtc", () => ResourceLockGates.UtcClockAsync(fixture));
    }
    else if (Arg("--suite") == "shared")
    {
        await Check("SharedContext.TransactionOwnership", () => SharedContextGates.TransactionAsync(fixture));
    }
    else
    {
        await Check("Failures.NativeConstraintClassificationAndRollback", () => FailureGates.ConstraintsAsync(fixture));
        await Check("Failures.WholeTransactionRetry", () => TransactionFailureGates.WholeTransactionRetryAsync(fixture));
    }
}
catch (Exception exception) { checks.Add(new("Probe.SetupOrRun", "Failed", SafeError(exception))); }
finally
{
    var binaries = new SortedDictionary<string, object>();
    foreach (var type in new[] { typeof(RuntimeFixture), typeof(CP6Context), typeof(SpaceContext), typeof(CP6.Persistence.PostgreSql.PostgreSqlMigrationsAssembly), typeof(DbContext), typeof(NpgsqlConnection), typeof(SqlConnection) })
        binaries[type.Assembly.GetName().Name!] = new { LoadedPath = type.Assembly.Location, Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(type.Assembly.Location))) };
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { Scope = OwnedTestDatabase.IsRequested() ? "WP6 explicitly named real runtime gates on a runner-owned isolated database; required-case and application/recovery evidence are bound separately." : "WP3 explicitly named real runtime gates on a newly owned isolated database. Complete business/API/worker and recovery acceptance remain separate.", Suite = Arg("--suite"), Case = Arg("--case"), Provider = provider, StartedHostUtc = started, FinishedHostUtc = DateTime.UtcNow, SourceBase = Arg("--source-sha"), SourceState = OwnedTestDatabase.IsRequested() ? "WP6 runner task source; applicable source-file and runtime-binary hashes required" : "WP3 uncommitted task source; per-file input binding required", DatabaseVersion = serverVersion, RuntimeBinaries = binaries, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
}
return checks.Any(x => x.Status == "Failed") ? 1 : 0;

static string SafeError(Exception error) => error switch
{
    ProbeAssertionException assertion => assertion.Message,
    PostgresException pg => $"PostgresException SQLSTATE={pg.SqlState}; constraint={pg.ConstraintName ?? "unspecified"}",
    SqlException sql => $"SqlException Number={sql.Number}",
    _ => error.GetType().Name
};
sealed record RuntimeCheck(string Name, string Status, string Detail);
