namespace CP6.Space.Contracts;

public sealed record SpacePage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor);

public sealed record SpaceModelDto(
    Guid Id,
    Guid SiteId,
    string Mode,
    string CutoverState,
    Guid? ActiveDraftVersionId,
    Guid? CurrentPublishedVersionId,
    string RowVersion);

public sealed record SpaceVersionDto(
    Guid Id,
    Guid ModelId,
    Guid SiteId,
    string VersionNo,
    string Name,
    string Status,
    Guid? BasedOnVersionId,
    long ContentRevision,
    string? ContentHash,
    string? ValidatedHash,
    DateTime? PublishedAtUtc,
    string RowVersion,
    string Purpose = "Production");

public sealed record CreateSpaceVersionRequest(
    string Name,
    Guid? BasedOnVersionId,
    string CreateMode = "PublishedVersion");

public sealed record CreateSpaceVersionResponse(
    Guid Id,
    Guid SiteId,
    string VersionNo,
    string Status,
    string RowVersion,
    Guid JobId,
    string JobStatusUrl,
    bool IdempotentReplay);

public sealed record SpaceSourceDto(
    Guid Id,
    Guid ModelVersionId,
    string SourceType,
    Guid? FileId,
    string DisplayName,
    string Sha256,
    string State,
    string? ParserVersion,
    Guid? MappingProfileId,
    long? MappingProfileVersion,
    string? Unit,
    decimal? ScaleToMillimeters,
    string RowVersion);

public sealed record CreateSpaceSourceRequest(
    Guid FileId,
    string SourceType,
    string DisplayName);

public sealed record CreateSpaceSourceResponse(
    SpaceSourceDto Source,
    bool IdempotentReplay);

public sealed record SpaceFileDto(
    Guid Id,
    string OriginalName,
    string? ContentType,
    string? Extension,
    long SizeBytes,
    string? Sha256,
    string State,
    string? ScanResultCode,
    string RowVersion);

public sealed record UploadSpaceUnderlayResponse(
    SpaceFileDto File,
    SpaceSourceDto Source,
    Guid? ScanJobId,
    string? JobStatusUrl,
    bool Reused);

public sealed record AttachSpaceUnderlayRequest(
    Guid SourceId,
    long ExpectedFloorRevision);

public sealed record AttachSpaceUnderlayResponse(
    SpaceSceneFloorDto Floor,
    bool IdempotentReplay);

public sealed record SpaceUnderlayCalibrationPointDto(
    decimal PixelX,
    decimal PixelY,
    int WorldX,
    int WorldY);

public sealed record SaveSpaceUnderlayCalibrationRequest(
    Guid FloorLogicalId,
    int PageNumber,
    int PixelWidth,
    int PixelHeight,
    SpaceUnderlayCalibrationPointDto Point1,
    SpaceUnderlayCalibrationPointDto Point2,
    SpaceUnderlayCalibrationPointDto ValidationPoint,
    long ExpectedFloorRevision);

public sealed record SpaceUnderlayCalibrationDto(
    Guid Id,
    Guid ModelVersionId,
    Guid FloorLogicalId,
    Guid SourceId,
    int PageNumber,
    int PixelWidth,
    int PixelHeight,
    SpaceUnderlayCalibrationPointDto Point1,
    SpaceUnderlayCalibrationPointDto Point2,
    SpaceUnderlayCalibrationPointDto ValidationPoint,
    decimal MillimetersPerPixel,
    int OffsetX,
    int OffsetY,
    decimal RotationZ,
    decimal ValidationErrorMillimeters,
    decimal ErrorThresholdMillimeters,
    DateTime CreatedAtUtc,
    Guid? CreatedBy);

public sealed record SaveSpaceUnderlayCalibrationResponse(
    SpaceSceneFloorDto Floor,
    SpaceUnderlayCalibrationDto Calibration,
    bool IdempotentReplay);

public sealed record SpaceJobDto(
    Guid Id,
    string JobType,
    string SubjectType,
    Guid SubjectId,
    string Status,
    long ProgressDone,
    long ProgressTotal,
    string? ProgressStage,
    int AttemptCount,
    int MaxAttempts,
    DateTime? NextAttemptAtUtc,
    DateTime? LockExpiresAtUtc,
    bool CancellationRequested,
    string? LastErrorCode,
    string? LastErrorSummary,
    int OpenInfoCount,
    int OpenWarningCount,
    int OpenBlockingCount,
    DateTime RequestedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    string? ResultSummaryJson,
    string RowVersion);

public sealed record SpaceIssueDto(
    Guid Id,
    Guid? ModelVersionId,
    Guid? SourceId,
    Guid? JobId,
    string Severity,
    string Code,
    string? SourceRef,
    Guid? TargetLogicalId,
    string MessageArgsJson,
    string? SuggestedActionCode,
    string Status,
    Guid? ResolutionCommandBatchId,
    Guid? AcknowledgedBy,
    DateTime? AcknowledgedAtUtc,
    string? AcknowledgementReason,
    DateTime CreatedAtUtc);
