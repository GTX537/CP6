using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceJobLedgerTests
{
    private static readonly Guid TenantId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid ActorId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Guid CorrelationId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly DateTime Now =
        new(2026, 7, 26, 13, 0, 0, DateTimeKind.Utc);

    private const string InputHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Business_key_is_server_deterministic_and_input_bound()
    {
        var request = NewRequest();

        var first = SpaceJobBusinessKey.Create(request);
        var second = SpaceJobBusinessKey.Create(request);
        var changed = SpaceJobBusinessKey.Create(
            request with { ProcessorVersion = "parser-v2" });

        Assert.Equal(64, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public async Task Coordinator_reuses_an_active_job()
    {
        var queue = new FakeJobQueue();
        var coordinator = NewCoordinator(queue);

        var first = await coordinator.EnqueueAsync(NewRequest());
        var second = await coordinator.EnqueueAsync(NewRequest());

        Assert.False(first.Reused);
        Assert.True(second.Reused);
        Assert.Same(first.Job, second.Job);
        Assert.Single(queue.Jobs);
        Assert.Equal(1, queue.SaveCount);
    }

    [Fact]
    public void Claim_renew_progress_and_complete_follow_the_lease()
    {
        var job = NewJob();
        var attempt = job.Claim(
            "worker-a",
            "parser-v1",
            Now,
            TimeSpan.FromSeconds(60));

        job.RenewLease(
            attempt.Id,
            "worker-a",
            Now.AddSeconds(20),
            TimeSpan.FromSeconds(60));
        job.ReportProgress(
            attempt.Id,
            "worker-a",
            4,
            10,
            "Normalize",
            Now.AddSeconds(21));
        job.Complete(
            attempt.Id,
            "worker-a",
            Now.AddSeconds(22),
            """{"items":4}""");
        attempt.Succeed(Now.AddSeconds(22));

        Assert.Equal(SpaceJobStatus.Succeeded, job.Status);
        Assert.Equal(10, job.ProgressDone);
        Assert.Equal(10, job.ProgressTotal);
        Assert.Equal("Completed", job.ProgressStage);
        Assert.Null(job.ActiveAttemptId);
        Assert.Equal(SpaceJobAttemptOutcome.Succeeded, attempt.Outcome);
    }

    [Fact]
    public void Progress_is_monotonic_and_total_is_stable()
    {
        var job = NewJob();
        var attempt = job.Claim(
            "worker-a",
            "parser-v1",
            Now,
            TimeSpan.FromMinutes(1));
        job.ReportProgress(
            attempt.Id,
            "worker-a",
            5,
            10,
            "Convert",
            Now.AddSeconds(1));

        Assert.Throws<SpaceJobStateException>(() =>
            job.ReportProgress(
                attempt.Id,
                "worker-a",
                4,
                10,
                "Convert",
                Now.AddSeconds(2)));
        Assert.Throws<SpaceJobStateException>(() =>
            job.ReportProgress(
                attempt.Id,
                "worker-a",
                6,
                12,
                "Convert",
                Now.AddSeconds(2)));
    }

    [Fact]
    public void Expired_takeover_fences_the_old_attempt()
    {
        var job = NewJob();
        var first = job.Claim(
            "worker-a",
            "parser-v1",
            Now,
            TimeSpan.FromSeconds(60));
        var second = job.Claim(
            "worker-b",
            "parser-v1",
            Now.AddSeconds(61),
            TimeSpan.FromSeconds(60));

        Assert.Equal(2, second.AttemptNo);
        Assert.Equal(second.Id, job.ActiveAttemptId);
        Assert.Throws<SpaceJobLeaseLostException>(() =>
            job.ReportProgress(
                first.Id,
                "worker-a",
                1,
                1,
                "Stale",
                Now.AddSeconds(62)));
    }

    [Theory]
    [InlineData(SpaceJobFailureKind.Transient, SpaceJobStatus.Queued)]
    [InlineData(SpaceJobFailureKind.Bug, SpaceJobStatus.Queued)]
    [InlineData(SpaceJobFailureKind.Resource, SpaceJobStatus.Failed)]
    [InlineData(SpaceJobFailureKind.Input, SpaceJobStatus.Failed)]
    [InlineData(SpaceJobFailureKind.Security, SpaceJobStatus.Failed)]
    public void Retry_policy_classifies_failures(
        SpaceJobFailureKind kind,
        SpaceJobStatus expected)
    {
        var decision = SpaceJobRetryPolicy.DecideAutomatic(kind, 1, 5, Now);

        Assert.Equal(expected, decision.NextStatus);
        Assert.Equal(
            expected == SpaceJobStatus.Queued,
            decision.NextAttemptAtUtc.HasValue);
    }

    [Fact]
    public void Automatic_retry_uses_exponential_backoff_and_then_deadletters()
    {
        var first = SpaceJobRetryPolicy.DecideAutomatic(
            SpaceJobFailureKind.Transient,
            1,
            3,
            Now);
        var second = SpaceJobRetryPolicy.DecideAutomatic(
            SpaceJobFailureKind.Transient,
            2,
            3,
            Now);
        var final = SpaceJobRetryPolicy.DecideAutomatic(
            SpaceJobFailureKind.Transient,
            3,
            3,
            Now);

        Assert.Equal(Now.AddSeconds(5), first.NextAttemptAtUtc);
        Assert.Equal(Now.AddSeconds(10), second.NextAttemptAtUtc);
        Assert.Equal(SpaceJobStatus.DeadLetter, final.NextStatus);
    }

    [Fact]
    public void Running_cancellation_waits_for_a_safe_checkpoint()
    {
        var job = NewJob();
        var attempt = job.Claim(
            "worker-a",
            "parser-v1",
            Now,
            TimeSpan.FromMinutes(1));

        job.RequestCancellation(ActorId, Now.AddSeconds(1));

        Assert.Equal(SpaceJobStatus.Running, job.Status);
        Assert.Throws<SpaceJobStateException>(() =>
            job.Complete(
                attempt.Id,
                "worker-a",
                Now.AddSeconds(2)));

        job.AcknowledgeCancellation(
            attempt.Id,
            "worker-a",
            Now.AddSeconds(2));
        attempt.Cancel(Now.AddSeconds(2));
        Assert.Equal(SpaceJobStatus.Cancelled, job.Status);
        Assert.Equal(SpaceJobAttemptOutcome.Cancelled, attempt.Outcome);
    }

    [Fact]
    public void Security_failure_and_unchanged_input_failure_are_not_retryable()
    {
        var security = FailTerminal(SpaceJobFailureKind.Security);
        Assert.Throws<SpaceJobNotRetryableException>(() =>
            security.CreateExplicitRetry(
                new string('b', 64),
                InputHash,
                ActorId,
                Now.AddMinutes(1),
                CorrelationId));

        var input = FailTerminal(SpaceJobFailureKind.Input);
        Assert.Throws<SpaceJobNotRetryableException>(() =>
            input.CreateExplicitRetry(
                input.BusinessKey,
                InputHash,
                ActorId,
                Now.AddMinutes(1),
                CorrelationId));
    }

    [Fact]
    public void Warning_acknowledgement_records_actor_and_reason_but_blocking_fails_closed()
    {
        var warning = SpaceModelIssue.Create(
            TenantId,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            SpaceIssueSeverity.Warning,
            "CAD_LAYER_AMBIGUOUS");
        warning.AcknowledgeWarning(
            ActorId,
            "Warehouse owner confirmed the intended layer.",
            Now);

        Assert.Equal(SpaceIssueStatus.Acknowledged, warning.Status);
        Assert.Equal(ActorId, warning.AcknowledgedBy);
        Assert.NotNull(warning.AcknowledgementReason);

        var blocking = SpaceModelIssue.Create(
            TenantId,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            SpaceIssueSeverity.Blocking,
            "CAD_UNIT_REQUIRED");
        Assert.Throws<SpaceJobStateException>(() =>
            blocking.AcknowledgeWarning(ActorId, "Ignore", Now));
    }

    private static SpaceJob FailTerminal(SpaceJobFailureKind kind)
    {
        var job = NewJob();
        var attempt = job.Claim(
            "worker-a",
            "parser-v1",
            Now,
            TimeSpan.FromMinutes(1));
        var decision = SpaceJobRetryPolicy.DecideAutomatic(kind, 1, 5, Now);
        job.Fail(
            attempt.Id,
            "worker-a",
            kind,
            "TEST_FAILURE",
            "Sanitized failure.",
            decision,
            Now.AddSeconds(1));
        return job;
    }

    private static SpaceJob NewJob(int maxAttempts = 5) =>
        SpaceJob.CreateQueued(
            TenantId,
            SpaceJobType.CadParse,
            SpaceJobSubjectType.ModelSource,
            Guid.NewGuid(),
            new string('b', 64),
            InputHash,
            10,
            maxAttempts,
            ActorId,
            Now,
            CorrelationId);

    private static SpaceJobEnqueueRequest NewRequest() =>
        new(
            SpaceJobType.CadParse,
            SpaceJobSubjectType.ModelSource,
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            InputHash,
            "parser-v1",
            "mapping-v3",
            Priority: 10,
            MaxAttempts: 5);

    private static SpaceJobCoordinator NewCoordinator(FakeJobQueue queue) =>
        new(
            new TestExecutionContext(TenantId, ActorId, CorrelationId),
            new FixedClock(),
            queue);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        Guid CorrelationId)
        : ISpaceExecutionContext, ISpaceCorrelationContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class FakeJobQueue : ISpaceJobQueue
    {
        public List<SpaceJob> Jobs { get; } = [];
        public int SaveCount { get; private set; }

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
                    job.TenantId == tenantId &&
                    job.Id == jobId));

        public Task<SpaceJobEnqueueResult> AddOrGetActiveAsync(
            SpaceJob job,
            CancellationToken cancellationToken = default)
        {
            Jobs.Add(job);
            SaveCount++;
            return Task.FromResult(
                new SpaceJobEnqueueResult(job, Reused: false));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
