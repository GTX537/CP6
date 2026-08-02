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
    string? ContainerNumber,
    string? OwnerId);

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
    IReadOnlyList<string> ContainerNumbers,
    IReadOnlyList<string> OwnerIds);

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

public sealed record SpaceWmsRuntimeDispatchTaskItemDto(
    string TaskId,
    string TaskType,
    string Status,
    string? AssignedTo,
    int Priority,
    int ContractVersion,
    int ExecutionVersion,
    string RowVersion,
    string TargetLocationRole,
    string? WmsLocationCode,
    bool TargetLocationResolved,
    Guid? LocationLogicalId,
    Guid? WmsLogicalId,
    string? SpaceLocationCode,
    bool CodeMatches,
    Guid? FloorLogicalId,
    string? FloorCode,
    string? FloorName,
    int? FloorLevel,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    Guid? RackLogicalId,
    string? RackCode,
    double? AnchorXMillimeters,
    double? AnchorYMillimeters,
    double? AnchorZMillimeters,
    decimal Quantity,
    string? MaterialNumber);

public sealed record SpaceWmsRuntimeDispatchTaskResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    SpaceWmsRuntimeSourceDto Source,
    IReadOnlyList<SpaceWmsRuntimeDispatchTaskItemDto> Items);

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

public sealed record SpaceWmsRuntimeWarehouseModelKpiDto(
    int FloorCount,
    int AreaAvailableFloorCount,
    int AreaMissingFloorCount,
    decimal? TotalFloorAreaSquareMeters,
    int ZoneCount,
    int RackCount,
    decimal RackFootprintSquareMeters,
    decimal? RackFootprintRatePercent,
    int ActiveLocationCount);

public sealed record SpaceWmsRuntimeWarehouseInventoryKpiDto(
    SpaceWmsRuntimeSourceDto Source,
    int? InventoryLineCount,
    int? OccupiedLocationCount,
    int? UnoccupiedLocationCount,
    decimal? OccupiedLocationRatePercent,
    string OccupiedLocationRateMethod,
    decimal? CapacityUtilizationPercent,
    string CapacityUtilizationStatus,
    string CapacityUtilizationReason,
    int? DistinctOwnerCount,
    int? DistinctMaterialCount,
    int? DistinctLotCount,
    int? DistinctContainerCount);

public sealed record SpaceWmsRuntimeWarehouseTaskKpiDto(
    SpaceWmsRuntimeSourceDto Source,
    int? ActiveTaskCount,
    int? ActiveTaskStopCount);

public sealed record SpaceWmsRuntimeWarehouseAnomalyKpiDto(
    int ActiveDeviceAlarmCount,
    int CriticalDeviceAlarmCount,
    int? CodeMismatchLocationCount,
    int? OverAllocatedInventoryLineCount,
    int AreaMissingFloorCount,
    int? UnclassifiedAbcMaterialCount);

public sealed record SpaceWmsRuntimeWarehouseAbcMaterialDto(
    string MaterialNumber,
    int OutboundMovementCount,
    decimal OutboundQuantity,
    decimal? PreviousCumulativeSharePercent,
    decimal? CumulativeSharePercent,
    string Rank,
    int OccupiedLocationCount,
    int FloorCount);

public sealed record SpaceWmsRuntimeWarehouseAbcLocationMaterialDto(
    string MaterialNumber,
    string Rank);

public sealed record SpaceWmsRuntimeWarehouseAbcLocationDto(
    Guid LocationLogicalId,
    string SpaceLocationCode,
    Guid FloorLogicalId,
    string FloorCode,
    string Rank,
    IReadOnlyList<SpaceWmsRuntimeWarehouseAbcLocationMaterialDto> Materials);

public sealed record SpaceWmsRuntimeWarehouseAbcDto(
    SpaceWmsRuntimeSourceDto Source,
    int WindowDays,
    string WindowStartDate,
    string WindowEndDateExclusive,
    string TransactionTimeBasis,
    string RankingMethod,
    decimal AThresholdPercent,
    decimal BThresholdPercent,
    bool SpatialMappingAvailable,
    int? MaterialCount,
    int? ACount,
    int? BCount,
    int? CCount,
    int? UnclassifiedCount,
    IReadOnlyList<SpaceWmsRuntimeWarehouseAbcMaterialDto> Materials,
    IReadOnlyList<SpaceWmsRuntimeWarehouseAbcLocationDto> Locations);

public sealed record SpaceWmsRuntimeWarehouseFloorKpiDto(
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    decimal? AreaSquareMeters,
    int ActiveLocationCount,
    int? OccupiedLocationCount,
    decimal? OccupiedLocationRatePercent,
    int? ALocationCount,
    int? BLocationCount,
    int? CLocationCount,
    int? UnclassifiedLocationCount);

public sealed record SpaceWmsRuntimeWarehouseOverviewResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    DateTimeOffset CapturedAtUtc,
    bool IsRuntimeComplete,
    SpaceWmsRuntimeWarehouseModelKpiDto Model,
    SpaceWmsRuntimeWarehouseInventoryKpiDto Inventory,
    SpaceWmsRuntimeWarehouseTaskKpiDto Tasks,
    SpaceWmsRuntimeWarehouseAnomalyKpiDto Anomalies,
    SpaceWmsRuntimeWarehouseAbcDto Abc,
    IReadOnlyList<SpaceWmsRuntimeWarehouseFloorKpiDto> Floors);
