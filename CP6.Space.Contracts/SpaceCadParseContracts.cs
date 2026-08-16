using System.ComponentModel.DataAnnotations;

namespace CP6.Space.Contracts;

public sealed record UploadSpaceCadSourceResponse(
    SpaceFileDto File,
    SpaceSourceDto Source,
    Guid? ScanJobId,
    string? JobStatusUrl,
    bool Reused);

public sealed record StartSpaceCadParseRequest(
    Guid PreparationId,
    Guid FloorLogicalId,
    SpaceCadUnit ConfirmedUnit,
    decimal ConfirmedScaleToMillimeters,
    string CoordinateMetadataJson,
    string CoordinateTransformSha256,
    Guid MappingProfileId,
    int MappingProfileVersion,
    string MappingDefinitionSha256,
    string MappingPreviewSha256);

public sealed record PreviewSpaceCadPreparationRequest(
    Guid FloorLogicalId,
    SpaceCadUnit ConfirmedUnit,
    SpaceCadPointV1 SourceOriginInSourceUnits,
    SpaceCadMillimeterPointV1 FloorOriginMillimeters,
    decimal RotationZDegrees,
    Guid MappingProfileId,
    int MappingProfileVersion,
    IReadOnlyList<SpaceCadLayerMappingOverrideV1> LayerOverrides);

public sealed record SpaceCadMappingProfileSummaryDto(
    Guid ProfileId,
    int Version,
    string Name,
    string Scope,
    bool IsEnabled,
    string DefinitionSha256,
    int RuleCount);

public sealed record SpaceCadPreparationStatusDto(
    Guid SourceId,
    string SourceState,
    string FileState,
    bool ReadyForPreparation,
    string? BlockingCode);

public sealed record SpaceCadPreparationInventoryDto(
    SpaceCadInventorySummaryV1 Summary,
    IReadOnlyList<SpaceCadLayerInventoryV1> Layers,
    IReadOnlyList<SpaceCadPreparationBlockInventoryDto> Blocks);

public sealed record SpaceCadPreparationBlockInventoryDto(
    string BlockId,
    string Name,
    bool IsDefined,
    bool IsExternalReference,
    long DefinitionEntityCount,
    long ReferenceCount,
    long AttributedReferenceCount,
    IReadOnlyList<SpaceCadBlockAttributeInventoryV1> Attributes,
    SpaceCadBoundsV1? ReferenceBounds);

public sealed record PreviewSpaceCadPreparationResponse(
    Guid? PreparationId,
    DateTime? ExpiresAtUtc,
    long BaseContentRevision,
    string? BaseContentHash,
    bool ReadyForParsing,
    SpaceCadCoordinateAnalysisV1 CoordinateAnalysis,
    SpaceCadCoordinateMetadataV1 CoordinateMetadata,
    IReadOnlyList<SpaceCadConversionIssueV1> CoordinateIssues,
    SpaceCadInventorySummaryV1? InventorySummary,
    SpaceCadPreparationInventoryDto? Inventory,
    SpaceCadMappingProfileSummaryDto MappingProfile,
    SpaceCadMappingPreviewV1? MappingPreview,
    SpaceCadSemanticPreviewV1? SemanticPreview,
    StartSpaceCadParseRequest? StartRequest);

public sealed record StartSpaceCadParseResponse(
    Guid JobId,
    string JobStatus,
    string JobStatusUrl,
    string ParseStatusUrl,
    SpaceSourceDto Source,
    bool IdempotentReplay);

public sealed record SpaceCadParseArtifactDto(
    Guid ArtifactId,
    Guid FileId,
    string ArtifactType,
    string SchemaVersion,
    string Sha256,
    long SizeBytes);

public sealed record SpaceCadParseDto(
    Guid JobId,
    Guid ModelVersionId,
    Guid SourceId,
    string Status,
    string SourceState,
    string ParserVersion,
    Guid FloorLogicalId,
    string CoordinateTransformSha256,
    Guid MappingProfileId,
    int MappingProfileVersion,
    string MappingDefinitionSha256,
    string MappingPreviewSha256,
    Guid? RetryOfJobId,
    bool CancellationRequested,
    string? LastErrorCode,
    string? LastErrorSummary,
    IReadOnlyList<SpaceCadParseArtifactDto> Artifacts);

public sealed record SpaceCadParseActionResponse(
    Guid JobId,
    string Status,
    string JobStatusUrl,
    string ParseStatusUrl,
    bool IdempotentReplay = false);

public sealed record ApplySpaceCadChangesetRequest(
    Guid CommandBatchId,
    Guid ClientInstanceId,
    Guid LeaseId,
    long ExpectedFloorRevision,
    long ExpectedContentRevision,
    string? ExpectedContentHash,
    string WorkspaceSha256,
    [property: MinLength(1)]
    [property: MaxLength(SpaceCadReviewWorkspaceVersions.MaximumApplyChanges)]
    IReadOnlyList<string> ChangeIds);

public sealed record ApplySpaceCadChangesetResponse(
    Guid CommandBatchId,
    long FloorRevision,
    long VersionContentRevision,
    long AppliedChangeCount,
    string WorkspaceSha256,
    bool IdempotentReplay,
    IReadOnlyList<SpaceSavedElementCommandDto> UndoCommands,
    IReadOnlyList<SpaceSavedElementCommandDto> RedoCommands);

public sealed record SpaceSavedElementCommandDto(
    string Type,
    Guid TargetLogicalId,
    SpaceUpdateElementPropertiesDto? UpdateProperties = null);
