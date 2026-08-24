namespace CP6.Space.Contracts;

public static class SpaceExcelCadApplyVersions
{
    public const int SchemaVersion = 2;
    public const int LegacySchemaVersion = 1;
    public const int PayloadSchemaVersion = 2;
}

public static class SpaceExcelCadCompensationDirections
{
    public const string Undo = "Undo";
    public const string Redo = "Redo";
}

public sealed record ConfirmSpaceExcelCadMatchRequest(
    bool Confirmed,
    Guid ArtifactId,
    string ArtifactPayloadSha256,
    long ExpectedContentRevision,
    Guid ClientInstanceId,
    Guid LeaseId,
    long ExpectedFloorRevision);

public sealed record ConfirmSpaceExcelCadMatchResponse(
    Guid MatchJobId,
    Guid ApplyJobId,
    Guid CommandBatchId,
    string JobStatus,
    string JobStatusUrl,
    bool IdempotentReplay);

public sealed record SpaceExcelCadApplyResultV1(
    int SchemaVersion,
    Guid MatchJobId,
    Guid ApplyJobId,
    Guid ArtifactId,
    string ArtifactPayloadSha256,
    Guid ModelVersionId,
    Guid ExcelSourceId,
    Guid FloorLogicalId,
    Guid CommandBatchId,
    long ExpectedFloorRevision,
    long ResultFloorRevision,
    long ExpectedContentRevision,
    long ResultContentRevision,
    long CreatedRackCount,
    long UpdatedRackCount,
    long UnchangedRackCount,
    Guid ConfirmedBy,
    DateTime ConfirmedAtUtc,
    DateTime AppliedAtUtc,
    string ApplyPlanSha256,
    string? HistorySha256 = null,
    int HistoryCommandCount = 0);

public sealed record SpaceExcelCadApplyDto(
    Guid MatchJobId,
    Guid ApplyJobId,
    Guid CommandBatchId,
    string JobStatus,
    long ExpectedContentRevision,
    SpaceExcelCadApplyResultV1? Result,
    bool IdempotentReplay,
    string? LastErrorCode,
    string? LastErrorSummary);

public sealed record CompensateSpaceExcelCadApplyRequest(
    int SchemaVersion,
    string Direction,
    Guid CommandBatchId,
    Guid ClientInstanceId,
    Guid LeaseId,
    long ExpectedFloorRevision,
    long ExpectedContentRevision,
    string HistorySha256);

public sealed record CompensateSpaceExcelCadApplyResponse(
    int SchemaVersion,
    Guid MatchJobId,
    Guid ApplyJobId,
    Guid CommandBatchId,
    string Direction,
    string HistorySha256,
    int HistoryCommandCount,
    long FloorRevision,
    long VersionContentRevision,
    bool IdempotentReplay);
