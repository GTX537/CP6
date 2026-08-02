namespace CP6.Space.Domain;

public enum SpacePersonnelSourceKind : short
{
    Real = 0,
    Simulated = 1,
}

public enum SpacePersonnelEventKind : short
{
    PositionObserved = 0,
    WorkStateChanged = 1,
}

public enum SpacePersonnelWorkState : short
{
    Unknown = 0,
    Offline = 1,
    Idle = 2,
    Busy = 3,
    Break = 4,
}

public sealed class SpacePersonnelEvent : SpaceTenantEntity
{
    private SpacePersonnelEvent()
    {
    }

    public Guid SiteId { get; private set; }
    public string SourceId { get; private set; } = string.Empty;
    public SpacePersonnelSourceKind SourceKind { get; private set; }
    public string SourceEventId { get; private set; } = string.Empty;
    public string PersonExternalId { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }
    public SpacePersonnelEventKind EventKind { get; private set; }
    public SpacePersonnelWorkState? WorkState { get; private set; }
    public Guid? FloorLogicalId { get; private set; }
    public Guid? LocationLogicalId { get; private set; }
    public decimal? XMillimeters { get; private set; }
    public decimal? YMillimeters { get; private set; }
    public decimal? ZMillimeters { get; private set; }
    public decimal? AccuracyMillimeters { get; private set; }
    public long? SourceSequence { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;

    public static SpacePersonnelEvent Create(
        Guid tenantId,
        Guid siteId,
        string sourceId,
        SpacePersonnelSourceKind sourceKind,
        string sourceEventId,
        string personExternalId,
        Guid? userId,
        SpacePersonnelEventKind eventKind,
        SpacePersonnelWorkState? workState,
        Guid? floorLogicalId,
        Guid? locationLogicalId,
        decimal? xMillimeters,
        decimal? yMillimeters,
        decimal? zMillimeters,
        decimal? accuracyMillimeters,
        long? sourceSequence,
        DateTime occurredAtUtc,
        DateTime receivedAtUtc,
        string payloadHash)
    {
        RequireIdentity(siteId, nameof(siteId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User identity cannot be empty.", nameof(userId));
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (!Enum.IsDefined(eventKind))
            throw new ArgumentOutOfRangeException(nameof(eventKind));
        if (workState.HasValue && !Enum.IsDefined(workState.Value))
            throw new ArgumentOutOfRangeException(nameof(workState));
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

        if (eventKind == SpacePersonnelEventKind.PositionObserved)
        {
            if (workState.HasValue)
                throw new ArgumentException("A position event cannot contain work state.");
            if (!locationLogicalId.HasValue &&
                (!floorLogicalId.HasValue || coordinateCount != 3))
            {
                throw new ArgumentException(
                    "A position event requires a location identity or floor plus XYZ coordinates.");
            }
        }
        else if (!workState.HasValue || floorLogicalId.HasValue ||
                 locationLogicalId.HasValue || coordinateCount != 0 ||
                 accuracyMillimeters.HasValue)
        {
            throw new ArgumentException(
                "A work-state event requires only a work state and cannot contain position fields.");
        }

        var value = new SpacePersonnelEvent
        {
            SiteId = siteId,
            SourceId = RequireText(sourceId, 100, nameof(sourceId)),
            SourceKind = sourceKind,
            SourceEventId = RequireText(sourceEventId, 200, nameof(sourceEventId)),
            PersonExternalId = RequireText(
                personExternalId,
                200,
                nameof(personExternalId)),
            UserId = userId,
            EventKind = eventKind,
            WorkState = workState,
            FloorLogicalId = floorLogicalId,
            LocationLogicalId = locationLogicalId,
            XMillimeters = xMillimeters,
            YMillimeters = yMillimeters,
            ZMillimeters = zMillimeters,
            AccuracyMillimeters = accuracyMillimeters,
            SourceSequence = sourceSequence,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = receivedAtUtc,
            PayloadHash = RequireHash(payloadHash, nameof(payloadHash)),
        };
        value.SetTenant(tenantId);
        return value;
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

public sealed class SpacePersonnelCurrentState : SpaceTenantEntity
{
    private SpacePersonnelCurrentState()
    {
    }

    public Guid SiteId { get; private set; }
    public string SourceId { get; private set; } = string.Empty;
    public SpacePersonnelSourceKind SourceKind { get; private set; }
    public string PersonExternalId { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }

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

    public SpacePersonnelWorkState WorkState { get; private set; }
    public DateTime? WorkStateOccurredAtUtc { get; private set; }
    public DateTime? WorkStateReceivedAtUtc { get; private set; }
    public long? WorkStateSourceSequence { get; private set; }
    public string? WorkStateSourceEventId { get; private set; }
    public Guid? WorkStateEventId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpacePersonnelCurrentState Create(SpacePersonnelEvent firstEvent)
    {
        ArgumentNullException.ThrowIfNull(firstEvent);
        var value = new SpacePersonnelCurrentState
        {
            SiteId = firstEvent.SiteId,
            SourceId = firstEvent.SourceId,
            SourceKind = firstEvent.SourceKind,
            PersonExternalId = firstEvent.PersonExternalId,
            WorkState = SpacePersonnelWorkState.Unknown,
        };
        value.SetTenant(firstEvent.TenantId);
        value.Apply(firstEvent);
        return value;
    }

    public bool Apply(SpacePersonnelEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.TenantId != TenantId || value.SiteId != SiteId ||
            value.SourceKind != SourceKind ||
            !string.Equals(value.SourceId, SourceId, StringComparison.Ordinal) ||
            !string.Equals(
                value.PersonExternalId,
                PersonExternalId,
                StringComparison.Ordinal))
        {
            throw new SpaceTenantScopeException(
                "A personnel event does not belong to this current-state identity.");
        }
        if (UserId.HasValue && value.UserId.HasValue && UserId != value.UserId)
        {
            throw new InvalidOperationException(
                "A personnel identity cannot be reassigned to another user.");
        }
        UserId ??= value.UserId;

        return value.EventKind switch
        {
            SpacePersonnelEventKind.PositionObserved => ApplyPosition(value),
            SpacePersonnelEventKind.WorkStateChanged => ApplyWorkState(value),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private bool ApplyPosition(SpacePersonnelEvent value)
    {
        if (!IsLater(
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

    private bool ApplyWorkState(SpacePersonnelEvent value)
    {
        if (!IsLater(
                value,
                WorkStateOccurredAtUtc,
                WorkStateSourceSequence,
                WorkStateSourceEventId))
        {
            return false;
        }

        WorkState = value.WorkState!.Value;
        WorkStateOccurredAtUtc = value.OccurredAtUtc;
        WorkStateReceivedAtUtc = value.ReceivedAtUtc;
        WorkStateSourceSequence = value.SourceSequence;
        WorkStateSourceEventId = value.SourceEventId;
        WorkStateEventId = value.Id;
        return true;
    }

    private static bool IsLater(
        SpacePersonnelEvent value,
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
