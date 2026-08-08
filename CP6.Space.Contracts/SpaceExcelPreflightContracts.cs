namespace CP6.Space.Contracts;

public sealed record UploadSpaceExcelSourceResponse(
    SpaceFileDto File,
    SpaceSourceDto Source,
    Guid? ScanJobId,
    string? JobStatusUrl,
    bool Reused);

public sealed record StartSpaceExcelPreflightRequest(
    Guid MappingProfileId,
    int MappingProfileVersion);

public sealed record StartSpaceExcelPreflightResponse(
    Guid JobId,
    string JobStatus,
    string JobStatusUrl,
    string PreviewUrl,
    string ErrorReportUrl,
    Guid MappingProfileId,
    int MappingProfileVersion,
    string MappingDefinitionHash,
    SpaceSourceDto Source,
    bool IdempotentReplay);

public sealed record SpaceExcelPreflightDto(
    Guid JobId,
    Guid ModelVersionId,
    Guid SourceId,
    string Status,
    string SourceState,
    Guid MappingProfileId,
    int MappingProfileVersion,
    string MappingDefinitionHash,
    string ParserVersion,
    bool CanConfirm,
    int InfoCount,
    int WarningCount,
    int BlockingCount,
    int SheetCount,
    int DataRowCount,
    int ValidRowCount,
    int ReturnedIssueCount,
    bool IssuesTruncated,
    string ErrorReportUrl,
    IReadOnlyList<SpaceExcelPreflightIssueDto> Issues);

public sealed record SpaceExcelPreflightIssueDto(
    Guid Id,
    string Severity,
    string Code,
    string? Sheet,
    int? Row,
    string? Column,
    string? TargetField,
    string MessageArgsJson,
    string? FixHint,
    DateTime CreatedAtUtc);
