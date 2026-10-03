using System.Data;
using System.Security.Cryptography;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class WorkSlotClaimGates
{
    private static readonly DateTime FixtureNow = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    public static async Task<string> InitializeSpaceAsync(RuntimeFixture fixture)
    {
        await fixture.VerifyOwnerAsync();
        await using var space = Space(fixture, Guid.NewGuid(), new FixtureClock(FixtureNow));
        var available = space.Database.GetMigrations().ToArray();
        ProbeAssert.Require(available.Length == (fixture.IsPostgreSql ? 1 : 47)
                && available[0] == (fixture.IsPostgreSql
                    ? "20261002161945_PostgreSqlSpaceBaselineV1"
                    : "20260726064940_SpaceE01S01ModelVersionBaseline")
                && available[^1] == (fixture.IsPostgreSql
                    ? "20261002161945_PostgreSqlSpaceBaselineV1"
                    : "20260827053057_SpaceV1UnifiedDraftCreation"),
            "Only the known production Space migration chain may be installed for work-slot gates.");
        var before = (await space.Database.GetAppliedMigrationsAsync()).ToArray();
        ProbeAssert.Require(before.Length <= available.Length
                && before.SequenceEqual(available.Take(before.Length), StringComparer.Ordinal),
            "Actual Space history must be an exact supported prefix before any migration.");
        if (before.Length < available.Length) await space.Database.MigrateAsync();
        var after = (await space.Database.GetAppliedMigrationsAsync()).ToArray();
        ProbeAssert.Require(after.SequenceEqual(available, StringComparer.Ordinal)
                && !(await space.Database.GetPendingMigrationsAsync()).Any(),
            "Work-slot gates require exact actual production Space migration history and zero pending migrations.");
        return $"Actual production Space migration history={after.Length}, pending=0; known-prefix guard enforced. No EnsureCreated or substitute model.";
    }

    public static async Task<string> FirstAcquireAsync(RuntimeFixture fixture)
    {
        ProbeAssert.Require(SpaceAiWorkSlotQueries.TenantLockResource(Guid.Parse("11111111-1111-4111-8111-111111111111"))
                == "cp6:space:ai-work-slots:11111111111141118111111111111111",
            "The shared tenant mutex namespace and exact N-format identity must remain stable.");
        await InitializeSpaceAsync(fixture);
        await using var graph = await WorkSlotGraph.CreateAsync(fixture);
        await using var context = graph.Context();
        var ledger = new EfSpaceAiCapacityLedger(context, graph.Clock);
        var lease = await ledger.TryAcquireWorkSlotAsync(graph.RunIds[0], "wp3-first-owner", 1, TimeSpan.FromSeconds(30));
        ProbeAssert.Require(lease is not null && lease.TenantId == graph.TenantId
                && lease.RunId == graph.RunIds[0] && lease.SlotNo == 1
                && lease.LeaseOwner == "wp3-first-owner" && lease.RowVersion.Length == 8,
            "Actual ledger first acquisition must initialize and claim a real fenced production work slot.");
        await using var observer = graph.Context();
        var slots = await observer.TenantAiWorkSlots.AsNoTracking().ToListAsync();
        ProbeAssert.Require(slots.Count == SpaceTenantAiWorkSlot.PlatformSlotCount
                && slots.Count(slot => slot.RunId is not null) == 1
                && slots.Single(slot => slot.SlotNo == lease!.SlotNo).RowVersion.SequenceEqual(lease!.RowVersion),
            "An independent production Space Context must observe exactly one committed claim and its original opaque token.");
        return "Actual EfSpaceAiCapacityLedger first claim persisted one lease among three real production slots with an eight-byte opaque token; independent Context observed the committed claim. Exact owned tenant graph cleaned. Lease clock is controlled fixture UTC, not database-clock acceptance; budget and full generation/CAD business remain WP5.";
    }

    public static async Task<string> ConcurrentMaxOneAsync(RuntimeFixture fixture)
    {
        await using var graph = await WorkSlotGraph.CreateAsync(fixture, 2);
        var leases = await RaceAcquireAsync(graph, graph, graph.RunIds[0], graph.RunIds[1], 1);
        ProbeAssert.Require(leases.Count(lease => lease is not null) == 1
                && leases.Where(lease => lease is not null).All(lease => lease!.SlotNo == 1 && lease.RowVersion.Length == 8),
            "Two actual sessions racing first creation at max1 must return exactly one real fenced lease.");
        await RequireActiveAsync(graph, 1);
        return "Two distinct physical sessions crossed a shared barrier with no existing tenant slots; actual ledger max1 committed only one lease, observed independently among three initialized slots. Exact owned graph cleaned; controlled fixture clock, no budget acceptance.";
    }

    public static async Task<string> ConcurrentMaxTwoAsync(RuntimeFixture fixture)
    {
        await using var graph = await WorkSlotGraph.CreateAsync(fixture, 2);
        var leases = await RaceAcquireAsync(graph, graph, graph.RunIds[0], graph.RunIds[1], 2);
        ProbeAssert.Require(leases.All(lease => lease is not null && lease.RowVersion.Length == 8)
                && leases.Select(lease => lease!.SlotNo).Order().SequenceEqual(new[] { 1, 2 }),
            "Two actual sessions racing max2 must acquire distinct production slots.");
        await RequireActiveAsync(graph, 2);
        return "Two distinct physical sessions raced actual ledger max2 and committed distinct slot1/slot2 leases; independent Context observed two active leases and three initialized slots. Exact owned graph cleaned; controlled fixture clock.";
    }

    public static async Task<string> SameRunSingletonAsync(RuntimeFixture fixture)
    {
        await using var graph = await WorkSlotGraph.CreateAsync(fixture);
        var leases = await RaceAcquireAsync(graph, graph, graph.RunIds[0], graph.RunIds[0], 2);
        ProbeAssert.Require(leases.Count(lease => lease is not null) == 1,
            "The same run racing with different owners must have exactly one claimant.");
        await using var observer = graph.Context();
        ProbeAssert.Require(await observer.TenantAiWorkSlots.CountAsync(slot => slot.RunId == graph.RunIds[0]) == 1,
            "The actual unique run must occupy only one production slot.");
        await RequireActiveAsync(graph, 1);
        return "Two distinct physical sessions raced one run with different owners; only one actual ledger claimant and one committed tenant/run slot. Exact owned graph cleaned; controlled fixture clock.";
    }

    public static async Task<string> LeaseFencesAndExpiryAsync(RuntimeFixture fixture)
    {
        await using var graph = await WorkSlotGraph.CreateAsync(fixture, 2);
        SpaceAiWorkSlotLease original;
        await using (var first = graph.Context())
        {
            original = await new EfSpaceAiCapacityLedger(first, graph.Clock).TryAcquireWorkSlotAsync(
                graph.RunIds[0], "wp3-original-owner", 1, TimeSpan.FromSeconds(30))
                ?? throw new ProbeAssertionException("The original fenced fixture claim must succeed.");
        }
        graph.Clock.UtcNow = FixtureNow.AddSeconds(5);
        SpaceAiWorkSlotLease renewed;
        await using (var context = graph.Context())
            renewed = await new EfSpaceAiCapacityLedger(context, graph.Clock).RenewWorkSlotAsync(original, TimeSpan.FromSeconds(30));
        ProbeAssert.Require(renewed.RowVersion.Length == 8 && !renewed.RowVersion.SequenceEqual(original.RowVersion)
                && renewed.LeaseExpiresAtUtc == graph.Clock.UtcNow.AddSeconds(30),
            "A valid actual renewal must advance its opaque native token and lease expiry.");
        await RejectLeaseAsync(graph, original, release: false);
        await RejectLeaseAsync(graph, original, release: true);
        var wrongOwner = renewed with { LeaseOwner = "wp3-other-owner" };
        await RejectLeaseAsync(graph, wrongOwner, release: false);
        await RejectLeaseAsync(graph, wrongOwner, release: true);

        graph.Clock.UtcNow = renewed.LeaseExpiresAtUtc;
        await RejectLeaseAsync(graph, renewed, release: false);
        SpaceAiWorkSlotLease replacement;
        await using (var context = graph.Context())
        {
            replacement = await new EfSpaceAiCapacityLedger(context, graph.Clock).TryAcquireWorkSlotAsync(
                graph.RunIds[1], "wp3-replacement-owner", 1, TimeSpan.FromSeconds(30))
                ?? throw new ProbeAssertionException("An expired production slot must be reclaimable.");
        }
        ProbeAssert.Require(replacement.SlotNo == renewed.SlotNo && replacement.RowVersion.Length == 8
                && !replacement.RowVersion.SequenceEqual(renewed.RowVersion),
            "Expiry replacement must own the same real slot with a different opaque native token.");
        await RejectLeaseAsync(graph, renewed, release: false);
        await RejectLeaseAsync(graph, renewed, release: true);
        var wrongRun = replacement with { RunId = renewed.RunId };
        await RejectLeaseAsync(graph, wrongRun, release: false);
        await RejectLeaseAsync(graph, wrongRun, release: true);
        await using (var observer = graph.Context())
        {
            var stored = await observer.TenantAiWorkSlots.AsNoTracking().SingleAsync(slot => slot.SlotNo == replacement.SlotNo);
            ProbeAssert.Require(stored.RunId == replacement.RunId && stored.LeaseOwner == replacement.LeaseOwner
                    && stored.RowVersion.SequenceEqual(replacement.RowVersion),
                "All rejected old-token, owner and run operations must leave the replacement lease unchanged.");
        }
        await using (var context = graph.Context())
            await new EfSpaceAiCapacityLedger(context, graph.Clock).ReleaseWorkSlotAsync(replacement);
        await RequireActiveAsync(graph, 0);
        return "Actual ledger renewal changed the opaque eight-byte token; stale-token and wrong-owner renew/release were rejected. Exact expiry rejected renewal, allowed a different run to reclaim the slot, and rejected the old lease plus wrong-run operations without altering the replacement. Valid replacement release succeeded. Controlled UTC fixture clock; real advancing database UTC is a separate gate. Exact owned graph cleaned.";
    }

    public static async Task<string> TenantIndependenceAsync(RuntimeFixture fixture)
    {
        await using var first = await WorkSlotGraph.CreateAsync(fixture);
        await using var second = await WorkSlotGraph.CreateAsync(fixture);
        var leases = await RaceAcquireAsync(first, second, first.RunIds[0], second.RunIds[0], 1);
        ProbeAssert.Require(first.TenantId != second.TenantId && leases[0]?.TenantId == first.TenantId
                && leases[1]?.TenantId == second.TenantId && leases.All(lease => lease?.SlotNo == 1),
            "Two tenants must independently claim their own max1 slot through actual separate sessions.");
        await RequireActiveAsync(first, 1);
        await RequireActiveAsync(second, 1);
        return "Two tenants on distinct physical sessions crossed a shared barrier and independently committed their own max1 slot1 lease; separate tenant Contexts observed one active lease each. Both exact owned graphs cleaned; controlled fixture clock.";
    }

    public static async Task<string> SkipLockedAsync(RuntimeFixture fixture)
    {
        await using var graph = await WorkSlotGraph.CreateAsync(fixture);
        await using (var initialize = graph.Context())
        {
            var ledger = new EfSpaceAiCapacityLedger(initialize, graph.Clock);
            var seedLease = await ledger.TryAcquireWorkSlotAsync(graph.RunIds[0], "wp3-skip-initialize", 2, TimeSpan.FromSeconds(30))
                ?? throw new ProbeAssertionException("The real ledger must initialize skip-lock fixture slots.");
            await ledger.ReleaseWorkSlotAsync(seedLease);
        }
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var holder = graph.Context();
        await using var peer = graph.Context();
        await holder.Database.OpenConnectionAsync(budget.Token);
        await peer.Database.OpenConnectionAsync(budget.Token);
        await RequireDistinctSessionsAsync(graph, holder, peer, budget.Token);
        await using var holderTransaction = await holder.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, budget.Token);
        await using var peerTransaction = await peer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, budget.Token);
        var held = await SpaceAiWorkSlotQueries.FindAvailableAsync(holder, graph.TenantId, 2, graph.Clock.UtcNow, budget.Token);
        ProbeAssert.Require(held?.SlotNo == 1, "The actual production candidate query must lock available slot1.");
        var originalTimeout = peer.Database.GetCommandTimeout();
        using var finitePeer = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
        finitePeer.CancelAfter(TimeSpan.FromSeconds(2));
        var skipped = await SpaceAiWorkSlotQueries.FindAvailableAsync(peer, graph.TenantId, 2, graph.Clock.UtcNow, finitePeer.Token);
        ProbeAssert.Require(skipped?.SlotNo == 2 && peer.Database.GetCommandTimeout() == originalTimeout,
            "The same production query must skip the held slot1 within its finite budget and restore caller timeout.");
        await peerTransaction.RollbackAsync(CancellationToken.None);
        await holderTransaction.RollbackAsync(CancellationToken.None);
        return "The actual production candidate query held available slot1 in one real ReadCommitted session; a distinct session used the same helper and returned slot2 under a two-second cancellation budget, then both transactions rolled back. SQL READPAST/PG SKIP LOCKED capability verified directly, not a claim that all background workers already use it. Caller timeout restored; exact owned graph cleaned.";
    }

    private static async Task<SpaceAiWorkSlotLease?[]> RaceAcquireAsync(
        WorkSlotGraph firstGraph, WorkSlotGraph secondGraph, Guid firstRun, Guid secondRun, int maximum)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var first = firstGraph.Context();
        await using var second = secondGraph.Context();
        ProbeAssert.Require(!await first.TenantAiWorkSlots.AnyAsync(budget.Token)
                && !await second.TenantAiWorkSlots.AnyAsync(budget.Token),
            "Concurrent claim fixtures must start before any tenant work-slot creation.");
        await first.Database.OpenConnectionAsync(budget.Token);
        await second.Database.OpenConnectionAsync(budget.Token);
        await RequireDistinctSessionsAsync(firstGraph, first, second, budget.Token);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        async Task<SpaceAiWorkSlotLease?> Acquire(SpaceContext context, WorkSlotGraph graph, Guid run, string owner)
        {
            if (Interlocked.Increment(ref ready) == 2) bothReady.SetResult();
            await start.Task.WaitAsync(budget.Token);
            return await new EfSpaceAiCapacityLedger(context, graph.Clock).TryAcquireWorkSlotAsync(
                run, owner, maximum, TimeSpan.FromSeconds(30), budget.Token);
        }
        var firstClaim = Acquire(first, firstGraph, firstRun, "wp3-race-owner-a");
        var secondClaim = Acquire(second, secondGraph, secondRun, "wp3-race-owner-b");
        await bothReady.Task.WaitAsync(budget.Token);
        start.SetResult();
        return await Task.WhenAll(firstClaim, secondClaim);
    }

    private static async Task RequireDistinctSessionsAsync(WorkSlotGraph graph, SpaceContext first, SpaceContext second, CancellationToken token)
    {
        var query = graph.IsPostgreSql ? "SELECT pg_catalog.pg_backend_pid() AS \"Value\"" : "SELECT CONVERT(int,@@SPID) AS [Value]";
        var firstId = await first.Database.SqlQueryRaw<int>(query).SingleAsync(token);
        var secondId = await second.Database.SqlQueryRaw<int>(query).SingleAsync(token);
        ProbeAssert.Require(firstId != secondId, "Claim concurrency and skip-lock gates require distinct physical native sessions.");
    }

    private static async Task RequireActiveAsync(WorkSlotGraph graph, int expected)
    {
        await using var observer = graph.Context();
        var slots = await observer.TenantAiWorkSlots.AsNoTracking().ToListAsync();
        ProbeAssert.Require(slots.Count == SpaceTenantAiWorkSlot.PlatformSlotCount
                && slots.Count(slot => slot.RunId is not null && slot.LeaseExpiresAtUtc > graph.Clock.UtcNow) == expected,
            "An independent Context must observe exact initialized slot count and committed active capacity.");
    }

    private static async Task RejectLeaseAsync(WorkSlotGraph graph, SpaceAiWorkSlotLease lease, bool release)
    {
        await using var context = graph.Context();
        var ledger = new EfSpaceAiCapacityLedger(context, graph.Clock);
        try
        {
            if (release) await ledger.ReleaseWorkSlotAsync(lease);
            else await ledger.RenewWorkSlotAsync(lease, TimeSpan.FromSeconds(30));
        }
        catch (SpaceAiCapacityLeaseLostException)
        {
            return;
        }
        throw new ProbeAssertionException("The actual ledger must reject a lost lease before it changes committed slot state.");
    }

    private static SpaceContext Space(RuntimeFixture fixture, Guid tenantId, FixtureClock clock)
    {
        var profile = DatabaseMigrationProfile.For(fixture.Database, DatabaseContextKind.Space);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<SpaceContext>(),
            fixture.Database, fixture.ConnectionString, profile.MigrationsAssembly,
            profile.HistoryTable, profile.HistorySchema).Options;
        return new SpaceContext(options, new FixtureExecution(tenantId), clock);
    }

    private sealed class FixtureExecution(Guid tenantId) : ISpaceExecutionContext
    {
        public Guid TenantId { get; } = tenantId;
        public Guid ActorId { get; } = Guid.NewGuid();
    }

    private sealed class FixtureClock(DateTime now) : ISpaceClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private sealed class WorkSlotGraph(RuntimeFixture fixture) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public FixtureClock Clock { get; } = new(FixtureNow);
        public List<Guid> RunIds { get; } = [];
        public bool IsPostgreSql => fixture.IsPostgreSql;
        private readonly List<Guid> jobIds = [];
        private Guid modelId;
        private Guid versionId;
        private Guid sourceId;

        public SpaceContext Context() => Space(fixture, TenantId, Clock);

        public static async Task<WorkSlotGraph> CreateAsync(RuntimeFixture fixture, int runCount = 1)
        {
            await fixture.VerifyOwnerAsync();
            var graph = new WorkSlotGraph(fixture);
            try
            {
                await using var context = graph.Context();
                var siteId = Guid.NewGuid();
                var model = SpaceModel.Create(graph.TenantId, siteId);
                graph.modelId = model.Id;
                var version = SpaceModelVersion.CreateDraft(graph.TenantId, model.Id, 1, "WP3 work-slot fixture");
                graph.versionId = version.Id;
                var sourceHash = Hash();
                var source = SpaceModelSource.CreateInlineSource(graph.TenantId, version.Id,
                    SpaceSourceType.Editor, "WP3 owned work-slot source", sourceHash);
                graph.sourceId = source.Id;
                context.AddRange(model, version, source);
                for (var runIndex = 0; runIndex < runCount; runIndex++)
                {
                    var runId = Guid.NewGuid();
                    var job = SpaceJob.CreateQueued(graph.TenantId, SpaceJobType.ApplyGeneration,
                        SpaceJobSubjectType.GenerationRun, runId, Hash(), Hash(), 0, 1,
                        Guid.NewGuid(), graph.Clock.UtcNow, Guid.NewGuid());
                    graph.jobIds.Add(job.Id);
                    graph.RunIds.Add(runId);
                    var run = SpaceGenerationRun.Create(new SpaceGenerationRunDefinition(
                        graph.TenantId, siteId, version.Id, source.Id, sourceHash, 0, Hash(), Hash(),
                        null, null, null, "wp3-work-slot-v1", SpaceAiPolicySnapshot.Disabled, null,
                        "wp3-work-slot-v1", job.Id, RunId: runId));
                    context.AddRange(job, run);
                }
                await context.SaveChangesAsync();
                return graph;
            }
            catch
            {
                await graph.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await using var cleanup = Context();
            // ExecuteDelete bypasses immutable-row application guards only for this exact owned fixture.
            await cleanup.TenantAiWorkSlots.Where(slot => slot.TenantId == TenantId).ExecuteDeleteAsync();
            await cleanup.GenerationRuns.Where(run => run.TenantId == TenantId && RunIds.Contains(run.Id)).ExecuteDeleteAsync();
            await cleanup.Jobs.Where(job => job.TenantId == TenantId && jobIds.Contains(job.Id)).ExecuteDeleteAsync();
            await cleanup.Sources.Where(source => source.TenantId == TenantId && source.Id == sourceId).ExecuteDeleteAsync();
            await cleanup.Versions.Where(version => version.TenantId == TenantId && version.Id == versionId).ExecuteDeleteAsync();
            await cleanup.Models.Where(model => model.TenantId == TenantId && model.Id == modelId).ExecuteDeleteAsync();
            ProbeAssert.Require(!await cleanup.TenantAiWorkSlots.AnyAsync()
                    && !await cleanup.GenerationRuns.AnyAsync(run => RunIds.Contains(run.Id))
                    && !await cleanup.Jobs.AnyAsync(job => jobIds.Contains(job.Id))
                    && !await cleanup.Sources.AnyAsync(source => source.Id == sourceId)
                    && !await cleanup.Versions.AnyAsync(version => version.Id == versionId)
                    && !await cleanup.Models.AnyAsync(model => model.Id == modelId),
                "Every exact owned work-slot fixture row must be absent after native cleanup.");
        }

        private static string Hash() => Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant();
    }
}
