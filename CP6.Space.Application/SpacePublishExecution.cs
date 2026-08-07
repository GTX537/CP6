using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public interface ISpacePublishOrchestrator
{
    Task<CreateSpacePublishAttemptResponse> StartAsync(
        Guid versionId,
        CreateSpacePublishAttemptRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpacePublishAttemptDto> GetAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task<RetrySpacePublishAttemptResponse> RetryAsync(
        Guid attemptId,
        RetrySpacePublishAttemptRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface ISpacePublishJobExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public static class SpacePublishJobSteps
{
    public const string ExecutePublishSaga = nameof(ExecutePublishSaga);
    public const string ReconcilePublishSaga = nameof(ReconcilePublishSaga);
}

public sealed class SpacePublishJobProcessor(
    ISpacePublishJobExecutor executor) : ISpaceJobProcessor
{
    public const string Version = "space-publish-v2";
    public SpaceJobType JobType => SpaceJobType.Publish;
    public SpaceJobSubjectType SubjectType => SpaceJobSubjectType.PublishAttempt;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes => [SpacePublishJobSteps.ExecutePublishSaga];

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(execution, cancellationToken);
}

public sealed class SpacePublishReconciliationJobProcessor(
    ISpacePublishJobExecutor executor) : ISpaceJobProcessor
{
    public const string Version = "space-publish-reconcile-v1";
    public SpaceJobType JobType => SpaceJobType.Reconcile;
    public SpaceJobSubjectType SubjectType => SpaceJobSubjectType.PublishAttempt;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes => [SpacePublishJobSteps.ReconcilePublishSaga];

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(execution, cancellationToken);
}

public sealed record SpaceRuntimeActivationRequest(
    Guid AttemptId,
    Guid SiteId,
    Guid TargetVersionId,
    Guid? BaseVersionId,
    string PlanHash,
    Guid ActorId);

public sealed record SpaceRuntimeActivationResult(
    string MaterializedHash,
    int FloorCount,
    int ZoneCount,
    int AisleCount,
    int RackCount,
    int LocationCount,
    int ElementCount);

public interface ISpaceRuntimeMaterializer
{
    Task<SpaceRuntimeActivationResult> ActivateAsync(
        SpaceRuntimeActivationRequest request,
        CancellationToken cancellationToken = default);
}
