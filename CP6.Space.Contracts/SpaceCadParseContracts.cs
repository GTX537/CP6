namespace CP6.Space.Contracts;

public sealed record UploadSpaceCadSourceResponse(
    SpaceFileDto File,
    SpaceSourceDto Source,
    Guid? ScanJobId,
    string? JobStatusUrl,
    bool Reused);

public sealed record StartSpaceCadParseRequest(
    Guid FloorLogicalId,
    SpaceCadUnit ConfirmedUnit,
    decimal ConfirmedScaleToMillimeters,
    string CoordinateMetadataJson,
    string CoordinateTransformSha256,
    Guid MappingProfileId,
    int MappingProfileVersion,
    string MappingDefinitionSha256,
    string MappingPreviewSha256);

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
    IReadOnlyList<string> ChangeIds);

public sealed record ApplySpaceCadChangesetResponse(
    Guid CommandBatchId,
    long FloorRevision,
    long VersionContentRevision,
    long AppliedChangeCount,
    string WorkspaceSha256,
    bool IdempotentReplay);
