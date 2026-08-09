using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceJobLease(
    Guid TenantId,
    Guid JobId,
    Guid AttemptId,
    int AttemptNo,
    string WorkerId,
    SpaceJobType JobType,
    SpaceJobSubjectType SubjectType,
    Guid SubjectId,
    string InputHash,
    DateTime LockExpiresAtUtc,
    byte[] RowVersion,
    bool CancellationRequested = false,
    long ProgressDone = 0,
    long ProgressTotal = 0);

public sealed record SpaceReusableCheckpoint(
    Guid StepId,
    Guid AttemptId,
    string StepCode,
    string CheckpointJson,
    string OutputHash);

public sealed record SpaceJobStepStartResult(
    Guid StepId,
    SpaceJobLease Lease);

public sealed record SpaceJobAttemptProgress(
    Guid AttemptId,
    int AttemptNo,
    string WorkerId,
    SpaceJobAttemptOutcome Outcome,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    SpaceJobFailureKind? FailureKind,
    string? ErrorCode);

public sealed record SpaceJobProgressSnapshot(
    Guid JobId,
    SpaceJobStatus Status,
    long ProgressDone,
    long ProgressTotal,
    string? ProgressStage,
    int AttemptCount,
    int MaxAttempts,
    DateTime? NextAttemptAtUtc,
    DateTime? LockExpiresAtUtc,
    bool CancellationRequested,
    string? LastErrorCode,
    string? LastErrorSummary,
    int OpenInfoCount,
    int OpenWarningCount,
    int OpenBlockingCount,
    IReadOnlyList<SpaceJobAttemptProgress> Attempts);

public interface ISpaceJobQueue
{
    Task<SpaceJob?> FindActiveAsync(
        Guid tenantId,
        SpaceJobType jobType,
        string businessKey,
        CancellationToken cancellationToken = default);

    Task<SpaceJob?> FindByIdAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<SpaceJobEnqueueResult> AddOrGetActiveAsync(
        SpaceJob job,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISpaceJobLeaseStore
{
    Task<SpaceJobLease?> TryClaimNextAsync(
        string workerId,
        string processorVersion,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<SpaceJobLease?> TryClaimNextAsync(
        string workerId,
        string processorVersion,
        TimeSpan leaseDuration,
        IReadOnlyCollection<SpaceJobType> supportedJobTypes,
        CancellationToken cancellationToken = default);

    Task<SpaceJobLease> RenewAsync(
        SpaceJobLease lease,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<SpaceJobLease> ReportProgressAsync(
        SpaceJobLease lease,
        long done,
        long total,
        string stage,
        CancellationToken cancellationToken = default);

    Task<SpaceJobStepStartResult> StartStepAsync(
        SpaceJobLease lease,
        int stepNo,
        string stepCode,
        CancellationToken cancellationToken = default);

    Task<SpaceJobLease> CompleteStepAsync(
        SpaceJobLease lease,
        Guid stepId,
        string checkpointJson,
        string outputHash,
        CancellationToken cancellationToken = default);

    Task<SpaceJobLease> ReuseStepAsync(
        SpaceJobLease lease,
        int stepNo,
        string stepCode,
        string checkpointJson,
        string outputHash,
        CancellationToken cancellationToken = default);

    Task<SpaceJobLease> FailStepAsync(
        SpaceJobLease lease,
        Guid stepId,
        CancellationToken cancellationToken = default);

    Task<SpaceReusableCheckpoint?> FindReusableCheckpointAsync(
        SpaceJobLease lease,
        string stepCode,
        CancellationToken cancellationToken = default);

    Task CompleteJobAsync(
        SpaceJobLease lease,
        string? resultSummaryJson = null,
        CancellationToken cancellationToken = default);

    Task FailJobAsync(
        SpaceJobLease lease,
        SpaceJobFailureKind failureKind,
        string errorCode,
        string sanitizedError,
        Guid? diagnosticArtifactId = null,
        string? resourceUsageJson = null,
        CancellationToken cancellationToken = default);

    Task AcknowledgeCancellationAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default);
}

public interface ISpaceJobProgressReader
{
    Task<SpaceJobProgressSnapshot?> GetAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
