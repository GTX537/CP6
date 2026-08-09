namespace CP6.Space.Contracts;

public sealed record SpaceWmsAdoptionDto(
    Guid Id,
    Guid SiteId,
    string AdapterId,
    string DataSource,
    string DataSourceKind,
    Guid WmsLogicalId,
    string? ExternalLocationId,
    string WmsLocationCode,
    bool WmsIsActive,
    string ExternalVersion,
    string WmsStateHash,
    DateTime LastObservedAtUtc,
    string Status,
    Guid? ModelVersionId,
    Guid? LocationLogicalId,
    string? SpaceLocationCode,
    bool HasGeometry,
    string? DifferenceCode,
    DateTime? BoundAtUtc,
    string RowVersion);

public sealed record RefreshSpaceWmsAdoptionResponse(
    Guid SiteId,
    string AdapterId,
    string DataSource,
    string DataSourceKind,
    DateTime ObservedAtUtc,
    int DiscoveredCount,
    int UpdatedCount,
    int MissingCount,
    int UnboundCount,
    int BoundCount,
    int DifferenceCount);

public sealed record BindSpaceWmsAdoptionRequest(
    Guid LocationLogicalId,
    string ExpectedRowVersion);

public sealed record BatchBindSpaceWmsAdoptionItem(
    Guid AdoptionId,
    Guid LocationLogicalId,
    string ExpectedRowVersion);

public sealed record BatchBindSpaceWmsAdoptionRequest(
    IReadOnlyList<BatchBindSpaceWmsAdoptionItem> Items);

public sealed record PlaceSpaceWmsAdoptionRequest(
    Guid FloorLogicalId,
    Guid RackLogicalId,
    int Column,
    int Level,
    int Depth,
    string ExpectedRowVersion);

public sealed record SpaceWmsAdoptionCommandResponse(
    IReadOnlyList<SpaceWmsAdoptionDto> Items,
    long ContentRevision,
    int OpenWarningCount,
    int OpenBlockingCount);
