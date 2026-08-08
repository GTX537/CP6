namespace CP6.Space.Contracts;

public sealed record SpaceExcelMappingProfileDto(
    Guid Id,
    string Name,
    string Scope,
    int Version,
    bool IsReadOnly,
    string DefinitionHash,
    SpaceExcelMappingDefinitionDto Definition,
    Guid? BasedOnProfileId,
    int? BasedOnVersion,
    string? RowVersion,
    DateTime? CreatedAtUtc,
    Guid? CreatedBy);

public sealed record SaveSpaceExcelMappingProfileRequest(
    Guid? ProfileId,
    string Name,
    SpaceExcelMappingDefinitionDto Definition,
    string? ExpectedRowVersion = null,
    Guid? CopyFromProfileId = null,
    int? CopyFromVersion = null);

public sealed record SaveSpaceExcelMappingProfileResponse(
    SpaceExcelMappingProfileDto Profile,
    bool Created,
    bool IdempotentReplay);

public sealed record PreviewSpaceExcelMappingRequest(
    SpaceExcelMappingDefinitionDto Definition,
    IReadOnlyList<SpaceExcelHeaderSampleDto> Workbook);

public sealed record SpaceExcelHeaderSampleDto(
    string SheetName,
    IReadOnlyList<string> Headers);

public sealed record SpaceExcelMappingDefinitionDto(
    int SchemaVersion,
    string UnknownColumnPolicy,
    string EmptyValuePolicy,
    string DuplicateRowPolicy,
    IReadOnlyList<SpaceExcelSheetMappingDto> Sheets);

public sealed record SpaceExcelSheetMappingDto(
    string TargetSheet,
    string SourceSheet,
    string SheetMatchMode,
    int HeaderRow,
    int DataStartRow,
    IReadOnlyList<SpaceExcelColumnMappingDto> Columns);

public sealed record SpaceExcelColumnMappingDto(
    string TargetField,
    string? SourceHeader,
    string? SourceColumn,
    string DataType,
    string? Format,
    string? DefaultValue,
    bool IsBusinessKey,
    string? ReferenceTarget,
    IReadOnlyList<SpaceExcelEnumConversionDto>? EnumConversions,
    decimal? UnitConversionMultiplier);

public sealed record SpaceExcelEnumConversionDto(
    string SourceValue,
    string TargetValue);

public sealed record SpaceExcelMappingPreviewDto(
    bool CanSave,
    SpaceExcelMappingDefinitionDto NormalizedDefinition,
    IReadOnlyList<SpaceExcelSheetPreviewDto> Sheets,
    IReadOnlyList<SpaceExcelMappingIssueDto> Issues);

public sealed record SpaceExcelSheetPreviewDto(
    string TargetSheet,
    string SourceSheetPattern,
    string? MatchedSourceSheet,
    string Status,
    IReadOnlyList<SpaceExcelColumnPreviewDto> Columns,
    IReadOnlyList<string> UnknownHeaders);

public sealed record SpaceExcelColumnPreviewDto(
    string TargetField,
    bool Required,
    string? SourceHeader,
    string? SourceColumn,
    int? SourceColumnIndex,
    string Status);

public sealed record SpaceExcelMappingIssueDto(
    string Code,
    string Severity,
    string? Sheet,
    string? Column,
    string Message,
    string FixHint);
