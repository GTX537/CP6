using System.Text.Json.Serialization;

namespace CP6.Space.Contracts;

public static class SpaceCadReviewWorkspaceVersions
{
    public const int SchemaVersion = 1;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
    public const int MaximumItems = 100_000;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadReviewItemKind
{
    MappingDiagnostic = 0,
    SemanticDiagnostic = 1,
    LowConfidenceProposal = 2,
    RejectedProposal = 3,
    ExcelUnmatched = 4,
    ExcelConflict = 5,
    ExcelError = 6,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadReviewItemStatus
{
    Open = 0,
    Resolved = 1,
}

public sealed record SpaceCadReviewItemV1(
    string ReviewItemId,
    string TrackingKey,
    SpaceCadReviewItemKind Kind,
    SpaceCadIssueSeverity Severity,
    SpaceCadReviewItemStatus Status,
    string Code,
    IReadOnlyList<string> RelatedCodes,
    string? DetailToken,
    string SuggestedActionCode,
    string? SourceRef,
    string? PreviewObjectId,
    Guid? TargetLogicalId,
    string? RackCode,
    SpaceCadConfidenceBand? ConfidenceBand,
    SpaceCadDiagnosticLocationV1 Location,
    string UpstreamEvidenceSha256,
    string? ResolvedFromWorkspaceSha256);

public sealed record SpaceCadReviewWorkspaceSummaryV1(
    long TotalCount,
    long OpenCount,
    long ResolvedCount,
    long OpenInfoCount,
    long OpenWarningCount,
    long OpenBlockingCount,
    long LocatableCount,
    long UnlocatableCount,
    long CadDiagnosticCount,
    long ProposalReviewCount,
    long ExcelReviewCount);

public sealed record SpaceCadReviewWorkspaceV1(
    int SchemaVersion,
    bool IsReadOnlyWorkspace,
    Guid TenantId,
    Guid ModelVersionId,
    Guid FloorLogicalId,
    string FloorCode,
    string DiagnosticIndexSha256,
    string? MatchPreviewSha256,
    long EditorContentRevision,
    string? EditorContentHash,
    string EditorSnapshotSha256,
    string? PreviousWorkspaceSha256,
    IReadOnlyList<SpaceCadReviewItemV1> Items,
    SpaceCadReviewWorkspaceSummaryV1 Summary,
    string WorkspaceSha256);

public sealed record SpaceCadReviewWorkspaceQueryV1(
    SpaceCadReviewItemStatus? Status = null,
    SpaceCadIssueSeverity? Severity = null,
    SpaceCadReviewItemKind? Kind = null,
    string? SourceRef = null,
    string? Search = null,
    bool OnlyLocatable = false,
    int Offset = 0,
    int Limit = SpaceCadReviewWorkspaceVersions.DefaultPageSize);

public sealed record SpaceCadReviewWorkspacePageV1(
    int Offset,
    int Limit,
    long TotalCount,
    IReadOnlyList<SpaceCadReviewItemV1> Items);
