namespace CP6.Space.Contracts;

public sealed record CreateSpacePlanningScenarioBranchRequest(
    Guid BasePublishedVersionId,
    string Name);

public sealed record SpacePlanningScenarioBranchDto(
    Guid BranchId,
    Guid SiteId,
    Guid ModelId,
    Guid BasePublishedVersionId,
    string BaseVersionNo,
    Guid ScenarioVersionId,
    string ScenarioVersionNo,
    string Name,
    string BranchStatus,
    string ScenarioVersionStatus,
    Guid CloneJobId,
    string CloneJobStatus,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    string DefinitionVersion,
    bool ProductionIsolated,
    IReadOnlyList<string> Limitations);

public sealed record CreateSpacePlanningScenarioBranchResponse(
    string Outcome,
    SpacePlanningScenarioBranchDto Branch);

public sealed record SpacePlanningScenarioBranchListResponse(
    IReadOnlyList<SpacePlanningScenarioBranchDto> Items,
    bool IsTruncated);
