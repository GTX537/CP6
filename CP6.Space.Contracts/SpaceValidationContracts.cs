namespace CP6.Space.Contracts;

public sealed record SpaceValidationIssueDto(
    Guid Id,
    Guid ValidationRunId,
    string Severity,
    string Category,
    string Code,
    Guid? SourceId,
    string? SourceRef,
    Guid? TargetLogicalId,
    string? FieldPath,
    string MessageArgsJson,
    string? SuggestedActionCode,
    Guid? GenerationRunId,
    Guid? GenerationProposalId,
    string EvidenceJson,
    DateTime CreatedAtUtc);

public sealed record SpaceValidationRunDto(
    Guid Id,
    Guid ModelVersionId,
    long ContentRevision,
    string ContentHash,
    string RuleSetVersion,
    string AdapterId,
    string CapabilityHash,
    string Status,
    int BlockingCount,
    int WarningCount,
    int InfoCount,
    DateTime RequestedAtUtc,
    Guid RequestedBy,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    Guid JobId,
    Guid CorrelationId,
    string? FailureCode,
    string? FailureSummary,
    string RowVersion,
    IReadOnlyList<SpaceValidationIssueDto> Issues);

public sealed record CreateSpaceValidationResponse(
    SpaceValidationRunDto Validation,
    bool Reused);
