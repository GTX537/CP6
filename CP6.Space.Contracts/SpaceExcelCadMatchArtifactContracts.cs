namespace CP6.Space.Contracts;

public static class SpaceExcelCadMatchArtifactVersions
{
    public const int SchemaVersion = 1;
    public const string ArtifactSchema = "space-excel-cad-match-v1";
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
}

public sealed record StartSpaceExcelCadMatchRequest(
    Guid ExcelSourceId,
    Guid PreflightJobId,
    Guid CadSourceId,
    Guid CadParseJobId,
    Guid FloorLogicalId,
    long ExpectedContentRevision);

public sealed record StartSpaceExcelCadMatchResponse(
    Guid JobId,
    string JobStatus,
    string JobStatusUrl,
    bool IdempotentReplay);

public sealed record SpaceExcelCadMatchArtifactV1(
    int SchemaVersion,
    bool IsAuthoritativeArtifact,
    Guid TenantId,
    Guid MatchJobId,
    Guid ModelVersionId,
    Guid ExcelSourceId,
    Guid PreflightJobId,
    Guid CadSourceId,
    Guid CadParseJobId,
    Guid CadPreviewSetArtifactId,
    Guid FloorLogicalId,
    long ExpectedContentRevision,
    Guid RequestedBy,
    DateTime RequestedAtUtc,
    SpaceExcelCadMatchPreviewV1 Preview,
    string ArtifactPayloadSha256);

public sealed record SpaceExcelCadMatchDto(
    Guid JobId,
    Guid ModelVersionId,
    string JobStatus,
    string ProcessorVersion,
    Guid ExcelSourceId,
    Guid PreflightJobId,
    Guid CadSourceId,
    Guid CadParseJobId,
    Guid FloorLogicalId,
    long ExpectedContentRevision,
    Guid? ArtifactId,
    string? ArtifactPayloadSha256,
    string? FileSha256,
    bool CanConfirm,
    SpaceExcelCadMatchSummaryV1? Summary,
    long TotalRowCount,
    int ReturnedRowCount,
    string? NextCursor,
    IReadOnlyList<SpaceExcelCadRackMatchV1> Rows,
    string? LastErrorCode,
    string? LastErrorSummary);
