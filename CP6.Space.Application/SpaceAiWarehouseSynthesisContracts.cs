using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class WarehouseDraftSynthesisVersions
{
    public const int SchemaVersion = 1;
    public const string IdentityAlgorithm = "uuidv5-rfc4122-v1";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseFusionSource
{
    TemplateDefault = 0,
    Ai = 1,
    DeterministicRule = 2,
    HumanLocked = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseFusionConfidenceBand
{
    High = 0,
    Medium = 1,
    Low = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseProposalIssueSeverity
{
    Info = 0,
    Warning = 1,
    Blocking = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseProposalGeometrySource
{
    CadIrDeterministicRule = 0,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseProposalCodeState
{
    NotApplicable = 0,
    ExistingServicePrecheckRequired = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseRackProfileSource
{
    ExplicitSelected = 0,
    ExcelMapping = 1,
    HumanLocked = 2,
}

public sealed record WarehouseTemplateDefaultFactV1(
    string SourceRef,
    string FieldPath,
    string ValueToken);

public sealed record WarehouseRackLevelProfileV1(
    int LevelNo,
    int BottomZMillimeters,
    int ClearHeightMillimeters,
    int BinCount,
    int DepthCount,
    int CellWidthMillimeters,
    int CellDepthMillimeters,
    int BeamHeightMillimeters = 0,
    decimal? MaxLoadKilograms = null);

public sealed record WarehouseRackGenerationProfileV1(
    Guid ProfileVersionId,
    int RackWidthMillimeters,
    int RackDepthMillimeters,
    int RackHeightMillimeters,
    IReadOnlyList<WarehouseRackLevelProfileV1> Levels);

public sealed record WarehouseRackProfileBindingV1(
    string SourceRef,
    WarehouseRackProfileSource Source,
    WarehouseRackGenerationProfileV1 Profile);

public sealed record WarehouseDraftSynthesisRequestV1(
    Guid ModelVersionId,
    string RuleVersion,
    SpaceAiCadFeatureMinimizationV1 FeaturePackage,
    SpaceCadSemanticPreviewV1 RulePreview,
    ValidatedSemanticResult Ai,
    IReadOnlyList<SpaceAiCadLockedFactV1> LockedFacts,
    IReadOnlyList<WarehouseTemplateDefaultFactV1> TemplateDefaults,
    IReadOnlyList<WarehouseRackProfileBindingV1> RackProfiles);

public sealed record WarehouseFusionEvidenceV1(
    WarehouseFusionSource Source,
    string ValueToken,
    decimal Confidence,
    IReadOnlyList<string> EvidenceCodes);

public sealed record WarehouseResolvedFieldV1(
    string FieldPath,
    string ValueToken,
    WarehouseFusionSource WinningSource,
    decimal Confidence,
    IReadOnlyList<WarehouseFusionEvidenceV1> Evidence);

public sealed record WarehouseProposalRelationV1(
    WarehouseRelationType RelationType,
    Guid TargetLogicalId,
    decimal Confidence,
    IReadOnlyList<string> EvidenceCodes);

public sealed record WarehouseRackLevelDerivationV1(
    Guid LogicalId,
    int LevelNo,
    int BottomZMillimeters,
    int ClearHeightMillimeters,
    int BinCount,
    int DepthCount,
    int CellWidthMillimeters,
    int CellDepthMillimeters,
    int BeamHeightMillimeters,
    decimal? MaxLoadKilograms,
    long LocationCount,
    Guid FirstLocationLogicalId,
    Guid LastLocationLogicalId);

public sealed record WarehouseRackDerivationV1(
    Guid ProfileVersionId,
    string ProfileSha256,
    WarehouseRackProfileSource WinningSource,
    IReadOnlyList<WarehouseRackProfileSource> EvidenceSources,
    int RackWidthMillimeters,
    int RackDepthMillimeters,
    int RackHeightMillimeters,
    long LocationCount,
    IReadOnlyList<WarehouseRackLevelDerivationV1> Levels,
    string IdentityAlgorithm,
    bool RequiresExistingCodeServicePrecheck);

public sealed record WarehouseDraftProposalV1(
    Guid LogicalId,
    string SourceKey,
    string SourceRef,
    WarehouseSpaceType ObjectType,
    SpaceCadSemanticGeometryV1 Geometry,
    WarehouseProposalGeometrySource GeometrySource,
    WarehouseProposalCodeState CodeState,
    IReadOnlyList<WarehouseResolvedFieldV1> Fields,
    IReadOnlyList<WarehouseProposalRelationV1> Relations,
    decimal Confidence,
    WarehouseFusionConfidenceBand ConfidenceBand,
    bool RequiresHumanReview,
    bool CanBatchAccept,
    WarehouseRackDerivationV1? RackDerivation = null);

public sealed record WarehouseProposalIssueV1(
    string Code,
    WarehouseProposalIssueSeverity Severity,
    string? SourceRef = null,
    string? SourceKey = null,
    string? FieldPath = null,
    string? DetailToken = null);

public sealed record WarehouseDraftProposalSummaryV1(
    long ProposalCount,
    long HighConfidenceCount,
    long MediumConfidenceCount,
    long LowConfidenceCount,
    long RackCount,
    long DerivedRackLevelCount,
    long DerivedLocationCount,
    long InfoCount,
    long WarningCount,
    long BlockingCount,
    bool CanEnterReview,
    bool ReadyForApply);

public sealed record WarehouseDraftProposalSetV1(
    int SchemaVersion,
    bool IsReadOnlyPreview,
    bool DraftWritten,
    Guid TenantId,
    Guid ModelVersionId,
    Guid FloorLogicalId,
    string SourceSha256,
    string CoordinateTransformSha256,
    string SemanticPreviewSha256,
    string ProviderInputSha256,
    string ProviderOutputSha256,
    string SourceMapSha256,
    string RuleVersion,
    IReadOnlyList<WarehouseDraftProposalV1> Proposals,
    IReadOnlyList<WarehouseProposalIssueV1> Issues,
    WarehouseDraftProposalSummaryV1 Summary,
    string ProposalSetSha256);

public interface IWarehouseDraftSynthesizer
{
    Task<WarehouseDraftProposalSetV1> SynthesizeAsync(
        WarehouseDraftSynthesisRequestV1 request,
        CancellationToken cancellationToken = default);
}
