using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpacePublishWarningAcknowledgementTests
{
    [Fact]
    public void Bound_hash_is_order_independent_and_validation_scoped()
    {
        var validationRunId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var expected = SpacePublishWarningAcknowledgement.ComputeBoundHash(
            validationRunId,
            2,
            [first, second]);
        var reordered = SpacePublishWarningAcknowledgement.ComputeBoundHash(
            validationRunId,
            2,
            [second, first]);
        var anotherRun = SpacePublishWarningAcknowledgement.ComputeBoundHash(
            Guid.NewGuid(),
            2,
            [first, second]);

        Assert.NotNull(expected);
        Assert.Equal(64, expected.Length);
        Assert.Equal(expected, reordered);
        Assert.NotEqual(expected, anotherRun);
    }

    [Fact]
    public void No_warnings_require_no_acknowledgement()
    {
        var validationRunId = Guid.NewGuid();

        Assert.Null(SpacePublishWarningAcknowledgement.ComputeBoundHash(
            validationRunId,
            0,
            []));
        SpacePublishWarningAcknowledgement.EnsureConfirmed(
            validationRunId,
            0,
            [],
            suppliedHash: null);
    }

    [Fact]
    public void Missing_acknowledgement_fails_with_stable_422()
    {
        var exception = Assert.Throws<SpaceProblemException>(() =>
            SpacePublishWarningAcknowledgement.EnsureConfirmed(
                Guid.NewGuid(),
                1,
                [Guid.NewGuid()],
                suppliedHash: null));

        Assert.Equal(
            SpaceErrorCodes.PublishWarningAcknowledgementRequired,
            exception.Code);
        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("confirm-publish-warnings", exception.RecoveryAction);
    }

    [Fact]
    public void Changed_warning_set_fails_as_stale()
    {
        var validationRunId = Guid.NewGuid();
        var expectedIssueId = Guid.NewGuid();
        var supplied = SpacePublishWarningAcknowledgement.ComputeBoundHash(
            validationRunId,
            1,
            [expectedIssueId]);

        var exception = Assert.Throws<SpaceProblemException>(() =>
            SpacePublishWarningAcknowledgement.EnsureConfirmed(
                validationRunId,
                1,
                [Guid.NewGuid()],
                supplied));

        Assert.Equal(SpaceErrorCodes.ValidationStale, exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("refresh-publish-preview", exception.RecoveryAction);
    }

    [Fact]
    public void Summary_and_issue_count_must_match()
    {
        var exception = Assert.Throws<SpaceProblemException>(() =>
            SpacePublishWarningAcknowledgement.ComputeBoundHash(
                Guid.NewGuid(),
                2,
                [Guid.NewGuid()]));

        Assert.Equal(SpaceErrorCodes.ValidationStale, exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("run-validation", exception.RecoveryAction);
    }
}
