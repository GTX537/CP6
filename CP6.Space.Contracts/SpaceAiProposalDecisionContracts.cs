using System.Text.Json;

namespace CP6.Space.Contracts;

public static class SpaceAiProposalDecisionContract
{
    public const int SchemaVersion = 1;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
    public const int MaximumBatchSize = 1_000;
}

public sealed record SpaceAiGenerationReviewSummaryDto(
    long TotalCount,
    long ProposedCount,
    long AcceptedCount,
    long RejectedCount,
    long ModifiedCount,
    long ObsoleteCount,
    long BlockingProposalCount,
    long OpenRunBlockingIssueCount,
    long OpenProposalBlockingIssueCount);

public sealed record SpaceAiGenerationReviewDto(
    int SchemaVersion,
    Guid RunId,
    Guid SiteId,
    Guid ModelVersionId,
    long BaseContentRevision,
    string Status,
    string RunRowVersion,
    string ReviewEtag,
    DateTimeOffset? ReviewCompletedAtUtc,
    bool ReviewCompleted,
    bool BatchAcceptEnabled,
    SpaceAiGenerationReviewSummaryDto Summary);

public sealed record SpaceAiProposalQuery(
    string? Status = null,
    string? ConfidenceBand = null,
    string? ProposalType = null,
    bool? HasBlockingIssue = null,
    string? Cursor = null,
    int Limit = SpaceAiProposalDecisionContract.DefaultPageSize);

public sealed record SpaceAiProposalDto(
    Guid ProposalId,
    Guid RunId,
    Guid ModelVersionId,
    long BaseContentRevision,
    string SourceHash,
    string SourceKey,
    string ProposalType,
    JsonElement SuggestedGeometry,
    JsonElement SuggestedAttributes,
    JsonElement SuggestedRelations,
    JsonElement SourceRefs,
    JsonElement Evidence,
    JsonElement FieldProvenance,
    decimal ConfidenceScore,
    string ConfidenceBand,
    string Status,
    bool HasBlockingIssue,
    JsonElement? HumanPatch,
    IReadOnlyList<string> LockedFields,
    Guid? AppliedLogicalId,
    string RowVersion,
    IReadOnlyList<string> AllowedPatchPaths);

public sealed record SpaceAiProposalPageDto(
    IReadOnlyList<SpaceAiProposalDto> Items,
    long TotalCount,
    int Limit,
    string ReviewEtag,
    string FilterHash,
    string? NextCursor);

public sealed record SpaceAiProposalIssueQuery(
    Guid? ProposalId = null,
    string? Severity = null,
    string? Status = null,
    string? IssueCode = null,
    string? Cursor = null,
    int Limit = SpaceAiProposalDecisionContract.DefaultPageSize);

public sealed record SpaceAiProposalIssueDto(
    Guid IssueId,
    Guid RunId,
    Guid? ProposalId,
    string Severity,
    string Code,
    string? SourceRef,
    string Status,
    string ResolutionKind,
    Guid? ResolutionDecisionId,
    JsonElement MessageArgs,
    string? SuggestedActionCode,
    DateTimeOffset CreatedAtUtc);

public sealed record SpaceAiProposalIssuePageDto(
    IReadOnlyList<SpaceAiProposalIssueDto> Items,
    long TotalCount,
    int Limit,
    string ReviewEtag,
    string FilterHash,
    string? NextCursor);

public sealed record SpaceAiProposalPatchOperationDto(
    string Op,
    string Path,
    JsonElement Value);

public sealed record CreateSpaceAiProposalDecisionRequest(
    Guid ProposalId,
    string Decision,
    string ExpectedProposalRowVersion,
    IReadOnlyList<SpaceAiProposalPatchOperationDto>? Patch,
    IReadOnlyList<string>? LockedFields,
    string? ReasonCode,
    string? Comment);

public sealed record SpaceAiProposalBatchSelectionDto(
    string? Status = null,
    string? ConfidenceBand = null,
    IReadOnlyList<string>? ProposalTypes = null,
    bool? HasBlockingIssue = null);

public sealed record CreateSpaceAiProposalBatchDecisionRequest(
    IReadOnlyList<Guid>? ProposalIds,
    SpaceAiProposalBatchSelectionDto? Selection,
    string Decision,
    string ReviewEtag,
    string? ReasonCode,
    string? Comment);

public sealed record SpaceAiProposalDecisionDto(
    Guid DecisionId,
    Guid DecisionBatchId,
    Guid RunId,
    Guid ProposalId,
    string Decision,
    JsonElement Before,
    JsonElement? After,
    IReadOnlyList<string> LockedFields,
    string? ReasonCode,
    string? Comment,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedBy);

public sealed record SpaceAiProposalDecisionResponse(
    string Outcome,
    Guid DecisionBatchId,
    IReadOnlyList<SpaceAiProposalDecisionDto> Decisions,
    SpaceAiGenerationReviewDto Review,
    bool IdempotentReplay);

public sealed record SpaceAiProposalDecisionHistoryDto(
    IReadOnlyList<SpaceAiProposalDecisionDto> Items,
    bool IsTruncated,
    string ReviewEtag);
