namespace CP6.Space.Contracts;

public sealed record SpacePlanningSimulationLocationCapacityRequest(
    Guid LocationLogicalId,
    decimal QuantityCapacity,
    int ConcurrentTaskCapacity);

public sealed record CreateSpacePlanningSimulationRunRequest(
    string Name,
    Guid DatasetId,
    decimal DefaultQuantityCapacity,
    int DefaultConcurrentTaskCapacity,
    int ThroughputWindowMinutes,
    decimal DistanceCostPerMeter,
    decimal LaborCostPerHour,
    decimal CongestionCostPerTaskHour,
    string CurrencyCode,
    IReadOnlyList<SpacePlanningSimulationLocationCapacityRequest>
        LocationCapacities);

public sealed record SpacePlanningSimulationParametersDto(
    decimal DefaultQuantityCapacity,
    int DefaultConcurrentTaskCapacity,
    int ThroughputWindowMinutes,
    decimal DistanceCostPerMeter,
    decimal LaborCostPerHour,
    decimal CongestionCostPerTaskHour,
    string CurrencyCode,
    int LocationCapacityOverrideCount);

public sealed record SpacePlanningSimulationDistanceDto(
    string GeometryBasis,
    int TaskCount,
    int EligibleTaskCount,
    int UnknownTaskCount,
    decimal CoveragePercent,
    decimal TotalDistanceMeters,
    decimal? AverageEligibleTaskDistanceMeters);

public sealed record SpacePlanningSimulationCongestionDto(
    int MonitoredLocationCount,
    int OverloadedLocationCount,
    int PeakConcurrentTasks,
    long CongestionSeconds,
    long CongestionTaskSeconds,
    decimal CongestionTaskHours);

public sealed record SpacePlanningSimulationCapacityDto(
    int MonitoredLocationCount,
    int OverloadedLocationCount,
    decimal PeakUtilizationPercent,
    string QuantityBasis);

public sealed record SpacePlanningSimulationThroughputDto(
    int CompletedTaskCount,
    decimal CompletedQuantity,
    decimal HistoricalWindowHours,
    int MeasurementWindowMinutes,
    decimal AverageCompletedTasksPerHour,
    decimal PeakCompletedTasksPerHour,
    decimal AverageCompletedQuantityPerHour,
    decimal PeakCompletedQuantityPerHour);

public sealed record SpacePlanningSimulationCostDto(
    string CurrencyCode,
    decimal LaborHours,
    decimal DistanceCost,
    decimal LaborCost,
    decimal CongestionCost,
    decimal TotalCost,
    string LaborBasis);

public sealed record SpacePlanningSimulationLocationResultDto(
    Guid LocationLogicalId,
    int TaskCount,
    int CompletedTaskCount,
    decimal TotalQuantity,
    int DistanceEligibleTaskCount,
    decimal TotalDistanceMeters,
    decimal QuantityCapacity,
    int ConcurrentTaskCapacity,
    int PeakConcurrentTasks,
    decimal PeakConcurrentQuantity,
    decimal CapacityUtilizationPercent,
    long CongestionSeconds,
    long CongestionTaskSeconds,
    bool IsOverloaded);

public sealed record SpacePlanningSimulationRunDto(
    Guid RunId,
    Guid SiteId,
    Guid BranchId,
    Guid ScenarioVersionId,
    long ScenarioContentRevision,
    Guid DatasetId,
    string Name,
    string Status,
    string DefinitionVersion,
    string DatasetRequestHash,
    string ResultHash,
    bool ProductionWriteAllowed,
    bool HighPrecisionPhysicalSimulation,
    SpacePlanningSimulationParametersDto Parameters,
    SpacePlanningSimulationDistanceDto Distance,
    SpacePlanningSimulationCongestionDto Congestion,
    SpacePlanningSimulationCapacityDto Capacity,
    SpacePlanningSimulationThroughputDto Throughput,
    SpacePlanningSimulationCostDto Cost,
    IReadOnlyList<SpacePlanningSimulationLocationResultDto> LocationResults,
    bool LocationResultsTruncated,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    IReadOnlyList<string> Limitations);

public sealed record SpacePlanningSimulationRunSummaryDto(
    Guid RunId,
    Guid DatasetId,
    long ScenarioContentRevision,
    string Name,
    string Status,
    string CurrencyCode,
    int TaskCount,
    decimal DistanceCoveragePercent,
    decimal TotalDistanceMeters,
    int OverloadedLocationCount,
    decimal AverageCompletedTasksPerHour,
    decimal TotalCost,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateSpacePlanningSimulationRunResponse(
    string Outcome,
    SpacePlanningSimulationRunDto Run);

public sealed record SpacePlanningSimulationRunListResponse(
    IReadOnlyList<SpacePlanningSimulationRunSummaryDto> Items,
    bool IsTruncated);
