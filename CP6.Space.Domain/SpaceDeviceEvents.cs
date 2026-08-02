namespace CP6.Space.Domain;

public enum SpaceDeviceSourceKind : short
{
    Real = 0,
    Simulated = 1,
}

public enum SpaceDeviceKind : short
{
    Agv = 0,
    Conveyor = 1,
    StackerCrane = 2,
    Lift = 3,
    Sorter = 4,
    Workstation = 5,
    Sensor = 6,
    Other = 7,
}

public enum SpaceDeviceEventKind : short
{
    PositionObserved = 0,
    OperatingStateChanged = 1,
    AlarmRaised = 2,
    AlarmCleared = 3,
}

public enum SpaceDeviceOperatingState : short
{
    Unknown = 0,
    Offline = 1,
    Idle = 2,
    Running = 3,
    Paused = 4,
    Faulted = 5,
    Maintenance = 6,
}

public enum SpaceDeviceAlarmSeverity : short
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public sealed class SpaceDeviceMapping : SpaceTenantEntity
{
    private SpaceDeviceMapping()
    {
    }

    public Guid SiteId { get; private set; }
    public string SourceId { get; private set; } = string.Empty;
    public SpaceDeviceSourceKind SourceKind { get; private set; }
    public string DeviceExternalId { get; private set; } = string.Empty;
    public SpaceDeviceKind DeviceKind { get; private set; }
    public Guid ElementLogicalId { get; private set; }
    public string ElementType { get; private set; } = string.Empty;
    public Guid ValidatedModelVersionId { get; private set; }
    public Guid ValidatedFloorLogicalId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceDeviceMapping Create(
        Guid tenantId,
        Guid siteId,
        string sourceId,
        SpaceDeviceSourceKind sourceKind,
        string deviceExternalId,
        SpaceDeviceKind deviceKind,
        Guid elementLogicalId,
        string elementType,
        Guid validatedModelVersionId,
        Guid validatedFloorLogicalId)
    {
        RequireIdentity(siteId, nameof(siteId));
        RequireIdentity(elementLogicalId, nameof(elementLogicalId));
        RequireIdentity(validatedModelVersionId, nameof(validatedModelVersionId));
        RequireIdentity(validatedFloorLogicalId, nameof(validatedFloorLogicalId));
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (!Enum.IsDefined(deviceKind))
            throw new ArgumentOutOfRangeException(nameof(deviceKind));

        var value = new SpaceDeviceMapping
        {
            SiteId = siteId,
            SourceId = RequireText(sourceId, 100, nameof(sourceId)),
            SourceKind = sourceKind,
            DeviceExternalId = RequireText(
                deviceExternalId,
                200,
                nameof(deviceExternalId)),
        };
        value.SetTenant(tenantId);
        value.Remap(
            deviceKind,
            elementLogicalId,
            elementType,
            validatedModelVersionId,
            validatedFloorLogicalId);
        return value;
    }

    public void Remap(
        SpaceDeviceKind deviceKind,
        Guid elementLogicalId,
        string elementType,
        Guid validatedModelVersionId,
        Guid validatedFloorLogicalId)
    {
        if (!Enum.IsDefined(deviceKind))
            throw new ArgumentOutOfRangeException(nameof(deviceKind));
        RequireIdentity(elementLogicalId, nameof(elementLogicalId));
        RequireIdentity(validatedModelVersionId, nameof(validatedModelVersionId));
        RequireIdentity(validatedFloorLogicalId, nameof(validatedFloorLogicalId));

        DeviceKind = deviceKind;
        ElementLogicalId = elementLogicalId;
        ElementType = RequireText(elementType, 50, nameof(elementType));
        ValidatedModelVersionId = validatedModelVersionId;
        ValidatedFloorLogicalId = validatedFloorLogicalId;
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("An identity is required.", parameterName);
    }

    private static string RequireText(
        string value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new ArgumentException("A bounded value is required.", parameterName);
        return value;
    }
}

public sealed class SpaceDeviceEvent : SpaceTenantEntity
{
    private SpaceDeviceEvent()
    {
    }

    public Guid SiteId { get; private set; }
    public string SourceId { get; private set; } = string.Empty;
    public SpaceDeviceSourceKind SourceKind { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;
    public Guid DeviceMappingId { get; private set; }
    public string DeviceExternalId { get; private set; } = string.Empty;
    public SpaceDeviceKind DeviceKind { get; private set; }
    public Guid ElementLogicalId { get; private set; }
    public SpaceDeviceEventKind EventKind { get; private set; }
    public SpaceDeviceOperatingState? OperatingState { get; private set; }
    public Guid? FloorLogicalId { get; private set; }
    public Guid? LocationLogicalId { get; private set; }
    public decimal? XMillimeters { get; private set; }
    public decimal? YMillimeters { get; private set; }
    public decimal? ZMillimeters { get; private set; }
    public decimal? AccuracyMillimeters { get; private set; }
    public string? AlarmExternalId { get; private set; }
    public string? AlarmCode { get; private set; }
    public SpaceDeviceAlarmSeverity? AlarmSeverity { get; private set; }
    public string? AlarmMessage { get; private set; }
    public long? SourceSequence { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;

    public static SpaceDeviceEvent Create(
        Guid tenantId,
        Guid siteId,
        string sourceId,
        SpaceDeviceSourceKind sourceKind,
        string sourceEventId,
        SpaceDeviceMapping mapping,
        SpaceDeviceEventKind eventKind,
        SpaceDeviceOperatingState? operatingState,
        Guid? floorLogicalId,
        Guid? locationLogicalId,
        decimal? xMillimeters,
        decimal? yMillimeters,
        decimal? zMillimeters,
        decimal? accuracyMillimeters,
        string? alarmExternalId,
        string? alarmCode,
        SpaceDeviceAlarmSeverity? alarmSeverity,
        string? alarmMessage,
        long? sourceSequence,
        DateTime occurredAtUtc,
        DateTime receivedAtUtc,
        string payloadHash)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        RequireIdentity(siteId, nameof(siteId));
        if (mapping.TenantId != tenantId || mapping.SiteId != siteId ||
            mapping.SourceKind != sourceKind ||
            !string.Equals(mapping.SourceId, sourceId, StringComparison.Ordinal))
        {
            throw new SpaceTenantScopeException(
                "The device mapping does not belong to the event source.");
        }
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (!Enum.IsDefined(eventKind))
            throw new ArgumentOutOfRangeException(nameof(eventKind));
        if (operatingState.HasValue && !Enum.IsDefined(operatingState.Value))
            throw new ArgumentOutOfRangeException(nameof(operatingState));
        if (alarmSeverity.HasValue && !Enum.IsDefined(alarmSeverity.Value))
            throw new ArgumentOutOfRangeException(nameof(alarmSeverity));
        if (floorLogicalId == Guid.Empty || locationLogicalId == Guid.Empty)
            throw new ArgumentException("Logical identities cannot be empty.");
        if (sourceSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSequence));
        RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        RequireUtc(receivedAtUtc, nameof(receivedAtUtc));

        var coordinateCount = new[]
        {
            xMillimeters.HasValue,
            yMillimeters.HasValue,
            zMillimeters.HasValue,
        }.Count(value => value);
        if (coordinateCount is not (0 or 3))
            throw new ArgumentException("Coordinates must be supplied as an XYZ triple.");
        if (accuracyMillimeters < 0 ||
            accuracyMillimeters.HasValue && coordinateCount == 0)
        {
            throw new ArgumentException("Accuracy requires a non-negative XYZ position.");
        }

        ValidateShape(
            eventKind,
            operatingState,
            floorLogicalId,
            locationLogicalId,
            coordinateCount,
            accuracyMillimeters,
            alarmExternalId,
            alarmCode,
            alarmSeverity,
            alarmMessage);

        var value = new SpaceDeviceEvent
        {
            SiteId = siteId,
            SourceId = RequireText(sourceId, 100, nameof(sourceId)),
            SourceKind = sourceKind,
            SourceEventId = RequireText(sourceEventId, 200, nameof(sourceEventId)),
            DeviceMappingId = mapping.Id,
            DeviceExternalId = mapping.DeviceExternalId,
            DeviceKind = mapping.DeviceKind,
            ElementLogicalId = mapping.ElementLogicalId,
            EventKind = eventKind,
            OperatingState = operatingState,
            FloorLogicalId = floorLogicalId,
            LocationLogicalId = locationLogicalId,
            XMillimeters = xMillimeters,
            YMillimeters = yMillimeters,
            ZMillimeters = zMillimeters,
            AccuracyMillimeters = accuracyMillimeters,
            AlarmExternalId = OptionalText(
                alarmExternalId,
                200,
                nameof(alarmExternalId)),
            AlarmCode = OptionalText(alarmCode, 100, nameof(alarmCode)),
            AlarmSeverity = alarmSeverity,
            AlarmMessage = OptionalText(
                alarmMessage,
                500,
                nameof(alarmMessage)),
            SourceSequence = sourceSequence,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = receivedAtUtc,
            PayloadHash = RequireHash(payloadHash, nameof(payloadHash)),
        };
        value.SetTenant(tenantId);
        return value;
    }

    private static void ValidateShape(
        SpaceDeviceEventKind eventKind,
        SpaceDeviceOperatingState? operatingState,
        Guid? floorLogicalId,
        Guid? locationLogicalId,
        int coordinateCount,
        decimal? accuracyMillimeters,
        string? alarmExternalId,
        string? alarmCode,
        SpaceDeviceAlarmSeverity? alarmSeverity,
        string? alarmMessage)
    {
        var hasAlarmId = !string.IsNullOrWhiteSpace(alarmExternalId);
        var hasAlarmCode = !string.IsNullOrWhiteSpace(alarmCode);
        var hasAlarmMessage = !string.IsNullOrWhiteSpace(alarmMessage);
        var hasPosition = floorLogicalId.HasValue ||
                          locationLogicalId.HasValue ||
                          coordinateCount != 0 ||
                          accuracyMillimeters.HasValue;

        switch (eventKind)
        {
            case SpaceDeviceEventKind.PositionObserved:
                if (operatingState.HasValue || hasAlarmId || hasAlarmCode ||
                    alarmSeverity.HasValue || hasAlarmMessage ||
                    !locationLogicalId.HasValue &&
                    (!floorLogicalId.HasValue || coordinateCount != 3))
                {
                    throw new ArgumentException(
                        "A position event requires a location identity or floor plus XYZ and cannot contain state or alarm fields.");
                }
                break;
            case SpaceDeviceEventKind.OperatingStateChanged:
                if (!operatingState.HasValue || hasPosition || hasAlarmId ||
                    hasAlarmCode || alarmSeverity.HasValue || hasAlarmMessage)
                {
                    throw new ArgumentException(
                        "A state event requires only an operating state.");
                }
                break;
            case SpaceDeviceEventKind.AlarmRaised:
                if (operatingState.HasValue || hasPosition || !hasAlarmId ||
                    !hasAlarmCode || !alarmSeverity.HasValue)
                {
                    throw new ArgumentException(
                        "An alarm-raised event requires alarm identity, code, and severity only.");
                }
                break;
            case SpaceDeviceEventKind.AlarmCleared:
                if (operatingState.HasValue || hasPosition || !hasAlarmId ||
                    hasAlarmCode || alarmSeverity.HasValue || hasAlarmMessage)
                {
                    throw new ArgumentException(
                        "An alarm-cleared event requires only the alarm identity.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(eventKind));
        }
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("An identity is required.", parameterName);
    }

    private static string RequireText(
        string value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new ArgumentException("A bounded value is required.", parameterName);
        return value;
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            throw new ArgumentException("A bounded value is required.", parameterName);
        return normalized;
    }

    private static string RequireHash(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 hash is required.", parameterName);
        return value.ToLowerInvariant();
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC.", parameterName);
    }
}

public sealed class SpaceDeviceCurrentState : SpaceTenantEntity
{
    private SpaceDeviceCurrentState()
    {
    }

    public Guid SiteId { get; private set; }
    public string SourceId { get; private set; } = string.Empty;
    public SpaceDeviceSourceKind SourceKind { get; private set; }
    public string DeviceExternalId { get; private set; } = string.Empty;
    public Guid DeviceMappingId { get; private set; }

    public Guid? FloorLogicalId { get; private set; }
    public Guid? LocationLogicalId { get; private set; }
    public decimal? XMillimeters { get; private set; }
    public decimal? YMillimeters { get; private set; }
    public decimal? ZMillimeters { get; private set; }
    public decimal? AccuracyMillimeters { get; private set; }
    public DateTime? PositionOccurredAtUtc { get; private set; }
    public DateTime? PositionReceivedAtUtc { get; private set; }
    public long? PositionSourceSequence { get; private set; }
    public string? PositionSourceEventId { get; private set; }
    public Guid? PositionEventId { get; private set; }

    public SpaceDeviceOperatingState OperatingState { get; private set; }
    public DateTime? OperatingStateOccurredAtUtc { get; private set; }
    public DateTime? OperatingStateReceivedAtUtc { get; private set; }
    public long? OperatingStateSourceSequence { get; private set; }
    public string? OperatingStateSourceEventId { get; private set; }
    public Guid? OperatingStateEventId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceDeviceCurrentState Create(SpaceDeviceEvent firstEvent)
    {
        ArgumentNullException.ThrowIfNull(firstEvent);
        if (firstEvent.EventKind is not (
                SpaceDeviceEventKind.PositionObserved or
                SpaceDeviceEventKind.OperatingStateChanged))
        {
            throw new ArgumentException(
                "A current-state projection requires a position or operating-state event.",
                nameof(firstEvent));
        }

        var value = new SpaceDeviceCurrentState
        {
            SiteId = firstEvent.SiteId,
            SourceId = firstEvent.SourceId,
            SourceKind = firstEvent.SourceKind,
            DeviceExternalId = firstEvent.DeviceExternalId,
            DeviceMappingId = firstEvent.DeviceMappingId,
            OperatingState = SpaceDeviceOperatingState.Unknown,
        };
        value.SetTenant(firstEvent.TenantId);
        value.Apply(firstEvent);
        return value;
    }

    public bool Apply(SpaceDeviceEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureIdentity(value);
        return value.EventKind switch
        {
            SpaceDeviceEventKind.PositionObserved => ApplyPosition(value),
            SpaceDeviceEventKind.OperatingStateChanged =>
                ApplyOperatingState(value),
            _ => throw new ArgumentException(
                "Alarm events do not update the device current-state projection.",
                nameof(value)),
        };
    }

    private bool ApplyPosition(SpaceDeviceEvent value)
    {
        if (!SpaceDeviceProjectionOrder.IsLater(
                value,
                PositionOccurredAtUtc,
                PositionSourceSequence,
                PositionSourceEventId))
        {
            return false;
        }

        FloorLogicalId = value.FloorLogicalId;
        LocationLogicalId = value.LocationLogicalId;
        XMillimeters = value.XMillimeters;
        YMillimeters = value.YMillimeters;
        ZMillimeters = value.ZMillimeters;
        AccuracyMillimeters = value.AccuracyMillimeters;
        PositionOccurredAtUtc = value.OccurredAtUtc;
        PositionReceivedAtUtc = value.ReceivedAtUtc;
        PositionSourceSequence = value.SourceSequence;
        PositionSourceEventId = value.SourceEventId;
        PositionEventId = value.Id;
        return true;
    }

    private bool ApplyOperatingState(SpaceDeviceEvent value)
    {
        if (!SpaceDeviceProjectionOrder.IsLater(
                value,
                OperatingStateOccurredAtUtc,
                OperatingStateSourceSequence,
                OperatingStateSourceEventId))
        {
            return false;
        }

        OperatingState = value.OperatingState!.Value;
        OperatingStateOccurredAtUtc = value.OccurredAtUtc;
        OperatingStateReceivedAtUtc = value.ReceivedAtUtc;
        OperatingStateSourceSequence = value.SourceSequence;
        OperatingStateSourceEventId = value.SourceEventId;
        OperatingStateEventId = value.Id;
        return true;
    }

    private void EnsureIdentity(SpaceDeviceEvent value)
    {
        if (value.TenantId != TenantId || value.SiteId != SiteId ||
            value.SourceKind != SourceKind ||
            value.DeviceMappingId != DeviceMappingId ||
            !string.Equals(value.SourceId, SourceId, StringComparison.Ordinal) ||
            !string.Equals(
                value.DeviceExternalId,
                DeviceExternalId,
                StringComparison.Ordinal))
        {
            throw new SpaceTenantScopeException(
                "A device event does not belong to this current-state identity.");
        }
    }
}

public sealed class SpaceDeviceAlarmState : SpaceTenantEntity
{
    private SpaceDeviceAlarmState()
    {
    }

    public Guid SiteId { get; private set; }
    public string SourceId { get; private set; } = string.Empty;
    public SpaceDeviceSourceKind SourceKind { get; private set; }
    public string DeviceExternalId { get; private set; } = string.Empty;
    public Guid DeviceMappingId { get; private set; }
    public string AlarmExternalId { get; private set; } = string.Empty;
    public string? AlarmCode { get; private set; }
    public SpaceDeviceAlarmSeverity? AlarmSeverity { get; private set; }
    public string? AlarmMessage { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public long? SourceSequence { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;
    public Guid EventId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceDeviceAlarmState Create(SpaceDeviceEvent firstEvent)
    {
        ArgumentNullException.ThrowIfNull(firstEvent);
        if (firstEvent.EventKind is not (
                SpaceDeviceEventKind.AlarmRaised or
                SpaceDeviceEventKind.AlarmCleared))
        {
            throw new ArgumentException(
                "An alarm-state projection requires an alarm event.",
                nameof(firstEvent));
        }

        var value = new SpaceDeviceAlarmState
        {
            SiteId = firstEvent.SiteId,
            SourceId = firstEvent.SourceId,
            SourceKind = firstEvent.SourceKind,
            DeviceExternalId = firstEvent.DeviceExternalId,
            DeviceMappingId = firstEvent.DeviceMappingId,
            AlarmExternalId = firstEvent.AlarmExternalId!,
        };
        value.SetTenant(firstEvent.TenantId);
        value.Apply(firstEvent);
        return value;
    }

    public bool Apply(SpaceDeviceEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureIdentity(value);
        if (value.EventKind is not (
                SpaceDeviceEventKind.AlarmRaised or
                SpaceDeviceEventKind.AlarmCleared))
        {
            throw new ArgumentException(
                "Only alarm events update an alarm-state projection.",
                nameof(value));
        }
        if (SourceEventId.Length > 0 &&
            !SpaceDeviceProjectionOrder.IsLater(
                value,
                OccurredAtUtc,
                SourceSequence,
                SourceEventId))
        {
            return false;
        }

        if (value.EventKind == SpaceDeviceEventKind.AlarmRaised)
        {
            AlarmCode = value.AlarmCode;
            AlarmSeverity = value.AlarmSeverity;
            AlarmMessage = value.AlarmMessage;
            IsActive = true;
        }
        else
        {
            IsActive = false;
        }
        OccurredAtUtc = value.OccurredAtUtc;
        ReceivedAtUtc = value.ReceivedAtUtc;
        SourceSequence = value.SourceSequence;
        SourceEventId = value.SourceEventId;
        EventId = value.Id;
        return true;
    }

    private void EnsureIdentity(SpaceDeviceEvent value)
    {
        if (value.TenantId != TenantId || value.SiteId != SiteId ||
            value.SourceKind != SourceKind ||
            value.DeviceMappingId != DeviceMappingId ||
            !string.Equals(value.SourceId, SourceId, StringComparison.Ordinal) ||
            !string.Equals(
                value.DeviceExternalId,
                DeviceExternalId,
                StringComparison.Ordinal) ||
            !string.Equals(
                value.AlarmExternalId,
                AlarmExternalId,
                StringComparison.Ordinal))
        {
            throw new SpaceTenantScopeException(
                "A device alarm event does not belong to this alarm identity.");
        }
    }
}

internal static class SpaceDeviceProjectionOrder
{
    public static bool IsLater(
        SpaceDeviceEvent value,
        DateTime? occurredAtUtc,
        long? sourceSequence,
        string? sourceEventId)
    {
        if (!occurredAtUtc.HasValue)
            return true;
        var comparison = value.OccurredAtUtc.CompareTo(occurredAtUtc.Value);
        if (comparison != 0)
            return comparison > 0;

        comparison = CompareSequence(value.SourceSequence, sourceSequence);
        return comparison != 0
            ? comparison > 0
            : string.CompareOrdinal(value.SourceEventId, sourceEventId) > 0;
    }

    private static int CompareSequence(long? left, long? right)
    {
        if (left.HasValue && right.HasValue)
            return left.Value.CompareTo(right.Value);
        if (left.HasValue)
            return 1;
        if (right.HasValue)
            return -1;
        return 0;
    }
}
