using System.Data;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class EfSpaceFileScanStateStore : ISpaceFileScanStateStore
{
    private readonly SpaceContext _context;
    private readonly ISpaceClock _clock;

    public EfSpaceFileScanStateStore(
        SpaceContext context,
        ISpaceClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<SpaceFileScanTarget> BeginScanAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken = default)
    {
        EnsureFileScanLease(lease);
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        try
        {
            var now = RequireUtcNow();
            var (job, attempt) = await LoadLeaseAsync(
                lease,
                now,
                cancellationToken);
            var file = await _context.Files.SingleOrDefaultAsync(
                           candidate => candidate.Id == lease.SubjectId,
                           cancellationToken)
                       ?? throw LeaseLost();
            if (!string.Equals(
                    file.Sha256,
                    lease.InputHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SpaceJobStateException(
                    "The file content no longer matches the scan Job input.");
            }
            if (job.CancellationRequestedAtUtc.HasValue)
            {
                if (file.State == SpaceFileState.Scanning)
                    file.DeferScan("SPACE_FILE_QUARANTINED");
                job.AcknowledgeCancellation(
                    lease.AttemptId,
                    lease.WorkerId,
                    now);
                attempt.Cancel(now);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new OperationCanceledException(
                    "The file-safety Job cancellation was acknowledged.");
            }

            file.BeginScanning();
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SpaceFileScanTarget(
                file.TenantId,
                file.Id,
                file.StorageKey,
                file.OriginalName,
                file.DetectedContentType ??
                throw new SpaceFileStateException(
                    "A quarantined file requires a detected content type."),
                file.Extension ??
                throw new SpaceFileStateException(
                    "A quarantined file requires an extension."),
                file.SizeBytes,
                file.Sha256!);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task FinishScanAsync(
        SpaceJobLease lease,
        FileSafetyResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureFileScanLease(lease);
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        try
        {
            var now = RequireUtcNow();
            var (job, attempt) = await LoadLeaseAsync(
                lease,
                now,
                cancellationToken);
            var file = await _context.Files.SingleOrDefaultAsync(
                           candidate => candidate.Id == lease.SubjectId,
                           cancellationToken)
                       ?? throw LeaseLost();
            if (file.State != SpaceFileState.Scanning)
            {
                throw new SpaceFileStateException(
                    "Only a scanning file can accept a safety result.");
            }

            if (job.CancellationRequestedAtUtc.HasValue)
            {
                file.DeferScan(
                    "SPACE_FILE_QUARANTINED",
                    result.ScanEngine,
                    result.SignatureVersion);
                job.AcknowledgeCancellation(
                    lease.AttemptId,
                    lease.WorkerId,
                    now);
                attempt.Cancel(now);
            }
            else if (result.Disposition == FileSafetyDisposition.Safe)
            {
                if (result.FailureKind.HasValue)
                {
                    throw new ArgumentException(
                        "A safe result cannot include a failure classification.",
                        nameof(result));
                }
                file.MarkClean(
                    result.ScanEngine,
                    result.SignatureVersion,
                    result.ResultCode);
                job.Complete(
                    lease.AttemptId,
                    lease.WorkerId,
                    now,
                    JsonSerializer.Serialize(
                        new
                        {
                            fileId = file.Id,
                            resultCode = result.ResultCode,
                            scanEngine = result.ScanEngine,
                            signatureVersion = result.SignatureVersion,
                        }));
                attempt.Succeed(now);
            }
            else
            {
                var failureKind = result.FailureKind ??
                                  throw new ArgumentException(
                                      "A non-safe result requires a failure classification.",
                                      nameof(result));
                if (result.Disposition == FileSafetyDisposition.Rejected)
                {
                    file.Reject(
                        result.ResultCode,
                        result.ScanEngine,
                        result.SignatureVersion);
                }
                else
                {
                    file.DeferScan(
                        result.ResultCode,
                        result.ScanEngine,
                        result.SignatureVersion);
                }

                var decision = SpaceJobRetryPolicy.DecideAutomatic(
                    failureKind,
                    job.AttemptCount,
                    job.MaxAttempts,
                    now);
                job.Fail(
                    lease.AttemptId,
                    lease.WorkerId,
                    failureKind,
                    result.ResultCode,
                    result.SanitizedSummary,
                    decision,
                    now);
                attempt.Fail(
                    failureKind,
                    result.ResultCode,
                    result.SanitizedSummary,
                    now);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw LeaseLost(exception);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<(SpaceJob Job, SpaceJobAttempt Attempt)> LoadLeaseAsync(
        SpaceJobLease lease,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (_context.CurrentTenantId != lease.TenantId)
            throw LeaseLost();
        var job = await _context.Jobs.SingleOrDefaultAsync(
                      candidate => candidate.Id == lease.JobId,
                      cancellationToken)
                  ?? throw LeaseLost();
        job.EnsureLease(lease.AttemptId, lease.WorkerId, nowUtc);
        var attempt = await _context.JobAttempts.SingleOrDefaultAsync(
                          candidate =>
                              candidate.Id == lease.AttemptId &&
                              candidate.JobId == lease.JobId &&
                              candidate.Outcome ==
                              SpaceJobAttemptOutcome.Running,
                          cancellationToken)
                      ?? throw LeaseLost();
        return (job, attempt);
    }

    private static void EnsureFileScanLease(SpaceJobLease lease)
    {
        if (lease.TenantId == Guid.Empty ||
            lease.JobType != SpaceJobType.FileScan ||
            lease.SubjectType != SpaceJobSubjectType.File ||
            lease.SubjectId == Guid.Empty)
        {
            throw LeaseLost();
        }
    }

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static SpaceJobLeaseLostException LeaseLost(Exception? inner = null)
    {
        const string message = "The worker lost its Space file-safety Job lease.";
        return inner is null
            ? new SpaceJobLeaseLostException(message)
            : new SpaceJobLeaseLostException(message, inner);
    }
}

public sealed class EfSpaceFileRetentionStore : ISpaceFileRetentionStore
{
    private readonly SpaceContext _context;

    public EfSpaceFileRetentionStore(SpaceContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Guid>> FindExpiredFileIdsAsync(
        Guid tenantId,
        DateTime nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantAndTime(tenantId, nowUtc);
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        return await _context.Files
            .AsNoTracking()
            .Where(file =>
                file.RetainUntilUtc.HasValue &&
                file.RetainUntilUtc <= nowUtc &&
                file.State != SpaceFileState.Uploading &&
                file.State != SpaceFileState.Scanning)
            .OrderBy(file => file.RetainUntilUtc)
            .ThenBy(file => file.Id)
            .Select(file => file.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpaceFileDeletionCandidate>>
        FindPendingContentDeletionAsync(
            Guid tenantId,
            int batchSize,
            CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        return await _context.Files
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId &&
                file.IsDeleted &&
                file.State == SpaceFileState.Deleted &&
                file.DeletionRequestedAtUtc.HasValue &&
                !file.ContentDeletedAtUtc.HasValue)
            .OrderBy(file => file.DeletionRequestedAtUtc)
            .ThenBy(file => file.Id)
            .Select(file => new SpaceFileDeletionCandidate(
                file.TenantId,
                file.Id,
                file.StorageKey))
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<SpaceFileTombstoneResult> TryTombstoneAsync(
        Guid tenantId,
        Guid fileId,
        DateTime nowUtc,
        bool requireExpired,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantAndTime(tenantId, nowUtc);
        if (fileId == Guid.Empty)
            throw new ArgumentException("File is required.", nameof(fileId));

        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        try
        {
            var file = await SpaceFileReferenceLock.LoadAsync(
                _context,
                tenantId,
                fileId,
                includeDeleted: true,
                cancellationToken);
            if (file is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new SpaceFileTombstoneResult(
                    SpaceFileTombstoneStatus.NotFound);
            }

            var candidate = new SpaceFileDeletionCandidate(
                file.TenantId,
                file.Id,
                file.StorageKey);
            if (file.IsDeleted || file.State == SpaceFileState.Deleted)
            {
                await transaction.CommitAsync(cancellationToken);
                return new SpaceFileTombstoneResult(
                    SpaceFileTombstoneStatus.AlreadyTombstoned,
                    file.ContentDeletedAtUtc.HasValue ? null : candidate);
            }
            if (requireExpired &&
                (!file.RetainUntilUtc.HasValue ||
                 file.RetainUntilUtc > nowUtc))
            {
                await transaction.CommitAsync(cancellationToken);
                return new SpaceFileTombstoneResult(
                    SpaceFileTombstoneStatus.NotExpired);
            }
            if (file.State is
                SpaceFileState.Uploading or
                SpaceFileState.Scanning)
            {
                await transaction.CommitAsync(cancellationToken);
                return new SpaceFileTombstoneResult(
                    SpaceFileTombstoneStatus.NotExpired);
            }

            var hasSource = await _context.Sources
                .IgnoreQueryFilters()
                .AnyAsync(
                    source =>
                        source.TenantId == tenantId &&
                        source.FileId == fileId &&
                        !source.IsDeleted,
                    cancellationToken);
            var hasArtifact = await _context.Artifacts
                .IgnoreQueryFilters()
                .AnyAsync(
                    artifact =>
                        artifact.TenantId == tenantId &&
                        artifact.FileId == fileId &&
                        !artifact.IsDeleted,
                    cancellationToken);
            var hasActiveScanJob = await _context.Jobs
                .IgnoreQueryFilters()
                .AnyAsync(
                    job =>
                        job.TenantId == tenantId &&
                        job.JobType == SpaceJobType.FileScan &&
                        job.SubjectType == SpaceJobSubjectType.File &&
                        job.SubjectId == fileId &&
                        (job.Status == SpaceJobStatus.Queued ||
                         job.Status == SpaceJobStatus.Running) &&
                        !job.IsDeleted,
                    cancellationToken);
            if (hasSource || hasArtifact || hasActiveScanJob)
            {
                await transaction.CommitAsync(cancellationToken);
                return new SpaceFileTombstoneResult(
                    SpaceFileTombstoneStatus.Referenced);
            }

            file.RequestDeletion(0, nowUtc);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SpaceFileTombstoneResult(
                SpaceFileTombstoneStatus.Tombstoned,
                candidate);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task MarkContentDeletedAsync(
        SpaceFileDeletionCandidate candidate,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureTenantAndTime(candidate.TenantId, nowUtc);
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        try
        {
            var file = await SpaceFileReferenceLock.LoadAsync(
                           _context,
                           candidate.TenantId,
                           candidate.FileId,
                           includeDeleted: true,
                           cancellationToken)
                       ?? throw new KeyNotFoundException(
                           "The Space file tombstone was not found.");
            if (!string.Equals(
                    file.StorageKey,
                    candidate.StorageKey,
                    StringComparison.Ordinal))
            {
                throw new SpaceFileStateException(
                    "The object key no longer matches the file tombstone.");
            }

            file.MarkContentDeleted(nowUtc);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private void EnsureTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty || tenantId != _context.CurrentTenantId)
        {
            throw new SpaceTenantScopeException(
                "The Space file operation crossed the verified tenant boundary.");
        }
    }

    private void EnsureTenantAndTime(Guid tenantId, DateTime nowUtc)
    {
        EnsureTenant(tenantId);
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", nameof(nowUtc));
    }
}

internal static class SpaceFileReferenceLock
{
    public static Task<SpaceFile?> LoadAsync(
        SpaceContext context,
        Guid tenantId,
        Guid fileId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var query = context.Files
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM [Space_File] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                 WHERE [TenantId] = {tenantId}
                   AND [Id] = {fileId}
                 """);
        if (includeDeleted)
            query = query.IgnoreQueryFilters();
        return query.SingleOrDefaultAsync(cancellationToken);
    }
}
