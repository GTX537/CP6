namespace CP6.Space.Contracts;

public sealed record SpaceWmsRuntimeSourceDto(
    string Kind,
    string AdapterId,
    string DataSourceId,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    long DelayMilliseconds,
    long ClockSkewMilliseconds,
    bool IsSimulated,
    bool IsAvailable);

public sealed record SpaceWmsRuntimeInventoryItemDto(
    Guid LocationLogicalId,
    Guid WmsLogicalId,
    string SpaceLocationCode,
    string WmsLocationCode,
    bool CodeMatches,
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    decimal PhysicalQuantity,
    decimal AllocatedQuantity,
    string? MaterialNumber,
    string? LotNumber,
    string? ContainerNumber,
    string? OwnerId = null);

public sealed record SpaceWmsRuntimeInventoryResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    SpaceWmsRuntimeSourceDto Source,
    IReadOnlyList<SpaceWmsRuntimeInventoryItemDto> Items);

public sealed record SpaceWmsRuntimeTaskItemDto(
    string TaskId,
    string TaskType,
    string Status,
    int SequenceNo,
    Guid LocationLogicalId,
    Guid WmsLogicalId,
    string SpaceLocationCode,
    string WmsLocationCode,
    bool CodeMatches,
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    Guid? RackLogicalId,
    string? RackCode,
    double? AnchorXMillimeters,
    double? AnchorYMillimeters,
    double? AnchorZMillimeters,
    decimal? Quantity,
    string? MaterialNumber);

public sealed record SpaceWmsRuntimeTaskResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    SpaceWmsRuntimeSourceDto Source,
    IReadOnlyList<SpaceWmsRuntimeTaskItemDto> Items);
