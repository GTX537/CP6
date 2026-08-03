using System.Text.Json.Serialization;

namespace CP6.Space.Contracts;

public static class SpaceCadMappingVersions
{
    public const int SchemaVersion = 1;
    public const int MaximumRules = 500;
    public const int MaximumOverrides = 5_000;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadMappingScope
{
    System = 0,
    Tenant = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadMappingSourceKind
{
    Layer = 0,
    Block = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadMappingMatchKind
{
    Exact = 0,
    Glob = 1,
    Regex = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadSemanticTarget
{
    Wall = 0,
    Column = 1,
    Door = 2,
    Dock = 3,
    Zone = 4,
    Aisle = 5,
    Rack = 6,
    Equipment = 7,
    VerticalCirculation = 8,
    Annotation = 9,
    Guide = 10,
    RestrictedArea = 11,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadGeometryRule
{
    DirectGeometry = 0,
    Centerline = 1,
    ClosedBoundary = 2,
    BlockFootprint = 3,
    InsertionPoint = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadMappingDecisionStatus
{
    Mapped = 0,
    Unmapped = 1,
    Ignored = 2,
    Conflict = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceCadMappingDecisionSource
{
    ProfileRule = 0,
    LayerOverride = 1,
    None = 2,
}

public sealed record SpaceCadMappingRuleV1(
    string RuleId,
    int Priority,
    SpaceCadMappingSourceKind SourceKind,
    SpaceCadMappingMatchKind MatchKind,
    string Pattern,
    string? AttributeName,
    SpaceCadMappingMatchKind? AttributeMatchKind,
    string? AttributePattern,
    SpaceCadSemanticTarget Target,
    string? TargetSubtype,
    SpaceCadGeometryRule GeometryRule,
    decimal? DefaultHeightMillimeters,
    decimal? DefaultThicknessMillimeters,
    decimal ConfidenceWeight,
    bool IsRequired);

public sealed record SpaceCadMappingProfileDraftV1(
    int SchemaVersion,
    Guid ProfileId,
    int Version,
    string Name,
    SpaceCadMappingScope Scope,
    Guid? TenantId,
    bool IsEnabled,
    Guid? BasedOnProfileId,
    int? BasedOnVersion,
    IReadOnlyList<SpaceCadMappingRuleV1> Rules);

public sealed record SpaceCadMappingProfileV1(
    int SchemaVersion,
    Guid ProfileId,
    int Version,
    string Name,
    SpaceCadMappingScope Scope,
    Guid? TenantId,
    bool IsEnabled,
    Guid? BasedOnProfileId,
    int? BasedOnVersion,
    IReadOnlyList<SpaceCadMappingRuleV1> Rules,
    string DefinitionSha256);

public sealed record SpaceCadLayerMappingOverrideV1(
    string LayerId,
    bool Ignore,
    SpaceCadSemanticTarget? Target,
    string? TargetSubtype,
    SpaceCadGeometryRule? GeometryRule,
    decimal? DefaultHeightMillimeters,
    decimal? DefaultThicknessMillimeters,
    decimal? ConfidenceWeight);

public sealed record SpaceCadMappingDecisionV1(
    SpaceCadMappingSourceKind SourceKind,
    string SourceKey,
    string? LayerId,
    long ObjectCount,
    SpaceCadMappingDecisionStatus Status,
    SpaceCadMappingDecisionSource DecisionSource,
    string? RuleId,
    SpaceCadSemanticTarget? Target,
    string? TargetSubtype,
    SpaceCadGeometryRule? GeometryRule,
    decimal? DefaultHeightMillimeters,
    decimal? DefaultThicknessMillimeters,
    decimal? ConfidenceWeight);

public sealed record SpaceCadMappingIssueV1(
    string Code,
    SpaceCadIssueSeverity Severity,
    SpaceCadMappingSourceKind? SourceKind = null,
    string? SourceKey = null,
    string? RuleId = null,
    string? DetailToken = null);

public sealed record SpaceCadMappingPreviewSummaryV1(
    long LayerCount,
    long MappedLayerCount,
    long UnmappedLayerCount,
    long IgnoredLayerCount,
    long ConflictLayerCount,
    long BlockCount,
    long MappedBlockCount,
    long UnmappedBlockCount,
    long ConflictBlockCount,
    long MappedLayerEntityCount,
    long MappedBlockReferenceCount,
    long InfoCount,
    long WarningCount,
    long BlockingCount);

public sealed record SpaceCadMappingPreviewV1(
    int SchemaVersion,
    Guid TenantId,
    Guid ProfileId,
    int ProfileVersion,
    string ProfileDefinitionSha256,
    string SourceSha256,
    string InventorySha256,
    string SourceStructureSha256,
    string ReuseKeySha256,
    IReadOnlyList<SpaceCadLayerMappingOverrideV1> LayerOverrides,
    IReadOnlyList<SpaceCadMappingDecisionV1> Decisions,
    IReadOnlyList<SpaceCadMappingIssueV1> Issues,
    SpaceCadMappingPreviewSummaryV1 Summary,
    bool ReadyForSemanticParsing,
    string PreviewSha256);
