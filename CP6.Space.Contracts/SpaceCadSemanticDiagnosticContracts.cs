using System.Text.Json.Serialization;

namespace CP6.Space.Contracts;

public static class SpaceCadSemanticDiagnosticVersions
{
    public const int SchemaVersion = 1;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadConfidenceBand
{
    High = 0,
    Review = 1,
    Low = 2,
    Rejected = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadDiagnosticOrigin
{
    Mapping = 0,
    Semantic = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadDiagnosticLocationKind
{
    Document = 0,
    Layer = 1,
    Block = 2,
    Entity = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadDiagnosticRecovery
{
    None = 0,
    MapSource = 1,
    FixMappingConflict = 2,
    ReviewCandidate = 3,
    InspectGeometry = 4,
    ConfirmRequiredSource = 5,
}

public sealed record SpaceCadDiagnosticLocationV1(
    SpaceCadDiagnosticLocationKind Kind,
    Guid FloorLogicalId,
    string? LayerId,
    string? BlockName,
    string? SourceRef,
    string? PreviewObjectId,
    SpaceCadMillimeterBoundsV1? Bounds,
    SpaceCadMillimeterPointV1? Anchor,
    int SuggestedPaddingMillimeters,
    bool CanFocusCanvas);

public sealed record SpaceCadSemanticEvidenceV1(
    string PreviewObjectId,
    string SourceRef,
    string SemanticPreviewSha256,
    SpaceCadSemanticTarget Target,
    string? TargetSubtype,
    decimal Confidence,
    SpaceCadSemanticDisposition Disposition,
    SpaceCadConfidenceBand ConfidenceBand,
    SpaceCadMappingSourceKind SourceKind,
    string SourceKey,
    SpaceCadMappingDecisionSource DecisionSource,
    string? RuleId,
    SpaceCadGeometryRule GeometryRule,
    SpaceCadDiagnosticLocationV1 Location,
    string EvidenceSha256);

public sealed record SpaceCadSemanticDiagnosticV1(
    string DiagnosticId,
    SpaceCadDiagnosticOrigin Origin,
    string Code,
    SpaceCadIssueSeverity Severity,
    SpaceCadDiagnosticRecovery Recovery,
    SpaceCadConfidenceBand? ConfidenceBand,
    SpaceCadMappingSourceKind? SourceKind,
    string? SourceKey,
    string? SourceRef,
    string? PreviewObjectId,
    string? RuleId,
    string? DetailToken,
    SpaceCadDiagnosticLocationV1 Location);

public sealed record SpaceCadSemanticDiagnosticSummaryV1(
    long SourceEntityCount,
    long ProposalCount,
    long HighConfidenceCount,
    long ReviewConfidenceCount,
    long LowConfidenceCount,
    long RejectedCount,
    long MappingDiagnosticCount,
    long SemanticDiagnosticCount,
    long LocatableDiagnosticCount,
    long UnlocatableDiagnosticCount,
    long InfoCount,
    long WarningCount,
    long BlockingCount);

public sealed record SpaceCadSemanticDiagnosticIndexV1(
    int SchemaVersion,
    bool IsReadOnlyIndex,
    Guid TenantId,
    Guid FloorLogicalId,
    string FloorCode,
    string SourceSha256,
    string CoordinateTransformSha256,
    string InventorySha256,
    Guid ProfileId,
    int ProfileVersion,
    string ProfileDefinitionSha256,
    string MappingPreviewSha256,
    string SemanticPreviewSha256,
    IReadOnlyList<SpaceCadSemanticEvidenceV1> Evidence,
    IReadOnlyList<SpaceCadSemanticDiagnosticV1> Diagnostics,
    SpaceCadSemanticDiagnosticSummaryV1 Summary,
    string DiagnosticIndexSha256);

public sealed record SpaceCadSemanticEvidenceQueryV1(
    SpaceCadConfidenceBand? ConfidenceBand = null,
    SpaceCadSemanticTarget? Target = null,
    string? LayerId = null,
    string? SourceRef = null,
    bool OnlyWithDiagnostics = false,
    int Offset = 0,
    int Limit = SpaceCadSemanticDiagnosticVersions.DefaultPageSize);

public sealed record SpaceCadSemanticDiagnosticQueryV1(
    SpaceCadIssueSeverity? Severity = null,
    SpaceCadDiagnosticOrigin? Origin = null,
    string? Code = null,
    string? LayerId = null,
    string? SourceRef = null,
    bool OnlyLocatable = false,
    int Offset = 0,
    int Limit = SpaceCadSemanticDiagnosticVersions.DefaultPageSize);

public sealed record SpaceCadSemanticPageV1<T>(
    int Offset,
    int Limit,
    long TotalCount,
    IReadOnlyList<T> Items);
