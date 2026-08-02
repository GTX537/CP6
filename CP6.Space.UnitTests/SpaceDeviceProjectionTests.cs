using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceDeviceProjectionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateTime ReceivedAtUtc =
        new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Current_state_uses_independent_monotonic_position_and_state_cursors()
    {
        var mapping = Mapping();
        var state = SpaceDeviceCurrentState.Create(Event(
            mapping,
            "STATE-NEW",
            SpaceDeviceEventKind.OperatingStateChanged,
            ReceivedAtUtc.AddMinutes(-1),
            20,
            operatingState: SpaceDeviceOperatingState.Running));

        Assert.False(state.Apply(Event(
            mapping,
            "STATE-OLD",
            SpaceDeviceEventKind.OperatingStateChanged,
            ReceivedAtUtc.AddMinutes(-3),
            10,
            operatingState: SpaceDeviceOperatingState.Offline)));
        Assert.True(state.Apply(Event(
            mapping,
            "POSITION-01",
            SpaceDeviceEventKind.PositionObserved,
            ReceivedAtUtc.AddMinutes(-2),
            15,
            floorLogicalId: mapping.ValidatedFloorLogicalId,
            x: 10m,
            y: 20m,
            z: 0m)));

        Assert.Equal(SpaceDeviceOperatingState.Running, state.OperatingState);
        Assert.Equal("STATE-NEW", state.OperatingStateSourceEventId);
        Assert.Equal(10m, state.XMillimeters);
        Assert.Equal("POSITION-01", state.PositionSourceEventId);
    }

    [Fact]
    public void Alarm_clear_requires_newer_evidence_and_preserves_last_raise_detail()
    {
        var mapping = Mapping();
        var alarm = SpaceDeviceAlarmState.Create(Event(
            mapping,
            "RAISE-NEW",
            SpaceDeviceEventKind.AlarmRaised,
            ReceivedAtUtc.AddMinutes(-1),
            20,
            alarmExternalId: "ALARM-01",
            alarmCode: "MOTOR-OVERHEAT",
            alarmSeverity: SpaceDeviceAlarmSeverity.Critical,
            alarmMessage: "Motor is hot."));

        Assert.False(alarm.Apply(Event(
            mapping,
            "CLEAR-OLD",
            SpaceDeviceEventKind.AlarmCleared,
            ReceivedAtUtc.AddMinutes(-3),
            10,
            alarmExternalId: "ALARM-01")));
        Assert.True(alarm.IsActive);
        Assert.True(alarm.Apply(Event(
            mapping,
            "CLEAR-NEW",
            SpaceDeviceEventKind.AlarmCleared,
            ReceivedAtUtc.AddSeconds(-30),
            30,
            alarmExternalId: "ALARM-01")));

        Assert.False(alarm.IsActive);
        Assert.Equal("MOTOR-OVERHEAT", alarm.AlarmCode);
        Assert.Equal(SpaceDeviceAlarmSeverity.Critical, alarm.AlarmSeverity);
        Assert.Equal("CLEAR-NEW", alarm.SourceEventId);
    }

    private static SpaceDeviceMapping Mapping() =>
        SpaceDeviceMapping.Create(
            TenantId,
            SiteId,
            "WCS-01",
            SpaceDeviceSourceKind.Real,
            "AGV-01",
            SpaceDeviceKind.Agv,
            Guid.NewGuid(),
            SpaceElementTypes.Device,
            Guid.NewGuid(),
            Guid.NewGuid());

    private static SpaceDeviceEvent Event(
        SpaceDeviceMapping mapping,
        string sourceEventId,
        SpaceDeviceEventKind kind,
        DateTime occurredAtUtc,
        long sequence,
        SpaceDeviceOperatingState? operatingState = null,
        Guid? floorLogicalId = null,
        decimal? x = null,
        decimal? y = null,
        decimal? z = null,
        string? alarmExternalId = null,
        string? alarmCode = null,
        SpaceDeviceAlarmSeverity? alarmSeverity = null,
        string? alarmMessage = null) =>
        SpaceDeviceEvent.Create(
            TenantId,
            SiteId,
            "WCS-01",
            SpaceDeviceSourceKind.Real,
            sourceEventId,
            mapping,
            kind,
            operatingState,
            floorLogicalId,
            null,
            x,
            y,
            z,
            x.HasValue ? 25m : null,
            alarmExternalId,
            alarmCode,
            alarmSeverity,
            alarmMessage,
            sequence,
            occurredAtUtc,
            ReceivedAtUtc,
            new string('a', 64));
}
