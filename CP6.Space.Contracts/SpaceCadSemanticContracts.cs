using System.Text.Json.Serialization;

namespace CP6.Space.Contracts;

public static class SpaceCadSemanticVersions
{
    public const int SchemaVersion = 1;
    public const decimal AutoAcceptanceThreshold = 0.90m;
    public const decimal ReviewThreshold = 0.70m;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadSemanticDraftObjectKind
{
    Element = 0,
    Zone = 1,
    Aisle = 2,
    Rack = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadSemanticGeometryKind
{
    Point = 0,
    Path = 1,
    Polygon = 2,
    Circle = 3,
    Arc = 4,
    BlockInstance = 5,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadSemanticDisposition
{
    AutoAccepted = 0,
    Candidate = 1,
    Rejected = 2,
}

public sealed record SpaceCadMillimeterBoundsV1(
    int MinX,
    int MinY,
    int MaxX,
    int MaxY);

public sealed record SpaceCadSemanticTransformV1(
    decimal M11,
    decimal M12,
    decimal M21,
    decimal M22,
    int OffsetX,
    int OffsetY,
    int OffsetZ);

public sealed record SpaceCadSemanticGeometryV1(
    SpaceCadSemanticGeometryKind Kind,
    IReadOnlyList<SpaceCadMillimeterPointV1> Points,
    int? RadiusMillimeters,
    decimal? StartAngleDegrees,
    decimal? EndAngleDegrees,
    bool IsClosed,
    SpaceCadSemanticTransformV1? Transform,
    SpaceCadMillimeterBoundsV1 Bounds);

public sealed record SpaceCadSemanticSourceReferenceV1(
    string SourceRef,
    string RawType,
    string LayerId,
    string? BlockName,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record SpaceCadSemanticAppliedMappingV1(
    SpaceCadMappingSourceKind SourceKind,
    string SourceKey,
    SpaceCadMappingDecisionSource DecisionSource,
    string? RuleId,
    SpaceCadGeometryRule GeometryRule,
    int? DefaultHeightMillimeters,
    int? DefaultThicknessMillimeters);

public sealed record SpaceCadSemanticPreviewItemV1(
    string PreviewObjectId,
    SpaceCadSemanticDraftObjectKind DraftObjectKind,
    SpaceCadSemanticTarget Target,
    string? TargetSubtype,
    SpaceCadSemanticSourceReferenceV1 Source,
    SpaceCadSemanticAppliedMappingV1 AppliedMapping,
    SpaceCadSemanticGeometryV1? Geometry,
    decimal Confidence,
    SpaceCadSemanticDisposition Disposition,
    bool IsConfirmable,
    bool IsSelected);

public sealed record SpaceCadSemanticIssueV1(
    string Code,
    SpaceCadIssueSeverity Severity,
    string? SourceRef = null,
    string? PreviewObjectId = null,
    SpaceCadMappingSourceKind? SourceKind = null,
    string? SourceKey = null,
    string? RuleId = null,
    string? DetailToken = null);

public sealed record SpaceCadSemanticPreviewSummaryV1(
    long SourceEntityCount,
    long MappedEntityCount,
    long AutoAcceptedCount,
    long CandidateCount,
    long RejectedCount,
    long ConfirmableCount,
    long SelectedCount,
    long InfoCount,
    long WarningCount,
    long BlockingCount);

public sealed record SpaceCadSemanticPreviewV1(
    int SchemaVersion,
    bool IsReadOnlyPreview,
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
    IReadOnlyList<SpaceCadSemanticPreviewItemV1> Items,
    IReadOnlyList<SpaceCadSemanticIssueV1> Issues,
    SpaceCadSemanticPreviewSummaryV1 Summary,
    bool ReadyForConfirmation,
    string SemanticPreviewSha256);
