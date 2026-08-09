using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpacePublishStateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now =
        new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Completed_attempt_releases_the_site_slot()
    {
        var attempt = CreateAttempt();

        attempt.BeginPreflight();
        attempt.BeginApplyingWms();
        attempt.BeginVerifyingWms(Now);
        attempt.BeginActivatingRuntime();
        attempt.Complete(Now.AddSeconds(1), "Published.");

        Assert.Equal(SpacePublishAttemptStatus.Completed, attempt.Status);
        Assert.Equal(SpacePublishStep.Complete, attempt.CurrentStep);
        Assert.False(attempt.OwnsPublishSlot);
        Assert.Equal(Now, attempt.WmsCommittedAtUtc);
        Assert.Equal(Now.AddSeconds(1), attempt.RuntimeActivatedAtUtc);
    }

    [Fact]
    public void Partial_external_effect_keeps_slot_for_reconciliation()
    {
        var attempt = CreateAttempt();

        attempt.BeginPreflight();
        attempt.BeginApplyingWms();
        attempt.RequireReconciliation(
            "SPACE_WMS_PARTIAL_RESULT",
            "One location was applied.");

        Assert.Equal(
            SpacePublishAttemptStatus.ReconciliationRequired,
            attempt.Status);
        Assert.Equal(SpacePublishStep.Reconcile, attempt.CurrentStep);
        Assert.True(attempt.OwnsPublishSlot);
        Assert.Null(attempt.FinishedAtUtc);
    }

    [Fact]
    public void Preflight_failure_is_terminal_and_releases_slot()
    {
        var attempt = CreateAttempt();

        attempt.BeginPreflight();
        attempt.FailNoEffect(
            "SPACE_LOCATION_IN_USE",
            "No WMS mutation occurred.",
            Now);

        Assert.Equal(
            SpacePublishAttemptStatus.FailedNoEffect,
            attempt.Status);
        Assert.False(attempt.OwnsPublishSlot);
        Assert.Equal(Now, attempt.FinishedAtUtc);
    }

    [Fact]
    public void Batch_must_apply_before_it_can_be_verified()
    {
        var batch = SpacePublishBatch.Create(
            TenantId,
            Guid.NewGuid(),
            1,
            "space:tenant:site:attempt:1",
            new string('a', 64),
            "{\"items\":[{}]}");

        Assert.Throws<SpaceVersionStateException>(batch.MarkVerified);

        batch.BeginApply(1);
        batch.RecordResult(
            SpacePublishBatchStatus.Applied,
            "external-1",
            "{}",
            Now);
        batch.MarkVerified();

        Assert.Equal(SpacePublishBatchStatus.Verified, batch.Status);
        Assert.Equal(1, batch.AttemptCount);
        Assert.Equal(1, batch.BatchAttemptNo);
    }

    [Fact]
    public void Timeout_waits_for_retry_without_releasing_production_slot()
    {
        var attempt = CreateAttempt();
        attempt.BeginPreflight();

        attempt.WaitForRetry(
            SpacePublishStep.Preflight,
            "SPACE_WMS_UNAVAILABLE",
            "WMS timed out.");

        Assert.Equal(SpacePublishAttemptStatus.WaitingRetry, attempt.Status);
        Assert.Equal(SpacePublishStep.Preflight, attempt.CurrentStep);
        Assert.True(attempt.OwnsPublishSlot);
        Assert.Null(attempt.FinishedAtUtc);
    }

    [Fact]
    public void Manual_recovery_rebinds_job_and_tracks_operator()
    {
        var attempt = CreateAttempt();
        var originalJobId = Guid.NewGuid();
        var retryJobId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        attempt.BindInitialJob(originalJobId);
        attempt.BeginPreflight();
        attempt.WaitForRetry(
            SpacePublishStep.Preflight,
            "SPACE_WMS_UNAVAILABLE",
            "WMS timed out.");
        attempt.RequireManualIntervention(
            "SPACE_WMS_UNAVAILABLE",
            "Automatic retries were exhausted.");

        attempt.ScheduleManualRetry(
            retryJobId,
            actorId,
            Now.AddMinutes(1),
            reconciliation: false);

        Assert.Equal(retryJobId, attempt.JobId);
        Assert.Equal(1, attempt.ManualRetryCount);
        Assert.Equal(actorId, attempt.LastRetriedBy);
        Assert.Equal(SpacePublishAttemptStatus.WaitingRetry, attempt.Status);
    }

    [Fact]
    public void Audit_events_form_a_deterministic_tamper_evident_chain()
    {
        var attemptId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var first = SpacePublishAuditEvent.Create(
            TenantId,
            attemptId,
            jobId,
            batchId: null,
            eventNo: 1,
            SpacePublishAuditEventType.Queued,
            SpacePublishAttemptStatus.Requested,
            SpacePublishStep.Preflight,
            actorId,
            correlationId,
            Now,
            "job:queued",
            "Queued.",
            errorCode: null,
            "{\"phase\":\"queued\"}",
            previousEventHash: null);
        var second = SpacePublishAuditEvent.Create(
            TenantId,
            attemptId,
            jobId,
            batchId: null,
            eventNo: 2,
            SpacePublishAuditEventType.ProcessingStarted,
            SpacePublishAttemptStatus.Preflighting,
            SpacePublishStep.Preflight,
            actorId,
            correlationId,
            Now.AddSeconds(1),
            "job:started",
            "Started.",
            errorCode: null,
            "{\"phase\":\"started\"}",
            first.EventHash);

        Assert.Equal(first.EventHash, second.PreviousEventHash);
        Assert.Equal(64, first.EvidenceHash.Length);
        Assert.Equal(64, first.EventHash.Length);
        Assert.NotEqual(first.EventHash, second.EventHash);
        Assert.Throws<ArgumentException>(() =>
            SpacePublishAuditEvent.Create(
                TenantId,
                attemptId,
                jobId,
                batchId: null,
                eventNo: 3,
                SpacePublishAuditEventType.PreflightPassed,
                SpacePublishAttemptStatus.ApplyingWms,
                SpacePublishStep.ApplyWms,
                actorId,
                correlationId,
                Now.AddSeconds(2),
                "job:invalid",
                "Invalid evidence.",
                errorCode: null,
                "not-json",
                second.EventHash));
    }

    private static SpacePublishAttempt CreateAttempt() =>
        SpacePublishAttempt.Create(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            baseVersionId: null,
            "cp6-wms-v1",
            "publish-key",
            new string('b', 64),
            Guid.NewGuid(),
            approvedBy: null,
            approvalReference: null,
            "{}",
            Now,
            Guid.NewGuid());
}
