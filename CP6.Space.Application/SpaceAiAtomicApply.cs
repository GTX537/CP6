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
