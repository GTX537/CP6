namespace CP6.Space.Contracts;

public static class SpacePersonnelEventContract
{
    public const string Version = "space-personnel-event-v1";
}

public sealed record IngestSpacePersonnelEventsRequest(
    string ContractVersion,
    string SourceId,
    string SourceKind,
    IReadOnlyList<SpacePersonnelEventInput> Events);

public sealed record SpacePersonnelEventInput(
    string SourceEventId,
    string PersonExternalId,
    Guid? UserId,
    string EventKind,
    string? WorkState,
    Guid? FloorLogicalId,
    Guid? LocationLogicalId,
    decimal? XMillimeters,
    decimal? YMillimeters,
    decimal? ZMillimeters,
    decimal? AccuracyMillimeters,
    long? SourceSequence,
    DateTimeOffset OccurredAtUtc);

public sealed record IngestSpacePersonnelEventsResponse(
    string ContractVersion,
    Guid SiteId,
    string SourceId,
    string SourceKind,
    DateTimeOffset ReceivedAtUtc,
    int ReceivedCount,
    int AcceptedCount,
    int DuplicateCount,
    int StaleCount,
    IReadOnlyList<SpacePersonnelEventReceipt> Receipts);

public sealed record SpacePersonnelEventReceipt(
    Guid EventId,
    string SourceEventId,
    string Outcome,
    bool ProjectionApplied);
