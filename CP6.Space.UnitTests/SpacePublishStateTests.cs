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
            new string('a', 64));

        Assert.Throws<SpaceVersionStateException>(batch.MarkVerified);

        batch.BeginApply();
        batch.RecordResult(
            SpacePublishBatchStatus.Applied,
            "external-1",
            "{}",
            Now);
        batch.MarkVerified();

        Assert.Equal(SpacePublishBatchStatus.Verified, batch.Status);
        Assert.Equal(1, batch.AttemptCount);
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
            Now,
            Guid.NewGuid());
}
