namespace CP6.Space.Contracts;

public static class SpaceDeviceEventContract
{
    public const string Version = "space-device-event-v1";
}

public sealed record CreateSpaceDeviceMappingRequest(
    string SourceId,
    string SourceKind,
    string DeviceExternalId,
    string DeviceKind,
    Guid ElementLogicalId);

public sealed record UpdateSpaceDeviceMappingRequest(
    string DeviceKind,
    Guid ElementLogicalId,
    string ExpectedRowVersion);

public sealed record SpaceDeviceMappingDto(
    Guid Id,
    Guid SiteId,
    string SourceId,
    string SourceKind,
    string DeviceExternalId,
    string DeviceKind,
    Guid ElementLogicalId,
    string ElementType,
    Guid ValidatedModelVersionId,
    Guid ValidatedFloorLogicalId,
    string RowVersion);

public sealed record SpaceDeviceMappingPageDto(
    IReadOnlyList<SpaceDeviceMappingDto> Items,
    string? NextCursor);

public sealed record IngestSpaceDeviceEventsRequest(
    string ContractVersion,
    string SourceId,
    string SourceKind,
    IReadOnlyList<SpaceDeviceEventInput> Events);

public sealed record SpaceDeviceEventInput(
    string SourceEventId,
    string DeviceExternalId,
    string EventKind,
    string? OperatingState,
    Guid? FloorLogicalId,
    Guid? LocationLogicalId,
    decimal? XMillimeters,
    decimal? YMillimeters,
    decimal? ZMillimeters,
    decimal? AccuracyMillimeters,
    string? AlarmExternalId,
    string? AlarmCode,
    string? AlarmSeverity,
    string? AlarmMessage,
    long? SourceSequence,
    DateTimeOffset OccurredAtUtc);

public sealed record IngestSpaceDeviceEventsResponse(
    string ContractVersion,
    Guid SiteId,
    string SourceId,
    string SourceKind,
    DateTimeOffset ReceivedAtUtc,
    int ReceivedCount,
    int AcceptedCount,
    int DuplicateCount,
    IReadOnlyList<SpaceDeviceEventReceipt> Receipts);

public sealed record SpaceDeviceEventReceipt(
    Guid EventId,
    string SourceEventId,
    string DeviceExternalId,
    string Outcome);
