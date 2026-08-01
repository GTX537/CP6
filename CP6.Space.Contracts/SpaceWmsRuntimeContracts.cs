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

public sealed record SpaceWmsRuntimeInventoryLocateCriteriaDto(
    string? MaterialNumber,
    string? LotNumber,
    string? ContainerNumber);

public sealed record SpaceWmsRuntimeInventoryLocateHitDto(
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
    IReadOnlyList<string> MaterialNumbers,
    IReadOnlyList<string> LotNumbers,
    IReadOnlyList<string> ContainerNumbers);

public sealed record SpaceWmsRuntimeInventoryLocateResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    SpaceWmsRuntimeSourceDto Source,
    SpaceWmsRuntimeInventoryLocateCriteriaDto Criteria,
    int LocationCount,
    int FloorCount,
    IReadOnlyList<SpaceWmsRuntimeInventoryLocateHitDto> Items);

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

public sealed record SpaceWmsRuntimeTaskFloorDto(
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    int ElevationMillimeters,
    int HeightMillimeters,
    int StopCount,
    decimal TotalQuantity);

public sealed record SpaceWmsRuntimeTaskWorkloadDto(
    Guid FloorLogicalId,
    string FloorCode,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    int StopCount,
    decimal TotalQuantity);

public sealed record SpaceWmsRuntimeTaskAisleDto(
    Guid FloorLogicalId,
    Guid ZoneLogicalId,
    Guid AisleLogicalId,
    string AisleCode,
    string CenterlineJson);

public sealed record SpaceWmsRuntimeTaskPathResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    SpaceWmsRuntimeSourceDto Source,
    string TaskId,
    int StopCount,
    int LocatedStopCount,
    int FloorCount,
    int ZoneCount,
    int FloorTransitionCount,
    int ZoneTransitionCount,
    decimal TotalQuantity,
    bool CrossFloor,
    bool CrossZone,
    IReadOnlyList<SpaceWmsRuntimeTaskItemDto> ActualStops,
    IReadOnlyList<SpaceWmsRuntimeTaskFloorDto> Floors,
    IReadOnlyList<SpaceWmsRuntimeTaskWorkloadDto> Workloads,
    IReadOnlyList<SpaceWmsRuntimeTaskAisleDto> Aisles);
