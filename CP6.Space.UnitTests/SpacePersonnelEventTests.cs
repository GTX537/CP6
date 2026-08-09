using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpacePersonnelEventTests
{
    private static readonly DateTime ReceivedAt =
        new(2026, 8, 2, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Position_and_work_state_have_independent_time_cursors()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var position = NewEvent(
            tenantId,
            siteId,
            "POSITION-2",
            SpacePersonnelEventKind.PositionObserved,
            ReceivedAt.AddMinutes(-1),
            sequence: 2);
        var state = SpacePersonnelCurrentState.Create(position);
        var olderWorkState = NewEvent(
            tenantId,
            siteId,
            "WORK-1",
            SpacePersonnelEventKind.WorkStateChanged,
            ReceivedAt.AddMinutes(-10),
            workState: SpacePersonnelWorkState.Busy,
            sequence: 1);

        Assert.True(state.Apply(olderWorkState));
        Assert.Equal(SpacePersonnelWorkState.Busy, state.WorkState);
        Assert.Equal(position.Id, state.PositionEventId);
        Assert.Equal(olderWorkState.Id, state.WorkStateEventId);
    }

    [Fact]
    public void Older_event_is_retained_but_does_not_regress_projection()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var latest = NewEvent(
            tenantId,
            siteId,
            "POSITION-2",
            SpacePersonnelEventKind.PositionObserved,
            ReceivedAt.AddMinutes(-1),
            sequence: 2);
        var state = SpacePersonnelCurrentState.Create(latest);
        var stale = NewEvent(
            tenantId,
            siteId,
            "POSITION-1",
            SpacePersonnelEventKind.PositionObserved,
            ReceivedAt.AddMinutes(-2),
            sequence: 1);

        Assert.False(state.Apply(stale));
        Assert.Equal(latest.Id, state.PositionEventId);
    }

    [Fact]
    public void Bound_user_identity_cannot_be_reassigned()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var state = SpacePersonnelCurrentState.Create(NewEvent(
            tenantId,
            siteId,
            "WORK-1",
            SpacePersonnelEventKind.WorkStateChanged,
            ReceivedAt.AddMinutes(-2),
            Guid.NewGuid(),
            SpacePersonnelWorkState.Idle));

        var conflicting = NewEvent(
            tenantId,
            siteId,
            "WORK-2",
            SpacePersonnelEventKind.WorkStateChanged,
            ReceivedAt.AddMinutes(-1),
            Guid.NewGuid(),
            SpacePersonnelWorkState.Busy);

        Assert.Throws<InvalidOperationException>(() => state.Apply(conflicting));
    }

    private static SpacePersonnelEvent NewEvent(
        Guid tenantId,
        Guid siteId,
        string sourceEventId,
        SpacePersonnelEventKind eventKind,
        DateTime occurredAtUtc,
        Guid? userId = null,
        SpacePersonnelWorkState? workState = null,
        long? sequence = null) =>
        SpacePersonnelEvent.Create(
            tenantId,
            siteId,
            "PDA-01",
            SpacePersonnelSourceKind.Real,
            sourceEventId,
            "PERSON-01",
            userId,
            eventKind,
            workState,
            eventKind == SpacePersonnelEventKind.PositionObserved
                ? Guid.NewGuid()
                : null,
            null,
            eventKind == SpacePersonnelEventKind.PositionObserved ? 10m : null,
            eventKind == SpacePersonnelEventKind.PositionObserved ? 20m : null,
            eventKind == SpacePersonnelEventKind.PositionObserved ? 0m : null,
            eventKind == SpacePersonnelEventKind.PositionObserved ? 50m : null,
            sequence,
            occurredAtUtc,
            ReceivedAt,
            new string('a', 64));
}
