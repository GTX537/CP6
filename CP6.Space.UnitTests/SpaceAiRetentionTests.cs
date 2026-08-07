using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceAiRetentionTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VersionId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SourceId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid JobId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTime Now =
        new(2026, 8, 6, 17, 30, 0, DateTimeKind.Utc);
    private static readonly string SourceHash = new('a', 64);

    [Fact]
    public void Policy_rejects_short_retention_and_invalid_batch_sizes()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SpaceAiRetentionOptions
            {
                RunPayloadRetention = TimeSpan.FromDays(89),
            }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new SpaceAiRetentionOptions
            {
                UsageRetention = TimeSpan.FromDays(364),
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SpaceAiRetentionOptions { BatchSize = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SpaceAiRetentionOptions { BatchSize = 1001 }.Validate());
    }

    [Fact]
    public void Payload_freezes_daily_cutoffs_and_rejects_schema_drift()
    {
        var payload = SpaceAiRetentionJobPayload.Create(
            new SpaceAiRetentionOptions(),
            Now);
        var json = SpaceAiRetentionPayloadCodec.Serialize(payload);

        Assert.Equal(
            new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc),
            payload.WindowEndUtc);
        Assert.Equal(payload.WindowEndUtc.AddDays(-90), payload.RunPayloadCutoffUtc);
        Assert.Equal(payload.WindowEndUtc.AddDays(-365), payload.UsageArchiveCutoffUtc);
        Assert.Equal(payload, SpaceAiRetentionPayloadCodec.ParsePayload(json));
        Assert.Throws<SpaceAiRetentionPayloadException>(() =>
            SpaceAiRetentionPayloadCodec.ParsePayload(
                json[..^1] + ",\"unexpected\":true}"));
        Assert.Throws<SpaceAiRetentionPayloadException>(() =>
            (payload with { BatchSize = 0 }).Validate(Now));
        Assert.Throws<SpaceAiRetentionPayloadException>(() =>
            (payload with { WindowEndUtc = Now.AddDays(1).Date }).Validate(Now));
    }

    [Fact]
    public void Run_hold_is_extension_only_and_purge_is_terminal_and_idempotent()
    {
        var run = NewStaleRun();
        run.ExtendRetentionHold(Now.AddDays(1));

        Assert.Throws<SpaceGenerationStateException>(() =>
            run.ExtendRetentionHold(Now));
        Assert.Throws<SpaceGenerationStateException>(() =>
            run.PurgeRetainedPayload(Now));

        var purgedAt = Now.AddDays(2);
        Assert.True(run.PurgeRetainedPayload(purgedAt));
        Assert.False(run.PurgeRetainedPayload(purgedAt.AddMinutes(1)));
        Assert.Equal(purgedAt, run.PayloadPurgedAtUtc);
        Assert.Equal(SourceHash, run.SourceHash);
        Assert.Equal(SpaceGenerationRunStatus.Stale, run.Status);
    }

    [Fact]
    public void Proposal_and_issue_purge_only_large_payloads()
    {
        var run = NewStaleRun();
        var proposal = NewProposal(run.Id);
        proposal.Modify("[{\"op\":\"replace\"}]", "[\"/name\"]");
        var issue = SpaceModelIssue.Create(
            TenantId,
            VersionId,
            SourceId,
            JobId,
            SpaceIssueSeverity.Warning,
            "AI_LOW_CONFIDENCE",
            "layer:racks/block:42",
            messageArgsJson: "{\"confidence\":0.4}",
            generationRunId: run.Id,
            generationProposalId: proposal.Id);
        var staging = SpaceGenerationStagingElement.Create(
            TenantId,
            run.Id,
            proposal.Id,
            VersionId,
            0,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Rack",
            "{\"name\":\"temporary\"}");

        Assert.True(proposal.PurgeRetainedPayload(Now));
        Assert.True(issue.PurgeRetainedPayload(Now));
        Assert.True(staging.RetireForRetention());
        Assert.False(proposal.PurgeRetainedPayload(Now.AddMinutes(1)));
        Assert.False(issue.PurgeRetainedPayload(Now.AddMinutes(1)));
        Assert.False(staging.RetireForRetention());

        Assert.Equal(SourceHash, proposal.SourceHash);
        Assert.Equal(SpaceGenerationProposalStatus.Modified, proposal.Status);
        Assert.Equal("{}", proposal.SuggestedGeometryJson);
        Assert.Equal("[]", proposal.EvidenceJson);
        Assert.Null(proposal.HumanPatchJson);
        Assert.Equal("AI_LOW_CONFIDENCE", issue.Code);
        Assert.Equal(SpaceIssueStatus.Open, issue.Status);
        Assert.Null(issue.SourceRef);
        Assert.Equal("{}", issue.MessageArgsJson);
        Assert.True(staging.IsDeleted);
        Assert.Equal("{}", staging.NormalizedPayloadJson);
    }

    [Fact]
    public void Usage_is_not_archived_before_365_days_and_is_idempotent()
    {
        var usage = NewUsage(Now.AddDays(-365));

        Assert.Throws<SpaceAiCapacityStateException>(() =>
            NewUsage(Now.AddDays(-364)).ArchiveForRetention(Now));
        Assert.True(usage.ArchiveForRetention(Now));
        Assert.False(usage.ArchiveForRetention(Now.AddMinutes(1)));
        Assert.Equal(Now, usage.ArchivedAtUtc);
        Assert.Equal(10, usage.InputUnits);
    }

    [Fact]
    public async Task Coordinator_is_restricted_and_queues_tenant_daily_job()
    {
        var deniedQueue = new FakeJobQueue();
        var denied = NewCoordinator(
            deniedQueue,
            new TestExecutionContext(TenantId, ActorId, IsExternal: false),
            allowed: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => denied.QueueAsync());
        Assert.Empty(deniedQueue.Jobs);

        var external = NewCoordinator(
            deniedQueue,
            new TestExecutionContext(TenantId, ActorId, IsExternal: true),
            allowed: true);
        var problem = await Assert.ThrowsAsync<SpaceProblemException>(
            () => external.QueueAsync());
        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, problem.Code);

        var queue = new FakeJobQueue();
        var result = await NewCoordinator(
            queue,
            new TestExecutionContext(TenantId, ActorId, IsExternal: false),
            allowed: true).QueueAsync();

        Assert.False(result.Reused);
        Assert.Equal(SpaceJobType.AiRetentionCleanup, result.Job.JobType);
        Assert.Equal(SpaceJobSubjectType.Tenant, result.Job.SubjectType);
        Assert.Equal(TenantId, result.Job.SubjectId);
        Assert.Equal(5, result.Job.MaxAttempts);
        var payload = SpaceAiRetentionPayloadCodec
            .ParsePayload(result.Job.PayloadJson)
            .Validate(Now);
        Assert.Equal(new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc), payload.WindowEndUtc);
    }

    private static SpaceAiRetentionCoordinator NewCoordinator(
        FakeJobQueue queue,
        TestExecutionContext execution,
        bool allowed) =>
        new(
            execution,
            new FixedClock(),
            new RetentionAuthorization(allowed),
            new SpaceJobCoordinator(execution, new FixedClock(), queue),
            new SpaceAiRetentionOptions());

    private static SpaceGenerationRun NewStaleRun()
    {
        var run = SpaceGenerationRun.Create(
            new SpaceGenerationRunDefinition(
                TenantId,
                Guid.NewGuid(),
                VersionId,
                SourceId,
                SourceHash,
                7,
                new string('b', 64),
                new string('c', 64),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "rules-1",
                SpaceAiPolicySnapshot.StructuredFeatures,
                Guid.NewGuid(),
                "1.0",
                JobId));
        run.BeginPreparing();
        run.BeginInferring();
        run.RecordDegradedReason("provider-timeout");
        run.BeginValidating();
        run.MarkAwaitingReview();
        run.MarkStale();
        return run;
    }

    private static SpaceGenerationProposal NewProposal(Guid runId) =>
        SpaceGenerationProposal.Create(
            new SpaceGenerationProposalDefinition(
                TenantId,
                runId,
                VersionId,
                7,
                SourceHash,
                "layer:racks/block:42",
                "Rack",
                "{\"type\":\"Polygon\"}",
                "{\"name\":\"Rack 42\"}",
                "[]",
                "[\"entity:42\"]",
                "[{\"confidence\":0.9}]",
                "{\"name\":\"ai\"}",
                0.9m,
                SpaceConfidenceBand.High,
                false));

    private static SpaceAiUsageRecord NewUsage(DateTime recordedAtUtc) =>
        SpaceAiUsageRecord.Create(
            TenantId,
            Guid.NewGuid(),
            "local-v1",
            "warehouse-v1",
            new string('d', 64),
            10,
            5,
            7,
            7,
            "USD",
            120,
            SpaceAiUsageOutcome.Succeeded,
            recordedAtUtc);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        bool IsExternal) : ISpaceExecutionContext, ISpaceCorrelationContext
    {
        public Guid CorrelationId { get; } = Guid.NewGuid();
    }

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed record RetentionAuthorization(bool Allowed) :
        ISpaceAiRetentionAuthorization
    {
        public bool IsRetentionServicePrincipal => Allowed;
    }

    private sealed class FakeJobQueue : ISpaceJobQueue
    {
        public List<SpaceJob> Jobs { get; } = [];

        public Task<SpaceJob?> FindActiveAsync(
            Guid tenantId,
            SpaceJobType jobType,
            string businessKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Jobs.SingleOrDefault(job =>
                    job.TenantId == tenantId &&
                    job.JobType == jobType &&
                    job.BusinessKey == businessKey &&
                    !job.IsTerminal));

        public Task<SpaceJob?> FindByIdAsync(
            Guid tenantId,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Jobs.SingleOrDefault(job =>
                    job.TenantId == tenantId && job.Id == jobId));

        public Task<SpaceJobEnqueueResult> AddOrGetActiveAsync(
            SpaceJob job,
            CancellationToken cancellationToken = default)
        {
            Jobs.Add(job);
            return Task.FromResult(new SpaceJobEnqueueResult(job, false));
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
