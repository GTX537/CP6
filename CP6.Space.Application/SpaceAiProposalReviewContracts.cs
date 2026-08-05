using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class WarehouseProposalReviewVersions
{
    public const int SchemaVersion = 1;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
    public const int MaximumItems = 100_000;
    public const int MaximumBatchSelection = 1_000;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseProposalReviewReadiness
{
    Ready = 0,
    NeedsReview = 1,
    Blocked = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseProposalDifferenceKind
{
    Added = 0,
    Modified = 1,
    Unchanged = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseProposalFieldDifferenceKind
{
    Added = 0,
    Removed = 1,
    Changed = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseProposalBatchAction
{
    Accept = 0,
    Reject = 1,
}

public sealed record WarehouseProposalReviewBaselineFieldV1(
    string FieldPath,
    string ValueToken);

public sealed record WarehouseProposalReviewBaselineObjectV1(
    Guid LogicalId,
    WarehouseSpaceType ObjectType,
    string GeometrySha256,
    SpaceCadMillimeterBoundsV1 GeometryBounds,
    IReadOnlyList<WarehouseProposalReviewBaselineFieldV1> Fields,
    long RackLevelCount,
    long LocationCount);

public sealed record WarehouseProposalReviewBaselineSnapshotV1(
    int SchemaVersion,
    bool IsReadOnlySnapshot,
    bool IsCompleteFloorProjection,
    Guid TenantId,
    Guid ModelVersionId,
    Guid FloorLogicalId,
    long ContentRevision,
    string? ContentHash,
    IReadOnlyList<WarehouseProposalReviewBaselineObjectV1> Objects,
    string SnapshotSha256);

public sealed record WarehouseProposalFieldDifferenceV1(
    string FieldPath,
    WarehouseProposalFieldDifferenceKind Kind,
    string? BeforeValueToken,
    string? AfterValueToken,
    WarehouseFusionSource? WinningSource,
    decimal? Confidence,
    IReadOnlyList<WarehouseFusionEvidenceV1> Evidence);

public sealed record WarehouseProposalDifferenceV1(
    WarehouseProposalDifferenceKind Kind,
    bool GeometryChanged,
    string? BeforeGeometrySha256,
    string AfterGeometrySha256,
    SpaceCadMillimeterBoundsV1? BeforeGeometryBounds,
    SpaceCadMillimeterBoundsV1 AfterGeometryBounds,
    IReadOnlyList<WarehouseProposalFieldDifferenceV1> Fields,
    long BeforeRackLevelCount,
    long AfterRackLevelCount,
    long BeforeLocationCount,
    long AfterLocationCount);

public sealed record WarehouseProposalReviewLocationV1(
    Guid FloorLogicalId,
    string SourceRef,
    SpaceCadMillimeterBoundsV1 Bounds,
    SpaceCadMillimeterPointV1 Anchor,
    int SuggestedPaddingMillimeters,
    bool CanFocusCanvas);

public sealed record WarehouseProposalReviewItemV1(
    string ReviewItemId,
    Guid LogicalId,
    string SourceKey,
    string SourceRef,
    WarehouseSpaceType ObjectType,
    decimal Confidence,
    WarehouseFusionConfidenceBand ConfidenceBand,
    WarehouseProposalReviewReadiness Readiness,
    bool HasBlockingIssue,
    bool CanBatchAccept,
    WarehouseProposalReviewLocationV1 Location,
    IReadOnlyList<WarehouseResolvedFieldV1> Fields,
    IReadOnlyList<WarehouseProposalRelationV1> Relations,
    WarehouseRackDerivationV1? RackDerivation,
    IReadOnlyList<WarehouseProposalIssueV1> Issues,
    WarehouseProposalDifferenceV1 Difference);

public sealed record WarehouseProposalReviewSummaryV1(
    long TotalCount,
    long HighConfidenceCount,
    long MediumConfidenceCount,
    long LowConfidenceCount,
    long ReadyCount,
    long NeedsReviewCount,
    long BlockedCount,
    long BatchAcceptEligibleCount,
    long AddedCount,
    long ModifiedCount,
    long UnchangedCount,
    long LocatableCount,
    long InfoIssueCount,
    long WarningIssueCount,
    long BlockingIssueCount,
    long RunIssueCount,
    long RunBlockingIssueCount);

public sealed record WarehouseProposalReviewWorkspaceV1(
    int SchemaVersion,
    bool IsReadOnlyWorkspace,
    bool DecisionWritten,
    bool DraftWritten,
    Guid TenantId,
    Guid ModelVersionId,
    Guid FloorLogicalId,
    string ProposalSetSha256,
    string BaselineSnapshotSha256,
    long BaselineContentRevision,
    string? BaselineContentHash,
    IReadOnlyList<WarehouseProposalIssueV1> RunIssues,
    IReadOnlyList<WarehouseProposalReviewItemV1> Items,
    WarehouseProposalReviewSummaryV1 Summary,
    string ReviewEtag,
    string WorkspaceSha256);

public sealed record WarehouseProposalReviewFilterV1(
    WarehouseFusionConfidenceBand? ConfidenceBand = null,
    WarehouseSpaceType? ObjectType = null,
    WarehouseProposalReviewReadiness? Readiness = null,
    WarehouseProposalDifferenceKind? DifferenceKind = null,
    WarehouseProposalIssueSeverity? IssueSeverity = null,
    string? IssueCode = null,
    WarehouseFusionSource? WinningSource = null,
    string? EvidenceCode = null,
    string? SourceRef = null,
    string? Search = null,
    bool OnlyLocatable = false);

public sealed record WarehouseProposalReviewQueryV1(
    WarehouseProposalReviewFilterV1? Filter = null,
    string? Cursor = null,
    int Limit = WarehouseProposalReviewVersions.DefaultPageSize);

public sealed record WarehouseProposalReviewPageV1(
    string ReviewEtag,
    string FilterHash,
    int Limit,
    long TotalCount,
    IReadOnlyList<WarehouseProposalReviewItemV1> Items,
    string? NextCursor);

public sealed record WarehouseProposalBatchSelectionRequestV1(
    WarehouseProposalBatchAction Action,
    string ReviewEtag,
    IReadOnlyList<string>? ReviewItemIds = null,
    WarehouseProposalReviewFilterV1? Filter = null);

public sealed record WarehouseProposalBatchIneligibleItemV1(
    string ReviewItemId,
    string ReasonCode);

public sealed record WarehouseProposalBatchSelectionPreviewV1(
    WarehouseProposalBatchAction Action,
    string ReviewEtag,
    long SelectedCount,
    IReadOnlyList<string> EligibleReviewItemIds,
    IReadOnlyList<WarehouseProposalBatchIneligibleItemV1> IneligibleItems,
    bool RequiresServerRevalidation,
    bool DecisionWritten,
    bool DraftWritten);
