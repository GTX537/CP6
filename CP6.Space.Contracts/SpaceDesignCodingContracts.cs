namespace CP6.Space.Contracts;

public static class SpaceDesignCodingContract
{
    public const int SchemaVersion = 1;
    public const string FillEmpty = "fill-empty";
    public const string Rebuild = "rebuild";
}

public sealed record SpaceLocationCodeSegmentDto(
    string Key,
    string Name,
    string Source,
    int Width,
    string Pad,
    int Start,
    int Step,
    string Separator,
    bool Upper,
    string FixedValue,
    bool Optional);

public sealed record PreviewSpaceLocationCodesRequest(
    int SchemaVersion,
    string Mode,
    Guid? ScopeZoneLogicalId,
    long ExpectedFloorRevision,
    long ExpectedContentRevision);

public sealed record SpaceLocationCodingRuleDto(
    Guid RuleId,
    string RuleName,
    int ScopeType,
    Guid? ScopeId,
    string RuleHash);

public sealed record SpaceLocationCodeProposalItemDto(
    Guid LocationLogicalId,
    Guid RackLogicalId,
    string RackCode,
    int ColumnNo,
    int LevelNo,
    int DepthNo,
    string? CurrentCode,
    string? ProposedCode,
    string Decision,
    string Reason,
    Guid? RuleId);

public sealed record PreviewSpaceLocationCodesResponse(
    int SchemaVersion,
    Guid ModelVersionId,
    Guid FloorLogicalId,
    string Mode,
    Guid? ScopeZoneLogicalId,
    long BaseFloorRevision,
    long BaseContentRevision,
    string ProposalHash,
    string RuleSetHash,
    int ChangedCount,
    int UnchangedCount,
    int ProtectedCount,
    IReadOnlyList<SpaceLocationCodingRuleDto> Rules,
    IReadOnlyList<SpaceLocationCodeProposalItemDto> Items);

public sealed record ApplySpaceLocationCodesRequest(
    int SchemaVersion,
    Guid CommandBatchId,
    Guid ClientInstanceId,
    Guid LeaseId,
    string Mode,
    Guid? ScopeZoneLogicalId,
    long ExpectedFloorRevision,
    long ExpectedContentRevision,
    string ProposalHash);

public sealed record ApplySpaceLocationCodesResponse(
    Guid CommandBatchId,
    long FloorRevision,
    long VersionContentRevision,
    string ProposalHash,
    int AppliedCount,
    IReadOnlyList<SpaceLocationCodeProposalItemDto> AppliedItems,
    bool IdempotentReplay);
