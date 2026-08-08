using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceAiAtomicApplySqlServerTests
{
    private static readonly DateTime Start =
        new(2026, 8, 6, 15, 0, 0, DateTimeKind.Utc);
    private static readonly string SourceHash = new('a', 64);

    [SqlServerFact]
    public async Task Apply_is_idempotent_and_commits_one_atomic_revision()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            var queued = await QueueAsync(context, execution, clock, graph);
            var replay = await new SpaceAiAtomicApplyService(
                context,
                execution,
                new AllowAccess(),
                clock).QueueAsync(
                    graph.RunId,
                    queued.Request,
                    "apply-zone-1");

            Assert.False(queued.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            Assert.Equal(queued.JobId, replay.JobId);

            var runner = Runner(
                context,
                execution,
                clock,
                new NoOpSpaceAiApplyFaultInjector());
            Assert.True(await runner.RunNextAsync(
                SpaceJobType.ApplyGeneration,
                "apply-worker"));
            Assert.False(await runner.RunNextAsync(
                SpaceJobType.ApplyGeneration,
                "apply-worker"));

            context.ChangeTracker.Clear();
            var run = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            var version = await context.Versions.SingleAsync(
                item => item.Id == graph.VersionId);
            var floor = await context.FloorRevisions.SingleAsync(
                item => item.ModelVersionId == graph.VersionId);
            var proposal = await context.GenerationProposals.SingleAsync(
                item => item.Id == graph.ProposalId);
            var job = await context.Jobs.SingleAsync(
                item => item.Id == queued.JobId);

            Assert.Equal(SpaceGenerationRunStatus.Succeeded, run.Status);
            Assert.Equal(SpaceJobStatus.Succeeded, job.Status);
            Assert.Equal(1, version.ContentRevision);
            Assert.Equal(1, floor.Revision);
            Assert.Equal(1, run.AppliedContentRevision);
            Assert.NotNull(run.ApplyPlanHash);
            Assert.Equal(
                SpaceGenerationProposalStatus.Applied,
                proposal.Status);
            Assert.NotNull(proposal.AppliedLogicalId);
            var zone = Assert.Single(await context.ZoneRevisions.ToListAsync());
            Assert.Equal("AI-ZONE-1", zone.ZoneCode);
            Assert.Equal("AI Storage Zone", zone.Name);
            Assert.Equal(proposal.AppliedLogicalId, zone.LogicalId);
            Assert.Single(await context.ElementCommandBatches.ToListAsync());
            Assert.Single(await context.ElementCommandRecords.ToListAsync());
            Assert.Single(await context.GenerationStagingElements.ToListAsync());
            Assert.Null(
                (await context.Models.SingleAsync()).CurrentPublishedVersionId);
        });
    }

    [SqlServerFact]
    public async Task Draft_change_marks_run_stale_without_apply_writes()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            var queued = await QueueAsync(context, execution, clock, graph);
            context.ChangeTracker.Clear();
            var edited = await context.Versions.SingleAsync(
                item => item.Id == graph.VersionId);
            edited.TouchContent();
            await context.SaveChangesAsync();

            var runner = Runner(
                context,
                execution,
                clock,
                new NoOpSpaceAiApplyFaultInjector());
            Assert.True(await runner.RunNextAsync(
                SpaceJobType.ApplyGeneration,
                "stale-worker"));

            context.ChangeTracker.Clear();
            var run = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            var job = await context.Jobs.SingleAsync(
                item => item.Id == queued.JobId);
            Assert.Equal(SpaceGenerationRunStatus.Stale, run.Status);
            Assert.Equal(SpaceJobStatus.Failed, job.Status);
            Assert.Equal(SpaceErrorCodes.AiRunStale, job.LastErrorCode);
            Assert.Equal(
                1,
                (await context.Versions.SingleAsync()).ContentRevision);
            Assert.Equal(0, (await context.FloorRevisions.SingleAsync()).Revision);
            Assert.Empty(await context.ZoneRevisions.ToListAsync());
            Assert.Empty(await context.ElementCommandBatches.ToListAsync());
            Assert.Empty(await context.GenerationStagingElements.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Commit_fault_rolls_back_then_reuses_plan_on_retry()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            var queued = await QueueAsync(context, execution, clock, graph);
            var fault = new OneShotFaultInjector("commit-before-commit");
            var runner = Runner(context, execution, clock, fault);

            Assert.True(await runner.RunNextAsync(
                SpaceJobType.ApplyGeneration,
                "retry-worker-1"));
            context.ChangeTracker.Clear();
            var firstJob = await context.Jobs.SingleAsync(
                item => item.Id == queued.JobId);
            Assert.Equal(SpaceJobStatus.Queued, firstJob.Status);
            Assert.Equal(
                SpaceGenerationRunStatus.Applying,
                (await context.GenerationRuns.SingleAsync()).Status);
            Assert.Equal(0, (await context.Versions.SingleAsync()).ContentRevision);
            Assert.Equal(0, (await context.FloorRevisions.SingleAsync()).Revision);
            Assert.Empty(await context.ZoneRevisions.ToListAsync());
            Assert.Empty(await context.ElementCommandBatches.ToListAsync());
            Assert.Equal(
                SpaceGenerationProposalStatus.Accepted,
                (await context.GenerationProposals.SingleAsync()).Status);
            Assert.Equal(
                SpaceGenerationStagingValidationStatus.Validated,
                (await context.GenerationStagingElements.SingleAsync())
                    .ValidationStatus);

            clock.UtcNow = clock.UtcNow.AddSeconds(5);
            Assert.True(await runner.RunNextAsync(
                SpaceJobType.ApplyGeneration,
                "retry-worker-2"));

            context.ChangeTracker.Clear();
            Assert.Equal(
                SpaceJobStatus.Succeeded,
                (await context.Jobs.SingleAsync(
                    item => item.Id == queued.JobId)).Status);
            Assert.Equal(
                SpaceGenerationRunStatus.Succeeded,
                (await context.GenerationRuns.SingleAsync()).Status);
            Assert.Equal(1, (await context.Versions.SingleAsync()).ContentRevision);
            Assert.Equal(1, (await context.FloorRevisions.SingleAsync()).Revision);
            Assert.Single(await context.ZoneRevisions.ToListAsync());
            Assert.Single(await context.ElementCommandBatches.ToListAsync());
            Assert.Equal(2, await context.JobAttempts.CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Concurrent_same_key_creates_one_apply_job_and_replays()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            context.ChangeTracker.Clear();
            var review = await new SpaceAiProposalDecisionService(
                context,
                execution,
                new AllowAccess(),
                new TestCursorCodec(),
                clock,
                new SpaceAiProposalReviewOptions()).GetReviewAsync(
                    graph.RunId);
            var request = new CreateSpaceAiAtomicApplyRequest(
                review.BaseContentRevision,
                review.RunRowVersion,
                review.ReviewEtag);
            var connectionString = context.Database.GetConnectionString()!;

            await using var firstContext = CreateContext(
                connectionString,
                execution,
                clock);
            await using var secondContext = CreateContext(
                connectionString,
                execution,
                clock);
            var gate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<SpaceAiAtomicApplyAcceptedDto> QueueAsync(
                SpaceContext serviceContext)
            {
                await gate.Task;
                return await new SpaceAiAtomicApplyService(
                    serviceContext,
                    execution,
                    new AllowAccess(),
                    clock).QueueAsync(
                        graph.RunId,
                        request,
                        "concurrent-apply-zone-1");
            }

            var first = QueueAsync(firstContext);
            var second = QueueAsync(secondContext);
            gate.SetResult();
            var responses = await Task.WhenAll(first, second);

            Assert.Single(responses, response => !response.IdempotentReplay);
            Assert.Single(responses, response => response.IdempotentReplay);
            Assert.Equal(responses[0].JobId, responses[1].JobId);
            context.ChangeTracker.Clear();
            Assert.Single(await context.Jobs.Where(item =>
                item.JobType == SpaceJobType.ApplyGeneration).ToListAsync());
            Assert.Single(await context.IdempotencyRecords.Where(item =>
                item.Operation == "space.ai-proposal.apply").ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Queued_apply_cancels_idempotently_without_draft_writes()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            var queued = await QueueAsync(context, execution, clock, graph);
            context.ChangeTracker.Clear();
            var run = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            var request = new SpaceAiRunActionRequest(
                Convert.ToBase64String(run.RowVersion));
            var service = RecoveryService(context, execution, clock);

            var cancelled = await service.CancelAsync(
                run.Id,
                request,
                "cancel-queued-apply");
            var replay = await service.CancelAsync(
                run.Id,
                request,
                "cancel-queued-apply");

            Assert.Equal("Cancelled", cancelled.Status);
            Assert.False(cancelled.IdempotentReplay);
            Assert.True(replay.IdempotentReplay);
            context.ChangeTracker.Clear();
            Assert.Equal(
                SpaceGenerationRunStatus.Cancelled,
                (await context.GenerationRuns.SingleAsync()).Status);
            Assert.Equal(
                SpaceJobStatus.Cancelled,
                (await context.Jobs.SingleAsync(
                    item => item.Id == queued.JobId)).Status);
            Assert.Equal(
                SpaceGenerationProposalStatus.Obsolete,
                (await context.GenerationProposals.SingleAsync()).Status);
            Assert.Equal(0, (await context.Versions.SingleAsync()).ContentRevision);
            Assert.Empty(await context.ElementCommandBatches.ToArrayAsync());
            Assert.Null((await context.Models.SingleAsync())
                .CurrentPublishedVersionId);
        });
    }

    [SqlServerFact]
    public async Task Concurrent_cancel_same_key_replays_after_row_version_changes()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            _ = await QueueAsync(context, execution, clock, graph);
            context.ChangeTracker.Clear();
            var run = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            var request = new SpaceAiRunActionRequest(
                Convert.ToBase64String(run.RowVersion));
            var connectionString = context.Database.GetConnectionString()!;
            await using var firstContext = CreateContext(
                connectionString,
                execution,
                clock);
            await using var secondContext = CreateContext(
                connectionString,
                execution,
                clock);
            var gate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<SpaceAiGenerationRunActionDto> CancelAsync(
                SpaceContext serviceContext)
            {
                await gate.Task;
                return await RecoveryService(
                    serviceContext,
                    execution,
                    clock).CancelAsync(
                        graph.RunId,
                        request,
                        "concurrent-cancel-apply");
            }

            var first = CancelAsync(firstContext);
            var second = CancelAsync(secondContext);
            gate.SetResult();
            var responses = await Task.WhenAll(first, second);

            Assert.Single(responses, response => !response.IdempotentReplay);
            Assert.Single(responses, response => response.IdempotentReplay);
            Assert.All(responses, response =>
                Assert.Equal("Cancelled", response.Status));
            context.ChangeTracker.Clear();
            Assert.Single(await context.IdempotencyRecords.Where(item =>
                item.Operation ==
                    "space.ai-generation-run.cancel").ToArrayAsync());
            Assert.Equal(
                SpaceGenerationRunStatus.Cancelled,
                (await context.GenerationRuns.SingleAsync()).Status);
        });
    }

    [SqlServerFact]
    public async Task Running_apply_acknowledges_cancel_at_worker_safe_point()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            await QueueAsync(context, execution, clock, graph);
            var store = new EfSpaceJobLeaseStore(context, clock);
            var lease = Assert.IsType<SpaceJobLease>(
                await store.TryClaimNextAsync(
                    "cancel-worker",
                    SpaceGenerationApplyJobProcessor.Version,
                    TimeSpan.FromMinutes(1),
                    [SpaceJobType.ApplyGeneration]));
            context.ChangeTracker.Clear();
            var run = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);

            var response = await RecoveryService(context, execution, clock)
                .CancelAsync(
                    run.Id,
                    new SpaceAiRunActionRequest(
                        Convert.ToBase64String(run.RowVersion)),
                    "cancel-running-apply");

            Assert.True(response.CancellationPending);
            await Runner(
                    context,
                    execution,
                    clock,
                    new NoOpSpaceAiApplyFaultInjector())
                .RunClaimedAsync(lease);
            context.ChangeTracker.Clear();
            Assert.Equal(
                SpaceGenerationRunStatus.Cancelled,
                (await context.GenerationRuns.SingleAsync()).Status);
            Assert.Equal(
                SpaceJobStatus.Cancelled,
                (await context.Jobs.SingleAsync(
                    item => item.Id == lease.JobId)).Status);
            Assert.Empty(await context.GenerationStagingElements.ToArrayAsync());
            Assert.Empty(await context.ElementCommandBatches.ToArrayAsync());
        });
    }

    [SqlServerFact]
    public async Task Resource_failure_retries_same_apply_job_and_frozen_run()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            var queued = await QueueAsync(context, execution, clock, graph);
            var store = new EfSpaceJobLeaseStore(context, clock);
            var lease = Assert.IsType<SpaceJobLease>(
                await store.TryClaimNextAsync(
                    "failure-worker",
                    SpaceGenerationApplyJobProcessor.Version,
                    TimeSpan.FromMinutes(1),
                    [SpaceJobType.ApplyGeneration]));
            await store.FailJobAsync(
                lease,
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.AiProviderUnavailable,
                "The configured Apply resource is temporarily unavailable.");
            context.ChangeTracker.Clear();
            var failed = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            Assert.Equal(SpaceGenerationRunStatus.Failed, failed.Status);

            var response = await RecoveryService(context, execution, clock)
                .RetryAsync(
                    failed.Id,
                    new SpaceAiRunActionRequest(
                        Convert.ToBase64String(failed.RowVersion)),
                    "retry-resource-apply");

            Assert.Equal(queued.JobId, response.JobId);
            context.ChangeTracker.Clear();
            var retriedRun = await context.GenerationRuns.SingleAsync();
            var retriedJob = await context.Jobs.SingleAsync(
                item => item.Id == queued.JobId);
            Assert.Equal(SpaceGenerationRunStatus.Applying, retriedRun.Status);
            Assert.Equal(SpaceJobStatus.Queued, retriedJob.Status);
            Assert.Equal("ManualRetryScheduled", retriedJob.ProgressStage);
            Assert.Equal(1, retriedJob.AttemptCount);
        });
    }

    [SqlServerFact]
    public async Task Failed_recovery_retires_current_run_before_replacement()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            _ = await QueueAsync(context, execution, clock, graph);
            var store = new EfSpaceJobLeaseStore(context, clock);
            var lease = Assert.IsType<SpaceJobLease>(
                await store.TryClaimNextAsync(
                    "failed-recovery-worker",
                    SpaceGenerationApplyJobProcessor.Version,
                    TimeSpan.FromMinutes(1),
                    [SpaceJobType.ApplyGeneration]));
            await store.FailJobAsync(
                lease,
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.AiProviderUnavailable,
                "The configured Apply resource is temporarily unavailable.");
            context.ChangeTracker.Clear();
            var failed = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            Assert.True(failed.IsCurrent);

            var response = await RecoveryService(context, execution, clock)
                .RecoverAsync(
                    graph.VersionId,
                    new CreateSpaceAiGenerationRecoveryRequest(
                        failed.Id,
                        0,
                        Convert.ToBase64String(failed.RowVersion),
                        SpaceAiRunRecoveryContract.SamePolicyMode),
                    "recover-failed-same-policy");

            context.ChangeTracker.Clear();
            var source = await context.GenerationRuns.SingleAsync(
                item => item.Id == failed.Id);
            var replacement = await context.GenerationRuns.SingleAsync(
                item => item.Id == response.ReplacementRunId);
            Assert.Equal(SpaceGenerationRunStatus.Cancelled, source.Status);
            Assert.False(source.IsCurrent);
            Assert.Equal(SpaceGenerationRunStatus.Queued, replacement.Status);
            Assert.True(replacement.IsCurrent);
            Assert.Equal(source.PolicySnapshot, replacement.PolicySnapshot);
            Assert.Equal(
                source.ProviderConfigVersionId,
                replacement.ProviderConfigVersionId);
            Assert.Equal(
                SpaceGenerationProposalStatus.Obsolete,
                (await context.GenerationProposals.SingleAsync()).Status);
        });
    }

    [SqlServerFact]
    public async Task Reconcile_repairs_run_from_authoritative_committed_batch()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            _ = await QueueAsync(context, execution, clock, graph);
            Assert.True(await Runner(
                context,
                execution,
                clock,
                new NoOpSpaceAiApplyFaultInjector()).RunNextAsync(
                    SpaceJobType.ApplyGeneration,
                    "reconcile-worker"));
            context.ChangeTracker.Clear();
            var committed = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            Assert.Equal(SpaceGenerationRunStatus.Succeeded, committed.Status);
            Assert.Equal(1, committed.AppliedContentRevision);

            const string failureCode =
                SpaceErrorCodes.AiApplyResultUnknown;
            const string failureSummary =
                "Simulated post-commit status uncertainty.";
            var failedStatus = (short)SpaceGenerationRunStatus.Failed;
            _ = await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Space_GenerationRun] SET [Status] = {failedStatus}, [FailureCode] = {failureCode}, [FailureSummary] = {failureSummary} WHERE [Id] = {graph.RunId}");
            context.ChangeTracker.Clear();
            var uncertain = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);

            var response = await RecoveryService(context, execution, clock)
                .ReconcileAsync(
                    uncertain.Id,
                    new SpaceAiRunActionRequest(
                        Convert.ToBase64String(uncertain.RowVersion)),
                    "reconcile-committed-apply");

            Assert.Equal("Succeeded", response.Status);
            context.ChangeTracker.Clear();
            var reconciled = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            Assert.Equal(SpaceGenerationRunStatus.Succeeded, reconciled.Status);
            Assert.Equal(1, reconciled.AppliedContentRevision);
            Assert.Null(reconciled.FailureCode);
            Assert.Single(await context.ElementCommandBatches.ToArrayAsync());
            Assert.Equal(
                1,
                (await context.Versions.SingleAsync(
                    item => item.Id == graph.VersionId)).ContentRevision);
        });
    }

    [SqlServerFact]
    public async Task Stale_recovery_creates_new_run_and_obsoletes_old_proposals()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            var stale = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            stale.MarkStale();
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            stale = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);

            var response = await RecoveryService(context, execution, clock)
                .RecoverAsync(
                    graph.VersionId,
                    new CreateSpaceAiGenerationRecoveryRequest(
                        stale.Id,
                        0,
                        Convert.ToBase64String(stale.RowVersion),
                        SpaceAiRunRecoveryContract.RuleOnlyMode),
                    "recover-stale-rule-only");

            Assert.NotNull(response.ReplacementRunId);
            context.ChangeTracker.Clear();
            var replacement = await context.GenerationRuns.SingleAsync(
                item => item.Id == response.ReplacementRunId);
            Assert.Equal(stale.Id, replacement.BasedOnRunId);
            Assert.Equal(SpaceGenerationRunStatus.Queued, replacement.Status);
            Assert.Equal(SpaceAiPolicySnapshot.Disabled, replacement.PolicySnapshot);
            Assert.Null(replacement.ProviderConfigVersionId);
            Assert.Equal(
                SpaceGenerationProposalStatus.Obsolete,
                (await context.GenerationProposals.SingleAsync()).Status);
            Assert.Equal(
                SpaceJobType.BuildScene,
                (await context.Jobs.SingleAsync(
                    item => item.Id == replacement.JobId)).JobType);
            Assert.Empty(await context.ElementCommandBatches.ToArrayAsync());
        });
    }

    [SqlServerFact]
    public async Task Apply_materializes_hierarchy_rack_derivation_and_element()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            await AddAcceptedProposalAsync(
                context,
                execution,
                graph,
                "aisle-ai-1",
                "Aisle",
                """
                {"kind":"Path","points":[{"x":500,"y":3500,"z":0},{"x":9500,"y":4500,"z":0}]}
                """,
                """
                {"name":"AI Aisle","aisleCode":"AI-AISLE-1","direction":"TwoWay","widthMillimeters":1000,"heightMillimeters":3000}
                """,
                """{"zoneSourceKey":"zone-ai-1"}""");
            var profileVersionId = Guid.NewGuid();
            await AddAcceptedProposalAsync(
                context,
                execution,
                graph,
                "rack-ai-1",
                "Rack",
                """
                {"kind":"Polygon","points":[{"x":1000,"y":5000,"z":0},{"x":3000,"y":5000,"z":0},{"x":3000,"y":6000,"z":0},{"x":1000,"y":6000,"z":0}]}
                """,
                $$$"""
                {"name":"AI Rack","rackCode":"AI-RACK-1","rackType":"Selective","rackDerivation":{"profileVersionId":"{{{profileVersionId:D}}}","rackWidthMillimeters":2000,"rackDepthMillimeters":1000,"rackHeightMillimeters":3000,"levels":[{"levelNo":1,"bottomZMillimeters":0,"clearHeightMillimeters":1200,"binCount":2,"depthCount":1,"cellWidthMillimeters":1000,"cellDepthMillimeters":1000,"beamHeightMillimeters":100,"maxLoadKilograms":500},{"levelNo":2,"bottomZMillimeters":1400,"clearHeightMillimeters":1200,"binCount":2,"depthCount":1,"cellWidthMillimeters":1000,"cellDepthMillimeters":1000,"beamHeightMillimeters":100,"maxLoadKilograms":400}]}}
                """,
                """{"zoneSourceKey":"zone-ai-1","aisleSourceKey":"aisle-ai-1"}""");
            await AddAcceptedProposalAsync(
                context,
                execution,
                graph,
                "wall-ai-1",
                "Wall",
                """
                {"kind":"Path","points":[{"x":500,"y":500,"z":0},{"x":9500,"y":600,"z":0}]}
                """,
                """
                {"name":"AI Safety Wall","thicknessMillimeters":100,"heightMillimeters":3000,"wallType":"Safety"}
                """,
                "{}");

            var queued = await QueueAsync(
                context,
                execution,
                clock,
                graph,
                "apply-hierarchy-1");
            var runner = Runner(
                context,
                execution,
                clock,
                new NoOpSpaceAiApplyFaultInjector());
            Assert.True(await runner.RunNextAsync(
                SpaceJobType.ApplyGeneration,
                "hierarchy-worker"));

            context.ChangeTracker.Clear();
            var run = await context.GenerationRuns.SingleAsync(
                item => item.Id == graph.RunId);
            Assert.Equal(SpaceGenerationRunStatus.Succeeded, run.Status);
            Assert.Equal(queued.JobId, run.ApplyJobId);
            Assert.Single(await context.ZoneRevisions.ToListAsync());
            Assert.Single(await context.AisleRevisions.ToListAsync());
            var rack = Assert.Single(
                await context.RackRevisions.ToListAsync());
            Assert.Equal("Selective", rack.RackType);
            Assert.Equal(2, await context.RackLevelRevisions.CountAsync());
            Assert.Equal(4, await context.LocationRevisions.CountAsync());
            var element = Assert.Single(
                await context.ElementRevisions.ToListAsync());
            Assert.Equal("Wall", element.ElementType);
            Assert.Equal(4, await context.ElementCommandRecords.CountAsync());
            Assert.Equal(4, await context.GenerationStagingElements.CountAsync());

            using var counts = JsonDocument.Parse(run.AppliedCountsJson!);
            Assert.Equal(
                1,
                counts.RootElement.GetProperty("zones").GetInt64());
            Assert.Equal(
                1,
                counts.RootElement.GetProperty("aisles").GetInt64());
            Assert.Equal(
                1,
                counts.RootElement.GetProperty("racks").GetInt64());
            Assert.Equal(
                2,
                counts.RootElement.GetProperty("rackLevels").GetInt64());
            Assert.Equal(
                4,
                counts.RootElement.GetProperty("locations").GetInt64());
            Assert.Equal(
                1,
                counts.RootElement.GetProperty("elements").GetInt64());
        });
    }

    [SqlServerFact]
    public async Task Apply_updates_existing_baseline_and_reconciles_rack_derivation()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var graph = await SeedReviewedZoneAsync(context, execution);
            var floor = await context.FloorRevisions.SingleAsync();
            var zoneLogicalId = WarehouseDeterministicIdentity.CreateObjectLogicalId(
                graph.VersionId,
                SourceHash,
                "zone-ai-1");
            var rackLogicalId = WarehouseDeterministicIdentity.CreateObjectLogicalId(
                graph.VersionId,
                SourceHash,
                "rack-ai-1");
            var aisleLogicalId = WarehouseDeterministicIdentity.CreateObjectLogicalId(
                graph.VersionId,
                SourceHash,
                "aisle-ai-1");
            var wallLogicalId = WarehouseDeterministicIdentity.CreateObjectLogicalId(
                graph.VersionId,
                SourceHash,
                "wall-ai-1");
            var zone = SpaceZoneRevision.Create(
                execution.TenantId,
                graph.VersionId,
                zoneLogicalId,
                floor.LogicalId,
                "OLD-ZONE",
                0,
                "Old Zone");
            zone.ConfigureShape(
                """
                {"schemaVersion":1,"kind":"polygon","points":[[500,500],[3500,500],[3500,2500],[500,2500]]}
                """);
            var aisle = SpaceAisleRevision.Create(
                execution.TenantId,
                graph.VersionId,
                aisleLogicalId,
                zoneLogicalId,
                "OLD-AISLE",
                0,
                "Old Aisle");
            aisle.ConfigureShape("[]", "[[500,3500],[8000,3500]]");
            var rack = SpaceRackRevision.Create(
                execution.TenantId,
                graph.VersionId,
                rackLogicalId,
                floor.LogicalId,
                zoneLogicalId,
                "OLD-RACK",
                name: "Old Rack");
            rack.ConfigureGeometry(1200, 5100, 0, 0, 1800, 900, 2500);
            var retainedLevel = SpaceRackLevelRevision.Create(
                execution.TenantId,
                graph.VersionId,
                WarehouseDeterministicIdentity.CreateRackLevelLogicalId(
                    rackLogicalId,
                    1),
                rackLogicalId,
                1,
                0,
                900,
                1,
                1,
                900,
                900);
            var obsoleteLevel = SpaceRackLevelRevision.Create(
                execution.TenantId,
                graph.VersionId,
                WarehouseDeterministicIdentity.CreateRackLevelLogicalId(
                    rackLogicalId,
                    3),
                rackLogicalId,
                3,
                2100,
                500,
                1,
                1,
                900,
                900);
            var retainedLocation = SpaceLocationRevision.Create(
                execution.TenantId,
                graph.VersionId,
                WarehouseDeterministicIdentity.CreateLocationLogicalId(
                    rackLogicalId,
                    1,
                    1,
                    1),
                floor.LogicalId,
                rackLogicalId,
                null,
                1,
                1,
                1,
                900,
                900,
                900);
            var obsoleteLocation = SpaceLocationRevision.Create(
                execution.TenantId,
                graph.VersionId,
                WarehouseDeterministicIdentity.CreateLocationLogicalId(
                    rackLogicalId,
                    3,
                    1,
                    1),
                floor.LogicalId,
                rackLogicalId,
                null,
                1,
                3,
                1,
                900,
                500,
                900);
            var wall = SpaceElementRevision.Create(
                execution.TenantId,
                graph.VersionId,
                wallLogicalId,
                floor.LogicalId,
                "Wall",
                """
                {"schemaVersion":1,"kind":"box","width":1000,"height":1000,"depth":100}
                """);
            wall.ConfigurePlacement(500, 500, 0, 0, 1000, 1000, 100);
            context.AddRange(
                zone,
                aisle,
                rack,
                retainedLevel,
                obsoleteLevel,
                retainedLocation,
                obsoleteLocation,
                wall);
            await context.SaveChangesAsync();

            await AddAcceptedProposalAsync(
                context,
                execution,
                graph,
                "aisle-ai-1",
                "Aisle",
                """
                {"kind":"Path","points":[{"x":500,"y":3500,"z":0},{"x":9500,"y":4500,"z":0}]}
                """,
                """
                {"name":"AI Aisle","aisleCode":"AI-AISLE-1","direction":"TwoWay","widthMillimeters":1000,"heightMillimeters":3000}
                """,
                """{"zoneSourceKey":"zone-ai-1"}""");
            var profileVersionId = Guid.NewGuid();
            await AddAcceptedProposalAsync(
                context,
                execution,
                graph,
                "rack-ai-1",
                "Rack",
                """
                {"kind":"Polygon","points":[{"x":1000,"y":5000,"z":0},{"x":3000,"y":5000,"z":0},{"x":3000,"y":6000,"z":0},{"x":1000,"y":6000,"z":0}]}
                """,
                $$$"""
                {"name":"AI Rack","rackCode":"AI-RACK-1","rackType":"Selective","rackDerivation":{"profileVersionId":"{{{profileVersionId:D}}}","rackWidthMillimeters":2000,"rackDepthMillimeters":1000,"rackHeightMillimeters":3000,"levels":[{"levelNo":1,"bottomZMillimeters":0,"clearHeightMillimeters":1200,"binCount":2,"depthCount":1,"cellWidthMillimeters":1000,"cellDepthMillimeters":1000,"beamHeightMillimeters":100,"maxLoadKilograms":500},{"levelNo":2,"bottomZMillimeters":1400,"clearHeightMillimeters":1200,"binCount":2,"depthCount":1,"cellWidthMillimeters":1000,"cellDepthMillimeters":1000,"beamHeightMillimeters":100,"maxLoadKilograms":400}]}}
                """,
                """{"zoneSourceKey":"zone-ai-1"}""");
            await AddAcceptedProposalAsync(
                context,
                execution,
                graph,
                "wall-ai-1",
                "Wall",
                """
                {"kind":"Path","points":[{"x":500,"y":500,"z":0},{"x":9500,"y":600,"z":0}]}
                """,
                """
                {"name":"AI Safety Wall","thicknessMillimeters":100,"heightMillimeters":3000,"wallType":"Safety"}
                """,
                "{}");

            var queued = await QueueAsync(
                context,
                execution,
                clock,
                graph,
                "apply-update-baseline-1");
            var runner = Runner(
                context,
                execution,
                clock,
                new NoOpSpaceAiApplyFaultInjector());
            Assert.True(await runner.RunNextAsync(
                SpaceJobType.ApplyGeneration,
                "update-worker"));

            context.ChangeTracker.Clear();
            var updatedZone = await context.ZoneRevisions.SingleAsync();
            var updatedAisle = await context.AisleRevisions.SingleAsync();
            var updatedRack = await context.RackRevisions.SingleAsync();
            var updatedWall = await context.ElementRevisions.SingleAsync();
            Assert.Equal(zone.Id, updatedZone.Id);
            Assert.Equal("AI-ZONE-1", updatedZone.ZoneCode);
            Assert.Equal("AI Storage Zone", updatedZone.Name);
            Assert.Equal(aisle.Id, updatedAisle.Id);
            Assert.Equal("AI-AISLE-1", updatedAisle.AisleCode);
            Assert.Equal("AI Aisle", updatedAisle.Name);
            Assert.Equal(rack.Id, updatedRack.Id);
            Assert.Equal("AI-RACK-1", updatedRack.RackCode);
            Assert.Equal("Selective", updatedRack.RackType);
            Assert.Equal(wall.Id, updatedWall.Id);
            Assert.Equal("Wall", updatedWall.ElementType);
            Assert.Equal(3000, updatedWall.Height);
            Assert.Equal(2, await context.RackLevelRevisions.CountAsync(item =>
                item.LifecycleState == SpaceLifecycleState.Active));
            Assert.Equal(1, await context.RackLevelRevisions.CountAsync(item =>
                item.LifecycleState == SpaceLifecycleState.Disabled));
            Assert.Equal(4, await context.LocationRevisions.CountAsync(item =>
                item.LifecycleState == SpaceLifecycleState.Active));
            Assert.Equal(1, await context.LocationRevisions.CountAsync(item =>
                item.LifecycleState == SpaceLifecycleState.Disabled));
            Assert.Equal(1, (await context.Versions.SingleAsync()).ContentRevision);
            Assert.All(
                await context.ElementCommandRecords.ToListAsync(),
                command => Assert.NotEqual("null", command.BeforeJson));
            Assert.Equal(4, await context.ElementCommandRecords.CountAsync());
            Assert.Equal(queued.JobId, (await context.GenerationRuns.SingleAsync()).ApplyJobId);
        });
    }

    [Fact]
    public async Task External_principal_is_denied_before_generation_data_access()
    {
        var execution = new ExternalTestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());
        await using var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            execution,
            new MutableClock(Start));
        var service = new SpaceAiAtomicApplyService(
            context,
            execution,
            new AllowAccess(),
            new MutableClock(Start));

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.QueueAsync(
                Guid.NewGuid(),
                new CreateSpaceAiAtomicApplyRequest(0, "row", "review"),
                "external-apply"));

        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, error.Code);
        Assert.Equal(403, error.StatusCode);
        Assert.Empty(context.GenerationRuns);
        Assert.Empty(context.Jobs);
    }

    private static async Task<ApplyGraph> SeedReviewedZoneAsync(
        SpaceContext context,
        TestExecutionContext execution)
    {
        var model = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
        var version = SpaceModelVersion.CreateDraft(
            execution.TenantId,
            model.Id,
            1,
            "AI Apply draft");
        var floor = SpaceFloorRevision.Create(
            execution.TenantId,
            version.Id,
            Guid.NewGuid(),
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            height: 6_000);
        floor.ConfigureBoundary(
            """
            {"schemaVersion":1,"kind":"polygon","points":[[0,0],[10000,0],[10000,8000],[0,8000]]}
            """,
            "RH_Z_UP_MM");
        var source = SpaceModelSource.CreateInlineSource(
            execution.TenantId,
            version.Id,
            SpaceSourceType.Editor,
            "Reviewed normalized warehouse features",
            SourceHash);
        var buildJob = SpaceJob.CreateQueued(
            execution.TenantId,
            SpaceJobType.BuildScene,
            SpaceJobSubjectType.ModelVersion,
            version.Id,
            new string('b', 64),
            SourceHash,
            50,
            3,
            execution.ActorId,
            Start,
            Guid.NewGuid());
        var run = SpaceGenerationRun.Create(
            new SpaceGenerationRunDefinition(
                execution.TenantId,
                model.SiteId,
                version.Id,
                source.Id,
                SourceHash,
                0,
                new string('c', 64),
                new string('d', 64),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "rules-1",
                SpaceAiPolicySnapshot.StructuredFeatures,
                Guid.NewGuid(),
                "1.0",
                buildJob.Id,
                floor.LogicalId));
        run.BeginPreparing();
        run.BeginInferring();
        run.RecordProviderResult("local-v1", "warehouse-v1", "1.0");
        run.BeginValidating();
        run.MarkAwaitingReview();
        run.MarkReviewCompleted(Start);

        const string geometry =
            """{"kind":"Polygon","points":[{"x":1000,"y":1000,"z":0},{"x":4000,"y":1000,"z":0},{"x":4000,"y":3000,"z":0},{"x":1000,"y":3000,"z":0}]}""";
        const string attributes =
            """{"name":"AI Storage Zone","zoneCode":"AI-ZONE-1","zonePurpose":"Storage"}""";
        const string relations = "{}";
        const string finalSnapshot =
            """{"proposalType":"Zone","geometry":{"kind":"Polygon","points":[{"x":1000,"y":1000,"z":0},{"x":4000,"y":1000,"z":0},{"x":4000,"y":3000,"z":0},{"x":1000,"y":3000,"z":0}]},"attributes":{"name":"AI Storage Zone","zoneCode":"AI-ZONE-1","zonePurpose":"Storage"},"relations":{}}""";
        var proposal = SpaceGenerationProposal.Create(
            new SpaceGenerationProposalDefinition(
                execution.TenantId,
                run.Id,
                version.Id,
                0,
                SourceHash,
                "zone-ai-1",
                "Zone",
                geometry,
                attributes,
                relations,
                "[]",
                "[]",
                "{}",
                0.97m,
                SpaceConfidenceBand.High,
                false));
        proposal.Accept();
        var decision = SpaceProposalDecision.Create(
            execution.TenantId,
            run.Id,
            proposal.Id,
            SpaceProposalDecisionType.Accept,
            finalSnapshot,
            finalSnapshot,
            null,
            "REVIEWED",
            "Reviewed against the normalized source.",
            Guid.NewGuid());

        context.AddRange(
            model,
            version,
            floor,
            source,
            buildJob,
            run,
            proposal,
            decision);
        await context.SaveChangesAsync();
        return new ApplyGraph(run.Id, version.Id, proposal.Id);
    }

    private static async Task AddAcceptedProposalAsync(
        SpaceContext context,
        TestExecutionContext execution,
        ApplyGraph graph,
        string sourceKey,
        string proposalType,
        string geometryJson,
        string attributesJson,
        string relationsJson)
    {
        var proposal = SpaceGenerationProposal.Create(
            new SpaceGenerationProposalDefinition(
                execution.TenantId,
                graph.RunId,
                graph.VersionId,
                0,
                SourceHash,
                sourceKey,
                proposalType,
                geometryJson,
                attributesJson,
                relationsJson,
                "[]",
                "[]",
                "{}",
                0.95m,
                SpaceConfidenceBand.High,
                false));
        proposal.Accept();
        var finalSnapshot =
            $"{{\"proposalType\":\"{proposalType}\"," +
            $"\"geometry\":{geometryJson}," +
            $"\"attributes\":{attributesJson}," +
            $"\"relations\":{relationsJson}}}";
        var decision = SpaceProposalDecision.Create(
            execution.TenantId,
            graph.RunId,
            proposal.Id,
            SpaceProposalDecisionType.Accept,
            finalSnapshot,
            finalSnapshot,
            null,
            "REVIEWED",
            "Reviewed hierarchy proposal.",
            Guid.NewGuid());
        context.AddRange(proposal, decision);
        await context.SaveChangesAsync();
    }

    private static async Task<QueuedApply> QueueAsync(
        SpaceContext context,
        TestExecutionContext execution,
        MutableClock clock,
        ApplyGraph graph,
        string idempotencyKey = "apply-zone-1")
    {
        context.ChangeTracker.Clear();
        var reviewService = new SpaceAiProposalDecisionService(
            context,
            execution,
            new AllowAccess(),
            new TestCursorCodec(),
            clock,
            new SpaceAiProposalReviewOptions());
        var review = await reviewService.GetReviewAsync(graph.RunId);
        var service = new SpaceAiAtomicApplyService(
            context,
            execution,
            new AllowAccess(),
            clock);
        var request = new CreateSpaceAiAtomicApplyRequest(
            review.BaseContentRevision,
            review.RunRowVersion,
            review.ReviewEtag);
        return new QueuedApply(
            await service.QueueAsync(
                graph.RunId,
                request,
                idempotencyKey),
            request);
    }

    private static SpaceJobProcessorRunner Runner(
        SpaceContext context,
        TestExecutionContext execution,
        MutableClock clock,
        ISpaceAiApplyFaultInjector faultInjector) =>
        new(
            new EfSpaceJobLeaseStore(context, clock),
            [
                new SpaceGenerationApplyJobProcessor(
                    new SpaceGenerationApplyStepExecutor(
                        context,
                        execution,
                        clock,
                        faultInjector)),
            ]);

    private static SpaceAiRunRecoveryService RecoveryService(
        SpaceContext context,
        TestExecutionContext execution,
        MutableClock clock)
    {
        var access = new AllowAccess();
        return new SpaceAiRunRecoveryService(
            context,
            execution,
            access,
            clock,
            new SpaceAiLockedFactService(context, execution, access));
    }

    private static async Task WithDatabaseAsync(
        Func<SpaceContext, TestExecutionContext, MutableClock, Task> action)
    {
        var tenantId = Guid.NewGuid();
        var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
        var clock = new MutableClock(Start);
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE13S10_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var context = CreateContext(
            connectionString,
            execution,
            clock);
        try
        {
            await context.Database.MigrateAsync();
            await action(context, execution, clock);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        ISpaceExecutionContext execution,
        ISpaceClock clock) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            execution,
            clock);

    private sealed record ApplyGraph(
        Guid RunId,
        Guid VersionId,
        Guid ProposalId);

    private sealed record QueuedApply(
        SpaceAiAtomicApplyAcceptedDto Response,
        CreateSpaceAiAtomicApplyRequest Request)
    {
        public Guid JobId => Response.JobId;
        public bool IdempotentReplay => Response.IdempotentReplay;
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;

    private sealed record ExternalTestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext
    {
        public bool IsExternal => true;
    }

    private sealed class MutableClock(DateTime utcNow) : ISpaceClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(state.Resource));

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new NotSupportedException();
    }

    private sealed class OneShotFaultInjector(string checkpoint)
        : ISpaceAiApplyFaultInjector
    {
        private bool _armed = true;

        public void ThrowIfRequested(string currentCheckpoint)
        {
            if (_armed && currentCheckpoint == checkpoint)
            {
                _armed = false;
                throw new InvalidOperationException("Injected Apply fault.");
            }
        }
    }
}
