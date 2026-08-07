namespace CP6.Space.Contracts;

public sealed record SpacePublishPreviewItemDto(
    int SequenceNo,
    string ObjectType,
    Guid LogicalId,
    Guid? FloorLogicalId,
    string Action,
    string? BeforeHash,
    string? AfterHash,
    string? BeforeCode,
    string? AfterCode,
    string? ExternalBindingId,
    string PayloadHash,
    string ImpactCode,
    bool MasterChanged,
    bool GeometryChanged,
    bool ProvenanceChanged,
    bool WmsChanged,
    bool Blocking);

public sealed record SpacePublishChangeSummaryDto(
    int CreateCount,
    int UpdateMasterCount,
    int UpdateGeometryOnlyCount,
    int DisableCount,
    int RestoreCount,
    int NoOpCount);

public sealed record SpacePublishImpactSummaryDto(
    int WmsCreateCount,
    int WmsUpdateCount,
    int WmsDisableCount,
    int WmsRestoreCount,
    int WmsNoOpCount,
    int RuntimeOnlyCount,
    int BlockingCount);

public sealed record SpacePublishPreviewDto(
    Guid TargetVersionId,
    Guid? BaseVersionId,
    Guid ValidationRunId,
    string ValidationStatus,
    int ValidationBlockingCount,
    string ContentHash,
    string PlanRuleSetVersion,
    string AdapterId,
    string CapabilityHash,
    string PlanHash,
    bool Publishable,
    int ItemCount,
    int ChangeCount,
    int MatchedItemCount,
    SpacePublishChangeSummaryDto Changes,
    SpacePublishImpactSummaryDto WmsImpact,
    IReadOnlyList<SpacePublishPreviewItemDto> Items,
    string? NextCursor);
