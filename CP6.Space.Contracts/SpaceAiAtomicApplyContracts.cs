using System.Text.Json;

namespace CP6.Space.Contracts;

public static class SpaceAiAtomicApplyContract
{
    public const int SchemaVersion = 1;
    public const int MaximumProposalCount = 100_000;
    public const int MaximumDerivedLocationCount = 1_000_000;
}

public static class SpaceAiRunRecoveryContract
{
    public const int SchemaVersion = 1;
    public const string SamePolicyMode = "SamePolicy";
    public const string RuleOnlyMode = "RuleOnly";
}

public sealed record CreateSpaceAiAtomicApplyRequest(
    long ExpectedContentRevision,
    string ExpectedRunRowVersion,
    string ReviewEtag);

public sealed record SpaceAiRunActionRequest(
    string ExpectedRunRowVersion);

public sealed record CreateSpaceAiGenerationRecoveryRequest(
    Guid BasedOnRunId,
    long ExpectedContentRevision,
    string ExpectedBasedOnRunRowVersion,
    string Mode);

public sealed record SpaceAiGenerationRunActionDto(
    int SchemaVersion,
    Guid RunId,
    Guid? ReplacementRunId,
    Guid? JobId,
    string Status,
    string RecoveryAction,
    bool Retryable,
    bool CancellationPending,
    bool IdempotentReplay);

public sealed record SpaceAiAppliedCountsDto(
    long Floors,
    long Zones,
    long Aisles,
    long Racks,
    long RackLevels,
    long Locations,
    long Elements,
    long Proposals);

public sealed record SpaceAiAtomicApplyAcceptedDto(
    int SchemaVersion,
    Guid RunId,
    Guid JobId,
    string Status,
    long ExpectedContentRevision,
    string ReviewEtag,
    bool IdempotentReplay);

public sealed record SpaceAiGenerationRunDto(
    int SchemaVersion,
    Guid RunId,
    Guid SiteId,
    Guid ModelVersionId,
    Guid? TargetFloorLogicalId,
    string Status,
    int Progress,
    long BaseContentRevision,
    long? AppliedContentRevision,
    Guid? ApplyJobId,
    string? ApplyJobStatus,
    string? ApplyPlanHash,
    JsonElement? AppliedCounts,
    string? FailureCode,
    string? FailureSummary,
    Guid? BasedOnRunId,
    string? DegradedReason,
    bool CancellationPending,
    bool Retryable,
    string RecoveryAction,
    string ApplyCommitState,
    string RowVersion);
