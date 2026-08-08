using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public interface ISpaceHistoricalRepublishService
{
    Task<StartSpaceHistoricalRepublishResponse> StartAsync(
        Guid historicalVersionId,
        StartSpaceHistoricalRepublishRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceHistoricalRepublishDto> GetAsync(
        Guid republishId,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceHistoricalRepublishPublishContext(
    Guid RepublishId,
    Guid HistoricalVersionId,
    string Reason,
    Guid RequestedBy);

public interface ISpaceHistoricalRepublishPublishStarter
{
    Task<CreateSpacePublishAttemptResponse> StartHistoricalRepublishAsync(
        Guid versionId,
        CreateSpacePublishAttemptRequest request,
        string idempotencyKey,
        SpaceHistoricalRepublishPublishContext context,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceVersionSnapshotCloneRequest(
    Guid HistoricalVersionId,
    Guid TargetVersionId,
    string HistoricalContentHash,
    Guid RequestedBy,
    DateTime RequestedAtUtc);

public interface ISpaceVersionSnapshotCloner
{
    Task<SpaceVersionCloneCounts> CloneSnapshotAsync(
        SpaceVersionSnapshotCloneRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISpaceHistoricalRepublishJobExecutor
{
    Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default);
}

public static class SpaceHistoricalRepublishJobSteps
{
    public const string CloneHistoricalSnapshot =
        nameof(CloneHistoricalSnapshot);
    public const string ValidateHistoricalSnapshot =
        nameof(ValidateHistoricalSnapshot);
    public const string QueuePublish = nameof(QueuePublish);

    public static readonly IReadOnlyList<string> All =
    [
        CloneHistoricalSnapshot,
        ValidateHistoricalSnapshot,
        QueuePublish,
    ];
}

public sealed class SpaceHistoricalRepublishJobProcessor(
    ISpaceHistoricalRepublishJobExecutor executor) : ISpaceJobProcessor
{
    public const string Version = "space-historical-republish-v1";

    public SpaceJobType JobType => SpaceJobType.HistoricalRepublish;
    public SpaceJobSubjectType SubjectType =>
        SpaceJobSubjectType.HistoricalRepublish;
    public string ProcessorVersion => Version;
    public IReadOnlyList<string> StepCodes =>
        SpaceHistoricalRepublishJobSteps.All;

    public Task<SpaceJobStepOutput> ExecuteStepAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(execution, cancellationToken);
}
