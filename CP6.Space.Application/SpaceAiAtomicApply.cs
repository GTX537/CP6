using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public interface ISpaceAiAtomicApplyService
{
    Task<SpaceAiAtomicApplyAcceptedDto> QueueAsync(
        Guid runId,
        CreateSpaceAiAtomicApplyRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceAiGenerationRunDto> GetRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}

public interface ISpaceAiGenerationRunService
{
    Task<SpaceAiGenerationRunAcceptedDto> CreateAsync(
        Guid versionId,
        CreateSpaceAiGenerationRunRequest request,
        string expectedVersionRowVersion,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface ISpaceAiRunRecoveryService
{
    Task<SpaceAiGenerationRunActionDto> CancelAsync(
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceAiGenerationRunActionDto> RetryAsync(
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceAiGenerationRunActionDto> DiscardAsync(
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceAiGenerationRunActionDto> ReconcileAsync(
        Guid runId,
        SpaceAiRunActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceAiGenerationRunActionDto> RecoverAsync(
        Guid versionId,
        CreateSpaceAiGenerationRecoveryRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceAiRunRecoveryState(
    bool Retryable,
    string RecoveryAction,
    string ApplyCommitState);

public static class SpaceAiRunRecoveryClassifier
{
    public static SpaceAiRunRecoveryState Classify(
        SpaceGenerationRun run,
        SpaceJob? job,
        bool commandBatchCommitted)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status == SpaceGenerationRunStatus.Succeeded ||
            commandBatchCommitted)
        {
            return new SpaceAiRunRecoveryState(
                Retryable: false,
                "open-updated-draft",
                "Committed");
        }
        if (run.Status == SpaceGenerationRunStatus.Stale)
        {
            return new SpaceAiRunRecoveryState(
                Retryable: false,
                "create-run-based-on-latest-draft",
                "NotCommitted");
        }
        if (run.Status == SpaceGenerationRunStatus.Cancelled)
        {
            return new SpaceAiRunRecoveryState(
                Retryable: false,
                "create-new-generation-run",
                "NotCommitted");
        }
        if (run.CancelPending)
        {
            return new SpaceAiRunRecoveryState(
                Retryable: false,
                "wait-for-safe-cancellation",
                "Pending");
        }
        if (run.Status == SpaceGenerationRunStatus.Applying)
        {
            var terminal = job?.Status is SpaceJobStatus.Succeeded or
                SpaceJobStatus.Failed or SpaceJobStatus.Cancelled or
                SpaceJobStatus.DeadLetter;
            return new SpaceAiRunRecoveryState(
                Retryable: false,
                terminal
                    ? "reconcile-apply-result"
                    : "wait-for-atomic-apply",
                terminal ? "Unknown" : "Pending");
        }
        if (run.Status == SpaceGenerationRunStatus.Failed)
        {
            var retryable = job is not null &&
                SpaceJobRetryPolicy.CanRetrySameInput(job);
            var providerFailure =
                job?.JobType == SpaceJobType.BuildScene &&
                string.Equals(
                    run.FailureCode,
                    SpaceErrorCodes.AiProviderUnavailable,
                    StringComparison.Ordinal);
            return new SpaceAiRunRecoveryState(
                retryable,
                providerFailure
                    ? "use-rule-only-or-retry-later"
                    : retryable
                        ? "retry-generation-run"
                        : "create-new-generation-run",
                "NotCommitted");
        }
        if (run.Status == SpaceGenerationRunStatus.AwaitingReview)
        {
            return new SpaceAiRunRecoveryState(
                Retryable: false,
                "complete-review-or-discard",
                "NotStarted");
        }
        return new SpaceAiRunRecoveryState(
            Retryable: false,
            "wait-for-generation-run",
            "NotStarted");
    }
}

public interface ISpaceGenerationApplyStepExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public static class SpaceGenerationApplyJobSteps
{
    public const string PrepareStaging = nameof(PrepareStaging);
    public const string ValidateStaging = nameof(ValidateStaging);
    public const string CommitDraft = nameof(CommitDraft);

    public static IReadOnlyList<string> All { get; } =
    [
        PrepareStaging,
        ValidateStaging,
        CommitDraft,
    ];
}

public sealed class SpaceGenerationApplyJobProcessor(
    ISpaceGenerationApplyStepExecutor executor) : ISpaceJobProcessor
{
    public const string Version = "space-generation-apply-v1";

    public SpaceJobType JobType => SpaceJobType.ApplyGeneration;
    public SpaceJobSubjectType SubjectType =>
        SpaceJobSubjectType.GenerationRun;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes =>
        SpaceGenerationApplyJobSteps.All;

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(execution, cancellationToken);
}

public interface ISpaceAiApplyFaultInjector
{
    void ThrowIfRequested(string checkpoint);
}

public sealed class NoOpSpaceAiApplyFaultInjector : ISpaceAiApplyFaultInjector
{
    public void ThrowIfRequested(string checkpoint)
    {
    }
}
