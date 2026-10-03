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
        private readonly List<Guid> jobIds = [];
        private Guid modelId;
        private Guid versionId;
        private Guid sourceId;

        public SpaceContext Context() => Space(fixture, TenantId, Clock);

        public static async Task<WorkSlotGraph> CreateAsync(RuntimeFixture fixture)
        {
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
