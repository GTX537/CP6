namespace CP6.Space.Contracts;

public sealed record GenerateSpacePutawayRecommendationRequest(
    string MaterialNumber,
    string? OwnerId,
    string? LotNumber,
    decimal InboundQuantity,
    Guid? FloorLogicalId = null,
    Guid? ZoneLogicalId = null,
    int? RequiredWidthMillimeters = null,
    int? RequiredHeightMillimeters = null,
    int? RequiredDepthMillimeters = null,
    decimal? RequiredMaxLoad = null,
    bool AllowExactStockConsolidation = true,
    int MaximumCandidates = 10);

public sealed record SpacePutawayRecommendationSourcesDto(
    SpaceWmsRuntimeSourceDto Inventory,
    SpaceWmsRuntimeSourceDto ActiveTasks);

public sealed record SpacePutawayRecommendationExclusionsDto(
    int MissingSpatialMetadata,
    int OutsideRequestedScope,
    int ActiveTask,
    int InvalidInventory,
    int LocationCodeMismatch,
    int OccupiedIncompatible,
    int DimensionTooSmall,
    int LoadUnverifiable,
    int LoadInsufficient);

public sealed record SpacePutawayRecommendationExclusionSampleDto(
    Guid LocationLogicalId,
    string? SpaceLocationCode,
    Guid FloorLogicalId,
    string? FloorCode,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    string Reason);

public sealed record SpacePutawayRecommendationCandidateDto(
    int Rank,
    string Category,
    Guid LocationLogicalId,
    string SpaceLocationCode,
    Guid FloorLogicalId,
    string FloorCode,
    string FloorName,
    int FloorLevel,
    Guid? ZoneLogicalId,
    string? ZoneCode,
    Guid? RackLogicalId,
    string? RackCode,
    int ColumnNo,
    int LevelNo,
    int DepthNo,
    int WidthMillimeters,
    int HeightMillimeters,
    int DepthMillimeters,
    decimal? MaxLoad,
    decimal CurrentPhysicalQuantity,
    decimal CurrentAllocatedQuantity,
    bool SameFloorAsExistingStock,
    bool SameZoneAsExistingStock,
    decimal? DistanceToMatchingStockMeters,
    IReadOnlyList<string> RuleHits);

public sealed record SpacePutawayRecommendationDto(
    Guid RecommendationId,
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    DateTimeOffset GeneratedAtUtc,
    Guid GeneratedBy,
    string DefinitionVersion,
    string Outcome,
    GenerateSpacePutawayRecommendationRequest Request,
    SpacePutawayRecommendationSourcesDto Sources,
    int ExaminedLocationCount,
    int EligibleCandidateCount,
    int ReturnedCandidateCount,
    bool IsTruncated,
    SpacePutawayRecommendationExclusionsDto Exclusions,
    bool ExclusionSamplesTruncated,
    IReadOnlyList<SpacePutawayRecommendationExclusionSampleDto>
        ExclusionSamples,
    IReadOnlyList<SpacePutawayRecommendationCandidateDto> Candidates,
    IReadOnlyList<string> Limitations);

public sealed record GenerateSpacePutawayRecommendationResponse(
    string Outcome,
    SpacePutawayRecommendationDto Recommendation);
