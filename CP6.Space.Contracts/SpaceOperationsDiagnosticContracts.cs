namespace CP6.Space.Contracts;

public sealed record SpaceOperationsDiagnosticThresholdsDto(
    int MaximumObservationGapSeconds,
    int MinimumBacktrackSegmentMillimeters,
    decimal BacktrackAngleDegrees,
    int DwellThresholdSeconds,
    int CongestionMinimumConcurrentPeople,
    decimal OccupancyWatchPercent,
    decimal OccupancyCriticalPercent);

public sealed record SpaceOperationsPersonnelSourceItemDto(
    string SourceId,
    string SourceKind,
    int EventCount,
    int PersonCount,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    DateTimeOffset LastReceivedAtUtc);

public sealed record SpaceOperationsPersonnelSourceDto(
    int EvidenceEventCount,
    int EligibleRealEventCount,
    int ExcludedSimulatedEventCount,
    int ExcludedOutsidePublishedModelEventCount,
    int PersonCount,
    int SourceCount,
    DateTimeOffset? FirstObservedAtUtc,
    DateTimeOffset? LastObservedAtUtc,
    DateTimeOffset? LastReceivedAtUtc,
    IReadOnlyList<SpaceOperationsPersonnelSourceItemDto> Sources);

public sealed record SpaceOperationsBacktrackFindingDto(
    Guid FloorLogicalId,
    string? FloorCode,
    Guid? LocationLogicalId,
    string? SpaceLocationCode,
    decimal XMillimeters,
    decimal YMillimeters,
    DateTimeOffset OccurredAtUtc,
    decimal TurnAngleDegrees,
    decimal ReturnSegmentMeters);

public sealed record SpaceOperationsPathDiagnosisDto(
    int PersonCount,
    int ObservedTransitionCount,
    int KnownDistanceSegmentCount,
    int UnknownDistanceSegmentCount,
    decimal ObservedDistanceMeters,
    int BacktrackCount,
    decimal BacktrackDistanceMeters,
    bool BacktracksTruncated,
    IReadOnlyList<SpaceOperationsBacktrackFindingDto> Backtracks);

public sealed record SpaceOperationsDwellHotspotDto(
    Guid LocationLogicalId,
    string? SpaceLocationCode,
    Guid FloorLogicalId,
    string? FloorCode,
    int EpisodeCount,
    int PersonCount,
    int TotalDwellSeconds,
    int MaximumDwellSeconds);

public sealed record SpaceOperationsDwellDiagnosisDto(
    int EpisodeCount,
    int PersonCount,
    int LocationCount,
    int TotalDwellSeconds,
    bool HotspotsTruncated,
    IReadOnlyList<SpaceOperationsDwellHotspotDto> Hotspots);

public sealed record SpaceOperationsCongestionHotspotDto(
    Guid LocationLogicalId,
    string? SpaceLocationCode,
    Guid FloorLogicalId,
    string? FloorCode,
    int PeakConcurrentPeople,
    int ConcurrentSeconds,
    int ObservedPersonCount);

public sealed record SpaceOperationsCongestionDiagnosisDto(
    int LocationCount,
    int PeakConcurrentPeople,
    int ConcurrentSeconds,
    bool HotspotsTruncated,
    IReadOnlyList<SpaceOperationsCongestionHotspotDto> Hotspots);

public sealed record SpaceOperationsFloorOccupancyDto(
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    int LocationCount,
    int? OccupiedLocationCount,
    decimal? LocationOccupancyPercent,
    string LocationOccupancyPressure);

public sealed record SpaceOperationsCapacityDiagnosisDto(
    SpaceWmsRuntimeSourceDto? Source,
    bool IsAvailable,
    string OccupancyBasis,
    int LocationCount,
    int? OccupiedLocationCount,
    decimal? LocationOccupancyPercent,
    string LocationOccupancyPressure,
    decimal? CapacityUtilizationPercent,
    string CapacityUtilizationStatus,
    string CapacityUtilizationReason,
    IReadOnlyList<SpaceOperationsFloorOccupancyDto> Floors);

public sealed record SpaceOperationsDiagnosticResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string? WarehouseCode,
    DateTimeOffset WindowFromUtc,
    DateTimeOffset WindowToUtc,
    DateTimeOffset CalculatedAtUtc,
    string DefinitionVersion,
    SpaceOperationsDiagnosticThresholdsDto Thresholds,
    SpaceOperationsPersonnelSourceDto PersonnelSource,
    SpaceOperationsPathDiagnosisDto Path,
    SpaceOperationsCongestionDiagnosisDto Congestion,
    SpaceOperationsDwellDiagnosisDto Dwell,
    SpaceOperationsCapacityDiagnosisDto Capacity,
    IReadOnlyList<string> Limitations);
