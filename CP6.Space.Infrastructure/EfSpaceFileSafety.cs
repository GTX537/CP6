using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Space.Application;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class EfSpaceFileScanStateStore : ISpaceFileScanStateStore
{
    private const string TenantInactiveCode = "SPACE_FILE_SCAN_TENANT_INACTIVE";
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
        if (await ReadCompletedScanAsync(lease, cancellationToken) is { } completed)
            return completed;

        try
        {
            return await BeginOwnedScanAsync(lease, cancellationToken);
        }
        catch (Exception error) when (MayHaveCompleted(error))
        {
            if (await ReadCompletedScanAsync(lease, cancellationToken) is { } raced)
                return raced;
            throw;
        }
    }

    private async Task<SpaceFileScanTarget> BeginOwnedScanAsync(SpaceJobLease lease, CancellationToken cancellationToken)
    {
        var (transaction, tenantExpiry) = await BeginWriteAsync(cancellationToken);
        await using var owned = transaction;
        var scan = await LoadLiveScanAsync(lease, cancellationToken);
        if (scan.Job.CancellationRequestedAtUtc.HasValue)
        {
            if (scan.File.State == SpaceFileState.Scanning)
                scan.File.DeferScan("SPACE_FILE_QUARANTINED");
            scan.Job.AcknowledgeCancellation(lease.AttemptId, lease.WorkerId, scan.NowUtc);
            scan.Attempt.Cancel(scan.NowUtc);
            await SaveAndCommitAsync(transaction, tenantExpiry, scan.LeaseExpiresAtUtc, cancellationToken);
            throw ScanCancelled();
        }

        var target = ToTarget(scan.File);
        scan.File.BeginScanning();
        await SaveAndCommitAsync(transaction, tenantExpiry, scan.LeaseExpiresAtUtc, cancellationToken);
        return target;
    }

    public async Task FinishScanAsync(
        SpaceJobLease lease,
        FileSafetyResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureFileScanLease(lease);
        _context.ChangeTracker.Clear();
        if (await RecoverFinishAsync(lease, result, cancellationToken))
            return;

        try
        {
            await FinishOwnedScanAsync(lease, result, cancellationToken);
        }
        catch (Exception error) when (MayHaveCompleted(error))
        {
            if (!await RecoverFinishAsync(lease, result, cancellationToken))
                throw;
        }
    }

    private async Task FinishOwnedScanAsync(SpaceJobLease lease, FileSafetyResult result, CancellationToken cancellationToken)
    {
        var (transaction, tenantExpiry) = await BeginWriteAsync(cancellationToken);
        await using var owned = transaction;
        var scan = await LoadLiveScanAsync(lease, cancellationToken);
        var (job, attempt, file, now, leaseExpiry) = scan;
        if (file.State != SpaceFileState.Scanning)
            throw new SpaceFileStateException("Only a scanning file can accept a safety result.");

        if (job.CancellationRequestedAtUtc.HasValue)
        {
            file.DeferScan("SPACE_FILE_QUARANTINED", result.ScanEngine, result.SignatureVersion);
            job.AcknowledgeCancellation(lease.AttemptId, lease.WorkerId, now);
            attempt.Cancel(now);
        }
        else if (result.Disposition == FileSafetyDisposition.Safe)
        {
            if (result.FailureKind.HasValue)
                throw new ArgumentException("A safe result cannot include a failure classification.", nameof(result));

            file.MarkClean(result.ScanEngine, result.SignatureVersion, result.ResultCode);
            job.Complete(lease.AttemptId, lease.WorkerId, now, JsonSerializer.Serialize(new
            {
                fileId = file.Id,
                resultCode = result.ResultCode,
                scanEngine = result.ScanEngine,
                signatureVersion = result.SignatureVersion,
                sanitizedSummary = result.SanitizedSummary,
            }));
            attempt.Succeed(now);
            await CompletePendingSourcesAsync(file, cancellationToken);
        }
        else
        {
            var kind = result.FailureKind ??
                throw new ArgumentException("A non-safe result requires a failure classification.", nameof(result));
            if (result.Disposition == FileSafetyDisposition.Rejected)
            {
                file.Reject(result.ResultCode, result.ScanEngine, result.SignatureVersion);
                await CompletePendingSourcesAsync(file, cancellationToken);
            }
            else
            {
                file.DeferScan(result.ResultCode, result.ScanEngine, result.SignatureVersion);
            }

            var decision = SpaceJobRetryPolicy.DecideAutomatic(kind, job.AttemptCount, job.MaxAttempts, now);
            job.Fail(lease.AttemptId, lease.WorkerId, kind, result.ResultCode, result.SanitizedSummary, decision, now);
            attempt.Fail(kind, result.ResultCode, result.SanitizedSummary, now);
        }

        await SaveAndCommitAsync(transaction, tenantExpiry, leaseExpiry, cancellationToken);
    }

    private async Task<bool> RecoverFinishAsync(SpaceJobLease lease, FileSafetyResult result, CancellationToken cancellationToken)
    {
        if (await ReadCompletedScanAsync(lease, cancellationToken) is not { CompletedResult: { } committed })
            return false;

        var canonical = result with
        {
            ResultCode = result.ResultCode.Trim(),
            ScanEngine = result.ScanEngine.Trim(),
            SignatureVersion = result.SignatureVersion.Trim(),
            SanitizedSummary = result.Disposition == FileSafetyDisposition.Safe ? result.SanitizedSummary : result.SanitizedSummary.Trim(),
        };
        if (committed != canonical)
            throw new SpaceJobStateException("A different file-safety result already committed for this attempt.");
        return true;
    }

    private static bool MayHaveCompleted(Exception error) => error is SpaceJobLeaseLostException or
        SpaceJobProcessingException { ErrorCode: TenantInactiveCode };

    private async Task<(IDbContextTransaction Transaction, DateTime? TenantExpiry)> BeginWriteAsync(CancellationToken cancellationToken)
    {
        var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            if (!_context.Database.IsNpgsql())
                return (transaction, null);

            var tenantId = _context.CurrentTenantId;
            var tenantContext = new TenantContext { CurrentTenantId = tenantId };
            await using var core = await DatabaseSharedContext.CreateAsync<CP6Context>(_context, DatabaseContextKind.Core,
                options => new(options, tenantContext), cancellationToken);
            await AuthenticationCompletionGateV1.AcquireAsync(core, cancellationToken);
            var tenant = await core.Sys_Tenants.AsNoTracking().Where(row => row.Id == tenantId)
                .Select(row => new { row.Enable, row.ExpireDate }).SingleOrDefaultAsync(cancellationToken);
            if (tenant is not { Enable: true } || tenant.ExpireDate <= DateTime.Now)
                throw TenantInactive();

            return (transaction, tenant.ExpireDate);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task SaveAndCommitAsync(IDbContextTransaction transaction, DateTime? tenantExpiry, DateTime leaseExpiry,
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException error)
        {
            _context.ChangeTracker.Clear();
            throw LeaseLost(error);
        }

        if (_context.Database.IsNpgsql())
        {
            if (leaseExpiry <= await DatabaseUtcClock.ReadUtcNowAsync(_context, cancellationToken))
                throw LeaseLost();
            if (tenantExpiry <= DateTime.Now)
                throw TenantInactive();
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception) when (_context.Database.IsNpgsql())
        {
            throw new SpaceJobProcessingException(SpaceJobFailureKind.Transient, "SPACE_FILE_SCAN_COMMIT_UNKNOWN",
                "The file-safety commit outcome is unknown. The original attempt can recover its persisted state.");
        }
    }

    private async Task<LiveScan> LoadLiveScanAsync(SpaceJobLease lease, CancellationToken cancellationToken)
    {
        var postgres = _context.Database.IsNpgsql();
        var job = await ScanJobs(lease, lockForWrite: postgres).SingleOrDefaultAsync(cancellationToken)
            ?? throw LeaseLost();
        var attempt = await ScanAttempts(lease).SingleOrDefaultAsync(
            row => row.Outcome == SpaceJobAttemptOutcome.Running, cancellationToken) ?? throw LeaseLost();
        var file = await (postgres
            ? SpaceFileReferenceLock.LoadAsync(_context, lease.TenantId, lease.SubjectId, includeDeleted: false, cancellationToken)
            : _context.Files.SingleOrDefaultAsync(row => row.Id == lease.SubjectId, cancellationToken))
            ?? throw LeaseLost();

        var now = postgres ? await DatabaseUtcClock.ReadUtcNowAsync(_context, cancellationToken) : RequireUtcNow();
        job.EnsureLease(lease.AttemptId, lease.WorkerId, now);
        if (!string.Equals(file.Sha256, lease.InputHash, StringComparison.OrdinalIgnoreCase))
            throw new SpaceJobStateException("The file content no longer matches the scan Job input.");
        return new(job, attempt, file, now, job.LockExpiresAtUtc!.Value);
    }

    private IQueryable<SpaceJob> ScanJobs(SpaceJobLease lease, bool lockForWrite = false)
    {
        var jobs = lockForWrite
            ? _context.Jobs.FromSqlInterpolated($"""
                SELECT * FROM "Space_Job"
                WHERE "TenantId" = {lease.TenantId} AND "Id" = {lease.JobId}
                FOR UPDATE
                """)
            : _context.Jobs;
        return jobs.Where(row => row.Id == lease.JobId &&
            row.JobType == SpaceJobType.FileScan && row.SubjectType == SpaceJobSubjectType.File &&
            row.SubjectId == lease.SubjectId && row.InputHash == lease.InputHash &&
            row.AttemptCount == lease.AttemptNo);
    }

    private IQueryable<SpaceJobAttempt> ScanAttempts(SpaceJobLease lease) => _context.JobAttempts.Where(
        row => row.Id == lease.AttemptId && row.JobId == lease.JobId && row.AttemptNo == lease.AttemptNo &&
            row.WorkerId == lease.WorkerId && row.InputHash == lease.InputHash);

    private async Task<SpaceFileScanTarget?> ReadCompletedScanAsync(SpaceJobLease lease, CancellationToken cancellationToken)
    {
        var state = await (
            from job in ScanJobs(lease).AsNoTracking()
            join attempt in ScanAttempts(lease).AsNoTracking() on job.Id equals attempt.JobId
            join storedFile in _context.Files.AsNoTracking() on job.SubjectId equals storedFile.Id
            where attempt.Outcome != SpaceJobAttemptOutcome.Running && storedFile.Sha256 == job.InputHash
            select new { Job = job, Attempt = attempt, File = storedFile }).SingleOrDefaultAsync(cancellationToken);
        if (state is null)
            return null;

        var file = state.File;
        var jobStatus = state.Job.Status;
        if (state.Attempt.Outcome == SpaceJobAttemptOutcome.Cancelled && jobStatus == SpaceJobStatus.Cancelled)
            throw ScanCancelled();

        FileSafetyResult? result = null;
        if (state.Attempt.Outcome == SpaceJobAttemptOutcome.Succeeded &&
            jobStatus == SpaceJobStatus.Succeeded && file.State == SpaceFileState.Clean)
        {
            result = FileSafetyResult.Clean(file.ScanEngine!, file.SignatureVersion!, file.ScanResultCode!);
            if (state.Job.ResultSummaryJson is { } summary)
            {
                using var document = JsonDocument.Parse(summary);
                if (document.RootElement.TryGetProperty("sanitizedSummary", out var saved))
                    result = result with { SanitizedSummary = saved.GetString()! };
            }
        }
        else if (state.Attempt.Outcome == SpaceJobAttemptOutcome.Failed &&
            file.ScanEngine is { } engine && file.SignatureVersion is { } signature)
        {
            var disposition = (file.State, jobStatus) switch
            {
                (SpaceFileState.Rejected, SpaceJobStatus.Failed) => FileSafetyDisposition.Rejected,
                (SpaceFileState.Quarantined, SpaceJobStatus.Queued or SpaceJobStatus.DeadLetter) => FileSafetyDisposition.Deferred,
                _ => (FileSafetyDisposition?)null,
            };
            if (disposition is { } completed)
                result = new FileSafetyResult(completed, state.Attempt.ErrorCode!, engine, signature,
                    state.Attempt.FailureKind, state.Attempt.SanitizedError!);
        }

        return result is null ? null : ToTarget(file) with { CompletedResult = result };
    }

    private static SpaceFileScanTarget ToTarget(SpaceFile file) => new(
        file.TenantId,
        file.Id,
        file.StorageKey,
        file.OriginalName,
        file.DetectedContentType ?? throw new SpaceFileStateException("A quarantined file requires a detected content type."),
        file.Extension ?? throw new SpaceFileStateException("A quarantined file requires an extension."),
        file.SizeBytes,
        file.Sha256!);

    private async Task CompletePendingSourcesAsync(
        SpaceFile file,
        CancellationToken cancellationToken)
    {
        var sources = await _context.Sources
            .Where(source =>
                source.FileId == file.Id &&
                source.State == SpaceSourceState.Scanning)
            .ToListAsync(cancellationToken);
        foreach (var source in sources)
            source.CompleteFileScan(file);
    }

    private void EnsureFileScanLease(SpaceJobLease lease)
    {
        if (lease.TenantId == Guid.Empty ||
            lease.TenantId != _context.CurrentTenantId ||
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

    private static OperationCanceledException ScanCancelled() =>
        new("The file-safety Job cancellation was acknowledged.");

    private static SpaceJobProcessingException TenantInactive() =>
        new(SpaceJobFailureKind.Transient, TenantInactiveCode,
            "The tenant is paused or expired. The file-safety Job can retry when the tenant is active.");

    private sealed record LiveScan(SpaceJob Job, SpaceJobAttempt Attempt, SpaceFile File, DateTime NowUtc, DateTime LeaseExpiresAtUtc);
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

        return await SpaceOwnedTransaction.ExecuteAsync(
            _context,
            IsolationLevel.Serializable,
            async transaction =>
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
            },
            cancellationToken);
    }

    public async Task MarkContentDeletedAsync(
        SpaceFileDeletionCandidate candidate,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureTenantAndTime(candidate.TenantId, nowUtc);
        _ = await SpaceOwnedTransaction.ExecuteAsync(
            _context,
            IsolationLevel.ReadCommitted,
            async transaction =>
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
                return true;
            },
            cancellationToken);
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
        // The caller owns the transaction, including any whole-command recovery.
        // FOR UPDATE protects an existing file; a missing file is returned without a write.
        var query = context.Database.IsNpgsql()
            ? context.Files.FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM "Space_File"
                 WHERE "TenantId" = {tenantId}
                   AND "Id" = {fileId}
                 FOR UPDATE
                 """)
            : context.Files.FromSqlInterpolated(
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
