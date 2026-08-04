using System.Text.Json.Serialization;

namespace CP6.Space.Contracts;

public static class SpaceExcelCadMatchVersions
{
    public const int SchemaVersion = 1;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceExcelCadMatchDisposition
{
    New = 0,
    Update = 1,
    Unchanged = 2,
    Unmatched = 3,
    Conflict = 4,
    Error = 5,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceExcelCadMatchKeyKind
{
    CadSourceRef = 0,
    CadRackCode = 1,
    EditorSourceRef = 2,
    EditorRackCode = 3,
}

public sealed record SpaceExcelRackValuesV1(
    string? FloorCode,
    string? ZoneCode,
    string? RackCode,
    decimal? XMillimeters,
    decimal? YMillimeters,
    decimal? ZMillimeters,
    decimal? WidthMillimeters,
    decimal? DepthMillimeters,
    decimal? HeightMillimeters,
    decimal? RotationZDegrees,
    string? RackTemplateCode,
    string? LifecycleStatus);

public sealed record SpaceExcelEditorRackSnapshotV1(
    Guid LogicalId,
    Guid RevisionId,
    string RackCode,
    string? SourceRef,
    string FloorCode,
    string ZoneCode,
    int XMillimeters,
    int YMillimeters,
    int ZMillimeters,
    int WidthMillimeters,
    int DepthMillimeters,
    int HeightMillimeters,
    decimal RotationZDegrees,
    string LifecycleState);

public sealed record SpaceExcelEditorSnapshotV1(
    int SchemaVersion,
    bool IsReadOnlySnapshot,
    Guid TenantId,
    Guid ModelVersionId,
    Guid FloorLogicalId,
    string FloorCode,
    long ContentRevision,
    string? ContentHash,
    IReadOnlyList<SpaceExcelEditorRackSnapshotV1> Racks,
    string SnapshotSha256);

public sealed record SpaceExcelCadMatchKeyEvidenceV1(
    SpaceExcelCadMatchKeyKind Kind,
    string Value,
    string CandidateId);

public sealed record SpaceExcelCadRackMatchV1(
    string ExcelRowId,
    string SourceSheet,
    int RowNumber,
    SpaceExcelRackValuesV1 Values,
    SpaceExcelCadMatchDisposition Disposition,
    string? CadPreviewObjectId,
    Guid? EditorLogicalId,
    string? MatchedSourceRef,
    decimal? CadConfidence,
    SpaceCadConfidenceBand? CadConfidenceBand,
    IReadOnlyList<SpaceExcelCadMatchKeyEvidenceV1> KeyEvidence,
    IReadOnlyList<string> DifferenceFields,
    IReadOnlyList<string> ErrorCodes,
    SpaceCadDiagnosticLocationV1? Location,
    string MatchEvidenceSha256);

public sealed record SpaceExcelCadMatchSummaryV1(
    long ExcelRackRowCount,
    long NewCount,
    long UpdateCount,
    long UnchangedCount,
    long UnmatchedCount,
    long ConflictCount,
    long ErrorCount,
    long LocatableCount);

public sealed record SpaceExcelCadMatchPreviewV1(
    int SchemaVersion,
    bool IsReadOnlyPreview,
    Guid TenantId,
    Guid ModelVersionId,
    Guid ExcelSourceId,
    Guid PreflightJobId,
    Guid MappingProfileId,
    int MappingProfileVersion,
    string MappingDefinitionSha256,
    string WorkbookProjectionSha256,
    Guid FloorLogicalId,
    string FloorCode,
    string SemanticPreviewSha256,
    string DiagnosticIndexSha256,
    bool CadReadyForConfirmation,
    long CadBlockingCount,
    long ExcelBlockingFindingCount,
    long EditorContentRevision,
    string? EditorContentHash,
    string EditorSnapshotSha256,
    IReadOnlyList<SpaceExcelCadRackMatchV1> Rows,
    SpaceExcelCadMatchSummaryV1 Summary,
    bool CanConfirm,
    string MatchPreviewSha256);

public sealed record SpaceExcelCadMatchQueryV1(
    SpaceExcelCadMatchDisposition? Disposition = null,
    string? RackCode = null,
    string? SourceRef = null,
    bool OnlyLocatable = false,
    int Offset = 0,
    int Limit = SpaceExcelCadMatchVersions.DefaultPageSize);

public sealed record SpaceExcelCadMatchPageV1(
    int Offset,
    int Limit,
    long TotalCount,
    IReadOnlyList<SpaceExcelCadRackMatchV1> Items);
