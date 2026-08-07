using System.Data;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class EfSpaceJobQueue : ISpaceJobQueue
{
    private readonly SpaceContext _context;

    public EfSpaceJobQueue(SpaceContext context)
    {
        _context = context;
    }

    public Task<SpaceJob?> FindActiveAsync(
        Guid tenantId,
        SpaceJobType jobType,
        string businessKey,
        CancellationToken cancellationToken = default) =>
        _context.Jobs.SingleOrDefaultAsync(
            job =>
                job.TenantId == tenantId &&
                job.JobType == jobType &&
                job.BusinessKey == businessKey &&
                (job.Status == SpaceJobStatus.Queued ||
                 job.Status == SpaceJobStatus.Running),
            cancellationToken);

    public Task<SpaceJob?> FindByIdAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default) =>
        _context.Jobs.SingleOrDefaultAsync(
            job => job.TenantId == tenantId && job.Id == jobId,
            cancellationToken);

    public async Task<SpaceJobEnqueueResult> AddOrGetActiveAsync(
        SpaceJob job,
        CancellationToken cancellationToken = default)
    {
        _context.Jobs.Add(job);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new SpaceJobEnqueueResult(job, Reused: false);
        }
        catch (DbUpdateException)
        {
            _context.Entry(job).State = EntityState.Detached;
            var existing = await FindActiveAsync(
                job.TenantId,
                job.JobType,
                job.BusinessKey,
                cancellationToken);
            if (existing is not null)
                return new SpaceJobEnqueueResult(existing, Reused: true);
            throw;
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await SpaceCloneReservationCleanup.ReleaseTrackedTerminalAsync(
            _context,
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class EfSpaceJobLeaseStore : ISpaceJobLeaseStore
{
    private const int ClaimConflictRetries = 5;

    private readonly SpaceContext _context;
    private readonly ISpaceClock _clock;

    public EfSpaceJobLeaseStore(
        SpaceContext context,
        ISpaceClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public Task<SpaceJobLease?> TryClaimNextAsync(
        string workerId,
        string processorVersion,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) =>
        TryClaimNextCoreAsync(
            workerId,
            processorVersion,
            leaseDuration,
            supportedJobTypes: null,
            cancellationToken);

    public Task<SpaceJobLease?> TryClaimNextAsync(
        string workerId,
        string processorVersion,
        TimeSpan leaseDuration,
        IReadOnlyCollection<SpaceJobType> supportedJobTypes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supportedJobTypes);
        if (supportedJobTypes.Count == 0)
        {
            throw new ArgumentException(
                "At least one supported Job type is required.",
                nameof(supportedJobTypes));
        }

        return TryClaimNextCoreAsync(
            workerId,
            processorVersion,
            leaseDuration,
            supportedJobTypes,
            cancellationToken);
    }

    private async Task<SpaceJobLease?> TryClaimNextCoreAsync(
        string workerId,
        string processorVersion,
        TimeSpan leaseDuration,
        IReadOnlyCollection<SpaceJobType>? supportedJobTypes,
        CancellationToken cancellationToken)
    {
        if (_context.CurrentTenantId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified Space tenant context is required.");
        var supportedTypes = supportedJobTypes?.Distinct().ToArray();

        for (var retry = 0; retry < ClaimConflictRetries; retry++)
        {
            _context.ChangeTracker.Clear();
            var now = RequireUtcNow();
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            try
            {
                var exhausted = await _context.Jobs
                    .Where(job =>
                        (supportedTypes == null ||
                         supportedTypes.Contains(job.JobType)) &&
                        job.Status == SpaceJobStatus.Running &&
                        job.LockExpiresAtUtc <= now &&
                        job.AttemptCount >= job.MaxAttempts)
                    .OrderBy(job => job.LockExpiresAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                if (exhausted is not null)
                {
                    await AbandonActiveAttemptAsync(
                        exhausted,
                        now,
                        "The final worker lease expired.",
                        cancellationToken);
                    exhausted.DeadLetterExpiredLease(now);
                    await SyncTerminalGenerationRunAsync(
                        exhausted,
                        cancellationToken);
                    await SpaceCloneReservationCleanup.ReleaseIfTerminalAsync(
                        _context,
                        exhausted,
                        cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                var job = await _context.Jobs
                    .Where(candidate =>
                        (supportedTypes == null ||
                         supportedTypes.Contains(candidate.JobType)) &&
                        candidate.AttemptCount < candidate.MaxAttempts &&
                        ((candidate.Status == SpaceJobStatus.Queued &&
                          candidate.NextAttemptAtUtc <= now) ||
                         (candidate.Status == SpaceJobStatus.Running &&
                          candidate.LockExpiresAtUtc <= now)))
                    .OrderByDescending(candidate => candidate.Priority)
                    .ThenBy(candidate => candidate.RequestedAtUtc)
                    .ThenBy(candidate => candidate.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (job is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }

                await AbandonActiveAttemptAsync(
                    job,
                    now,
                    "The worker lease expired and another worker took over.",
                    cancellationToken);
                var attempt = job.Claim(
                    workerId,
                    processorVersion,
                    now,
                    leaseDuration);
                _context.JobAttempts.Add(attempt);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return CreateLease(job, attempt);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
            }
        }

        return null;
    }

    public async Task<SpaceJobLease> RenewAsync(
        SpaceJobLease lease,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        job.RenewLease(lease.AttemptId, lease.WorkerId, now, leaseDuration);
        await SaveLeaseChangesAsync(cancellationToken);
        return RefreshLease(lease, job);
    }

    public async Task<SpaceJobLease> ReportProgressAsync(
        SpaceJobLease lease,
        long done,
        long total,
        string stage,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        job.ReportProgress(
            lease.AttemptId,
            lease.WorkerId,
            done,
            total,
            stage,
            now);
        await SaveLeaseChangesAsync(cancellationToken);
        return RefreshLease(lease, job);
    }

    public async Task<SpaceJobStepStartResult> StartStepAsync(
        SpaceJobLease lease,
        int stepNo,
        string stepCode,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        job.FenceCheckpoint(lease.AttemptId, lease.WorkerId, now);

        var existing = await _context.JobSteps.SingleOrDefaultAsync(
            step =>
                step.AttemptId == lease.AttemptId &&
                step.StepCode == stepCode,
            cancellationToken);
        if (existing is not null)
        {
            await SaveLeaseChangesAsync(cancellationToken);
            return new SpaceJobStepStartResult(
                existing.Id,
                RefreshLease(lease, job));
        }

        var step = SpaceJobStep.Start(
            lease.TenantId,
            lease.AttemptId,
            stepNo,
            stepCode,
            now);
        _context.JobSteps.Add(step);
        await SaveLeaseChangesAsync(cancellationToken);
        return new SpaceJobStepStartResult(
            step.Id,
            RefreshLease(lease, job));
    }

    public async Task<SpaceJobLease> CompleteStepAsync(
        SpaceJobLease lease,
        Guid stepId,
        string checkpointJson,
        string outputHash,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        job.FenceCheckpoint(lease.AttemptId, lease.WorkerId, now);
        var step = await _context.JobSteps.SingleOrDefaultAsync(
                       item =>
                           item.Id == stepId &&
                           item.AttemptId == lease.AttemptId,
                       cancellationToken)
                   ?? throw new KeyNotFoundException("The Space Job step was not found.");

        if (step.Status is SpaceJobStepStatus.Succeeded or SpaceJobStepStatus.Reused)
        {
            if (!string.Equals(
                    step.OutputHash,
                    outputHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    step.CheckpointJson,
                    checkpointJson,
                    StringComparison.Ordinal))
            {
                throw new SpaceJobStateException(
                    "A completed step cannot be overwritten with different output.");
            }
        }
        else
        {
            step.Complete(checkpointJson, outputHash, now);
        }

        await SaveLeaseChangesAsync(cancellationToken);
        return RefreshLease(lease, job);
    }

    public async Task<SpaceJobLease> ReuseStepAsync(
        SpaceJobLease lease,
        int stepNo,
        string stepCode,
        string checkpointJson,
        string outputHash,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        job.FenceCheckpoint(lease.AttemptId, lease.WorkerId, now);
        var existing = await _context.JobSteps.SingleOrDefaultAsync(
            step =>
                step.AttemptId == lease.AttemptId &&
                step.StepCode == stepCode,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != SpaceJobStepStatus.Reused ||
                existing.StepNo != stepNo ||
                !string.Equals(
                    existing.CheckpointJson,
                    checkpointJson,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.OutputHash,
                    outputHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SpaceJobStateException(
                    "A Job step cannot be reused with different output.");
            }

            await SaveLeaseChangesAsync(cancellationToken);
            return RefreshLease(lease, job);
        }

        _context.JobSteps.Add(
            SpaceJobStep.Reuse(
                lease.TenantId,
                lease.AttemptId,
                stepNo,
                stepCode,
                checkpointJson,
                outputHash,
                now));
        await SaveLeaseChangesAsync(cancellationToken);
        return RefreshLease(lease, job);
    }

    public async Task<SpaceJobLease> FailStepAsync(
        SpaceJobLease lease,
        Guid stepId,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        job.FenceCheckpoint(lease.AttemptId, lease.WorkerId, now);
        var step = await _context.JobSteps.SingleOrDefaultAsync(
                       item =>
                           item.Id == stepId &&
                           item.AttemptId == lease.AttemptId,
                       cancellationToken)
                   ?? throw new KeyNotFoundException(
                       "The Space Job step was not found.");
        if (step.Status == SpaceJobStepStatus.Running)
            step.Fail(now);
        else if (step.Status != SpaceJobStepStatus.Failed)
        {
            throw new SpaceJobStateException(
                "A completed Job step cannot be failed.");
        }

        await SaveLeaseChangesAsync(cancellationToken);
        return RefreshLease(lease, job);
    }

    public async Task<SpaceReusableCheckpoint?> FindReusableCheckpointAsync(
        SpaceJobLease lease,
        string stepCode,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        var activeAttempt = await _context.JobAttempts
            .AsNoTracking()
            .SingleAsync(
                attempt => attempt.Id == lease.AttemptId,
                cancellationToken);

        return await (
                from step in _context.JobSteps.AsNoTracking()
                join attempt in _context.JobAttempts.AsNoTracking()
                    on new { step.TenantId, Id = step.AttemptId }
                    equals new { attempt.TenantId, attempt.Id }
                where (attempt.JobId == job.Id ||
                       (job.RetryOfJobId.HasValue &&
                        attempt.JobId == job.RetryOfJobId.Value)) &&
                      attempt.Id != activeAttempt.Id &&
                      attempt.InputHash == activeAttempt.InputHash &&
                      attempt.ProcessorVersion == activeAttempt.ProcessorVersion &&
                      step.StepCode == stepCode &&
                      (step.Status == SpaceJobStepStatus.Succeeded ||
                       step.Status == SpaceJobStepStatus.Reused) &&
                      step.CheckpointJson != null &&
                      step.OutputHash != null
                orderby (attempt.JobId == job.Id) descending,
                    attempt.AttemptNo descending,
                    step.StepNo descending
                select new SpaceReusableCheckpoint(
                    step.Id,
                    attempt.Id,
                    step.StepCode,
                    step.CheckpointJson!,
                    step.OutputHash!))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task CompleteJobAsync(
        SpaceJobLease lease,
        string? resultSummaryJson = null,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        var attempt = await LoadAttemptAsync(lease, cancellationToken);
        job.Complete(
            lease.AttemptId,
            lease.WorkerId,
            now,
            resultSummaryJson);
        attempt.Succeed(now);
        await SaveLeaseChangesAsync(cancellationToken);
    }

    public async Task FailJobAsync(
        SpaceJobLease lease,
        SpaceJobFailureKind failureKind,
        string errorCode,
        string sanitizedError,
        Guid? diagnosticArtifactId = null,
        string? resourceUsageJson = null,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        var attempt = await LoadAttemptAsync(lease, cancellationToken);
        var decision = SpaceJobRetryPolicy.DecideAutomatic(
            failureKind,
            job.AttemptCount,
            job.MaxAttempts,
            now);
        job.Fail(
            lease.AttemptId,
            lease.WorkerId,
            failureKind,
            errorCode,
            sanitizedError,
            decision,
            now);
        if (job.IsTerminal)
        {
            await SyncTerminalGenerationRunAsync(
                job,
                cancellationToken);
        }
        attempt.Fail(
            failureKind,
            errorCode,
            sanitizedError,
            now,
            diagnosticArtifactId,
            resourceUsageJson);
        await SaveLeaseChangesAsync(cancellationToken);
    }

    public async Task AcknowledgeCancellationAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default)
    {
        var now = RequireUtcNow();
        var job = await LoadLeaseJobAsync(lease, now, cancellationToken);
        var attempt = await LoadAttemptAsync(lease, cancellationToken);
        job.AcknowledgeCancellation(
            lease.AttemptId,
            lease.WorkerId,
            now);
        await SyncCancelledGenerationRunAsync(job, now, cancellationToken);
        attempt.Cancel(now);
        await SaveLeaseChangesAsync(cancellationToken);
    }

    private async Task<SpaceJob> LoadLeaseJobAsync(
        SpaceJobLease lease,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        if (lease.TenantId != _context.CurrentTenantId)
            throw LeaseLost();

        var job = await _context.Jobs.SingleOrDefaultAsync(
            candidate => candidate.Id == lease.JobId,
            cancellationToken);
        if (job is null)
            throw LeaseLost();

        job.EnsureLease(lease.AttemptId, lease.WorkerId, nowUtc);
        return job;
    }

    private Task<SpaceJobAttempt> LoadAttemptAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken) =>
        _context.JobAttempts.SingleAsync(
            attempt =>
                attempt.Id == lease.AttemptId &&
                attempt.JobId == lease.JobId &&
                attempt.Outcome == SpaceJobAttemptOutcome.Running,
            cancellationToken);

    private async Task AbandonActiveAttemptAsync(
        SpaceJob job,
        DateTime nowUtc,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!job.ActiveAttemptId.HasValue)
            return;

        var previous = await _context.JobAttempts.SingleOrDefaultAsync(
            attempt =>
                attempt.Id == job.ActiveAttemptId.Value &&
                attempt.Outcome == SpaceJobAttemptOutcome.Running,
            cancellationToken);
        previous?.Abandon(nowUtc, reason);
    }

    private async Task SyncTerminalGenerationRunAsync(
        SpaceJob job,
        CancellationToken cancellationToken)
    {
        if (job.JobType is not (
            SpaceJobType.BuildScene or SpaceJobType.ApplyGeneration))
        {
            return;
        }
        var run = await _context.GenerationRuns.SingleOrDefaultAsync(
            item => item.JobId == job.Id || item.ApplyJobId == job.Id,
            cancellationToken);
        if (run is null || run.Status is not (
            SpaceGenerationRunStatus.Queued or
            SpaceGenerationRunStatus.Preparing or
            SpaceGenerationRunStatus.Inferring or
            SpaceGenerationRunStatus.Validating or
            SpaceGenerationRunStatus.Applying))
        {
            return;
        }
        run.MarkFailed(
            job.LastErrorCode ?? SpaceErrorCodes.JobProcessorFailed,
            job.LastErrorSummary ??
                "The generation Job failed without changing Draft.");
    }

    private async Task SyncCancelledGenerationRunAsync(
        SpaceJob job,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (job.JobType is not (
            SpaceJobType.BuildScene or SpaceJobType.ApplyGeneration))
        {
            return;
        }
        var run = await _context.GenerationRuns.SingleOrDefaultAsync(
            item => item.JobId == job.Id || item.ApplyJobId == job.Id,
            cancellationToken);
        if (run is null || run.CancelRequestedAtUtc is null ||
            run.Status is SpaceGenerationRunStatus.Succeeded or
            SpaceGenerationRunStatus.Cancelled)
        {
            return;
        }
        run.CompleteCancellation(nowUtc);
        var proposals = await _context.GenerationProposals
            .Where(item => item.RunId == run.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var proposal in proposals.Where(item =>
                     item.Status is not (
                         SpaceGenerationProposalStatus.Applied or
                         SpaceGenerationProposalStatus.Obsolete)))
        {
            proposal.MarkObsolete();
        }
    }

    private async Task SaveLeaseChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await SpaceCloneReservationCleanup.ReleaseTrackedTerminalAsync(
                _context,
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _context.ChangeTracker.Clear();
            throw LeaseLost(exception);
        }
    }

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static SpaceJobLease CreateLease(
        SpaceJob job,
        SpaceJobAttempt attempt) =>
        new(
            job.TenantId,
            job.Id,
            attempt.Id,
            attempt.AttemptNo,
            attempt.WorkerId,
            job.JobType,
            job.SubjectType,
            job.SubjectId,
            job.InputHash,
            job.LockExpiresAtUtc!.Value,
            job.RowVersion.ToArray(),
            job.CancellationRequestedAtUtc.HasValue,
            job.ProgressDone,
            job.ProgressTotal);

    private static SpaceJobLease RefreshLease(
        SpaceJobLease lease,
        SpaceJob job) =>
        lease with
        {
            LockExpiresAtUtc = job.LockExpiresAtUtc!.Value,
            RowVersion = job.RowVersion.ToArray(),
            CancellationRequested =
                job.CancellationRequestedAtUtc.HasValue,
            ProgressDone = job.ProgressDone,
            ProgressTotal = job.ProgressTotal,
        };

    private static SpaceJobLeaseLostException LeaseLost(Exception? inner = null)
    {
        const string message = "The worker lost its Space Job lease.";
        return inner is null
            ? new SpaceJobLeaseLostException(message)
            : new SpaceJobLeaseLostException(message, inner);
    }
}

public sealed class EfSpaceJobProgressReader : ISpaceJobProgressReader
{
    private readonly SpaceContext _context;

    public EfSpaceJobProgressReader(SpaceContext context)
    {
        _context = context;
    }

    public async Task<SpaceJobProgressSnapshot?> GetAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _context.Jobs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.TenantId == tenantId &&
                    candidate.Id == jobId,
                cancellationToken);
        if (job is null)
            return null;

        var attempts = await _context.JobAttempts
            .AsNoTracking()
            .Where(attempt => attempt.JobId == jobId)
            .OrderBy(attempt => attempt.AttemptNo)
            .Select(attempt => new SpaceJobAttemptProgress(
                attempt.Id,
                attempt.AttemptNo,
                attempt.WorkerId,
                attempt.Outcome,
                attempt.StartedAtUtc,
                attempt.FinishedAtUtc,
                attempt.FailureKind,
                attempt.ErrorCode))
            .ToListAsync(cancellationToken);
        var openIssues = _context.Issues
            .AsNoTracking()
            .Where(issue =>
                issue.JobId == jobId &&
                issue.Status == SpaceIssueStatus.Open);

        return new SpaceJobProgressSnapshot(
            job.Id,
            job.Status,
            job.ProgressDone,
            job.ProgressTotal,
            job.ProgressStage,
            job.AttemptCount,
            job.MaxAttempts,
            job.Status == SpaceJobStatus.Queued ? job.NextAttemptAtUtc : null,
            job.LockExpiresAtUtc,
            job.CancellationRequestedAtUtc.HasValue,
            job.LastErrorCode,
            job.LastErrorSummary,
            await openIssues.CountAsync(
                issue => issue.Severity == SpaceIssueSeverity.Info,
                cancellationToken),
            await openIssues.CountAsync(
                issue => issue.Severity == SpaceIssueSeverity.Warning,
                cancellationToken),
            await openIssues.CountAsync(
                issue => issue.Severity == SpaceIssueSeverity.Blocking,
                cancellationToken),
            attempts);
    }
}
