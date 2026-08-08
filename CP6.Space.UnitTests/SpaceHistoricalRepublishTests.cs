using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceHistoricalRepublishTests
{
    private static readonly DateTime Now =
        new(2026, 8, 7, 17, 10, 0, DateTimeKind.Utc);

    [Fact]
    public void Passed_operation_preserves_lineage_and_binds_new_publish_attempt()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var historicalVersionId = Guid.NewGuid();
        var expectedPublishedVersionId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var operation = SpaceHistoricalRepublish.Create(
            tenantId,
            siteId,
            modelId,
            historicalVersionId,
            expectedPublishedVersionId,
            "restore-key",
            new string('A', 64),
            "  Restore the verified layout.  ",
            "  CAB-42  ",
            requestedBy,
            Now,
            Guid.NewGuid());
        var targetVersionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var validationRunId = Guid.NewGuid();
        var publishAttemptId = Guid.NewGuid();

        operation.BindReservation(targetVersionId, jobId);
        operation.MarkSnapshotCloned();
        operation.MarkValidationPassed(validationRunId);
        operation.MarkPublishQueued(publishAttemptId);

        Assert.Equal(tenantId, operation.TenantId);
        Assert.Equal(siteId, operation.SiteId);
        Assert.Equal(modelId, operation.ModelId);
        Assert.Equal(historicalVersionId, operation.HistoricalVersionId);
        Assert.Equal(
            expectedPublishedVersionId,
            operation.ExpectedPublishedVersionId);
        Assert.Equal(targetVersionId, operation.TargetVersionId);
        Assert.Equal(jobId, operation.JobId);
        Assert.Equal(validationRunId, operation.ValidationRunId);
        Assert.Equal(publishAttemptId, operation.PublishAttemptId);
        Assert.Equal(requestedBy, operation.RequestedBy);
        Assert.Equal("Restore the verified layout.", operation.Reason);
        Assert.Equal("CAB-42", operation.ApprovalReference);
        Assert.Equal(new string('a', 64), operation.RequestHash);
        Assert.Equal(
            SpaceHistoricalRepublishStatus.PublishQueued,
            operation.Status);

        operation.BindReservation(targetVersionId, jobId);
        operation.MarkSnapshotCloned();
        operation.MarkValidationPassed(validationRunId);
        operation.MarkPublishQueued(publishAttemptId);
    }

    [Fact]
    public void Blocked_validation_cannot_be_rebound_or_published()
    {
        var operation = Create();
        operation.BindReservation(Guid.NewGuid(), Guid.NewGuid());
        operation.MarkSnapshotCloned();
        operation.MarkValidationBlocked(Guid.NewGuid());

        Assert.Equal(
            SpaceHistoricalRepublishStatus.ValidationBlocked,
            operation.Status);
        Assert.Throws<SpaceVersionStateException>(
            () => operation.MarkValidationPassed(Guid.NewGuid()));
        Assert.Throws<SpaceVersionStateException>(
            () => operation.MarkPublishQueued(Guid.NewGuid()));
    }

    [Fact]
    public void Reservation_and_validation_bindings_are_immutable()
    {
        var operation = Create();
        operation.BindReservation(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<SpaceVersionStateException>(
            () => operation.BindReservation(Guid.NewGuid(), Guid.NewGuid()));

        operation.MarkSnapshotCloned();
        operation.MarkValidationPassed(Guid.NewGuid());

        Assert.Throws<SpaceVersionStateException>(
            () => operation.MarkValidationPassed(Guid.NewGuid()));
    }

    [Fact]
    public void Processor_exposes_three_ordered_recovery_steps()
    {
        Assert.Equal(
            new[]
            {
                SpaceHistoricalRepublishJobSteps.CloneHistoricalSnapshot,
                SpaceHistoricalRepublishJobSteps.ValidateHistoricalSnapshot,
                SpaceHistoricalRepublishJobSteps.QueuePublish,
            },
            SpaceHistoricalRepublishJobSteps.All);
    }

    private static SpaceHistoricalRepublish Create() =>
        SpaceHistoricalRepublish.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "restore-key",
            new string('b', 64),
            "Restore the verified layout.",
            approvalReference: null,
            Guid.NewGuid(),
            Now,
            Guid.NewGuid());
}
