namespace CP6.Space.Contracts;

public sealed record GenerateSpaceDispatchRecommendationRequest(
    string? TaskType = null,
    Guid? TaskFloorLogicalId = null,
    Guid? TaskZoneLogicalId = null,
    bool AllowCrossFloor = false,
    decimal? MaximumTravelDistanceMeters = null,
    bool IncludeSimulatedPersonnel = false,
    int MaximumAssignments = 20);

public sealed record SpaceDispatchPersonnelSourceItemDto(
    string SourceId,
    string SourceKind,
    int CurrentStateCount,
    DateTimeOffset? LatestPositionOccurredAtUtc,
    DateTimeOffset? LatestPositionReceivedAtUtc,
    DateTimeOffset? LatestWorkStateOccurredAtUtc,
    DateTimeOffset? LatestWorkStateReceivedAtUtc);

public sealed record SpaceDispatchPersonnelSourceDto(
    DateTimeOffset AsOfUtc,
    int FreshnessThresholdSeconds,
    int CurrentStateCount,
    int RealStateCount,
    int SimulatedStateCount,
    bool SourcesTruncated,
    IReadOnlyList<SpaceDispatchPersonnelSourceItemDto> Sources);

public sealed record SpaceDispatchRecommendationSourcesDto(
    SpaceWmsRuntimeSourceDto DispatchTasks,
    SpaceDispatchPersonnelSourceDto Personnel);

public sealed record SpaceDispatchRecommendationExclusionsDto(
    int TasksOutsideRequestedScope,
    int TasksNotPending,
    int TasksAlreadyAssigned,
    int InvalidTasks,
    int TaskTargetOutsidePublishedModel,
    int TaskLocationCodeMismatch,
    int EligibleTasksWithoutAssignment,
    int PeoplePositionStale,
    int PeopleWorkStateStale,
    int PeopleNotIdle,
    int PeopleSimulatedExcluded,
    int PeopleWithoutResolvablePosition,
    int EligiblePeopleWithoutAssignment,
    int CrossFloorPairsRejected,
    int DistanceUnverifiablePairsRejected,
    int DistanceExceededPairsRejected);

public sealed record SpaceDispatchRecommendationExclusionSampleDto(
    string Subject,
    string Reason,
    string? TaskId,
    string? PersonKey,
    string? LocationCode,
    Guid? FloorLogicalId,
    string? FloorCode,
    Guid? ZoneLogicalId,
    string? ZoneCode);

public sealed record SpaceDispatchRecommendationAssignmentDto(
    int Rank,
    string TaskId,
    string TaskType,
    string TaskStatus,
    int TaskPriority,
    int TaskContractVersion,
    int TaskExecutionVersion,
    string TaskRowVersion,
    string TargetLocationRole,
    Guid TargetLocationLogicalId,
    string TargetLocationCode,
    Guid TargetFloorLogicalId,
    string TargetFloorCode,
    string TargetFloorName,
    int TargetFloorLevel,
    Guid? TargetZoneLogicalId,
    string? TargetZoneCode,
    Guid? TargetRackLogicalId,
    string? TargetRackCode,
    decimal TaskQuantity,
    string? TaskMaterialNumber,
    string PersonKey,
    string PersonSourceId,
    string PersonSourceKind,
    string PersonExternalId,
    Guid? PersonLocationLogicalId,
    Guid PersonFloorLogicalId,
    Guid? PersonZoneLogicalId,
    DateTimeOffset PersonPositionOccurredAtUtc,
    DateTimeOffset PersonPositionReceivedAtUtc,
    DateTimeOffset PersonWorkStateOccurredAtUtc,
    DateTimeOffset PersonWorkStateReceivedAtUtc,
    bool SameFloor,
    bool SameZone,
    decimal? GeometricDistanceMeters,
    IReadOnlyList<string> RuleHits);

public sealed record SpaceDispatchRecommendationDto(
    Guid RecommendationId,
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    DateTimeOffset GeneratedAtUtc,
    Guid GeneratedBy,
    string DefinitionVersion,
    string Outcome,
    GenerateSpaceDispatchRecommendationRequest Request,
    SpaceDispatchRecommendationSourcesDto Sources,
    int ExaminedTaskCount,
    int EligibleTaskCount,
    int ExaminedPersonCount,
    int EligiblePersonCount,
    int EligiblePairCount,
    int MatchableAssignmentCount,
    int ReturnedAssignmentCount,
    bool IsTruncated,
    SpaceDispatchRecommendationExclusionsDto Exclusions,
    bool ExclusionSamplesTruncated,
    IReadOnlyList<SpaceDispatchRecommendationExclusionSampleDto>
        ExclusionSamples,
    IReadOnlyList<SpaceDispatchRecommendationAssignmentDto> Assignments,
    IReadOnlyList<string> Limitations);

public sealed record GenerateSpaceDispatchRecommendationResponse(
    string Outcome,
    SpaceDispatchRecommendationDto Recommendation);
