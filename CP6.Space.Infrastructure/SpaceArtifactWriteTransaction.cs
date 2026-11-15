using System.Data;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public interface ISpaceArtifactWriteAuthority
{
    Task<DateTime?> AuthorizeTenantAsync(SpaceContext context, CancellationToken cancellationToken);
    void ConfirmTenantExpiry(DateTime? expiresAt);
}

internal sealed class SpaceArtifactWriteTransaction(
    SpaceContext context,
    SpaceJobLease lease,
    ISpaceFileStore? files,
    IDbContextTransaction? transaction,
    ISpaceArtifactWriteAuthority? authority,
    DateTime? expiresAt,
    SpaceJob? currentJob) : IAsyncDisposable
{
    private readonly List<(Guid FileId, string StorageKey)> _published = [];
    private bool _preservePublished;

    internal static Task<SpaceArtifactWriteTransaction> BeginMetadataAsync(SpaceContext context, SpaceJobLease lease,
        ISpaceArtifactWriteAuthority? authority, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql() && authority is null)
            throw new SpaceJobProcessingException(SpaceJobFailureKind.Resource, SpaceErrorCodes.JobProcessorUnavailable,
                "Main authority is unavailable for the source processing write.");

        return BeginAsync(context, lease, null, authority, cancellationToken);
    }

    internal static async Task<SpaceArtifactWriteTransaction> BeginAsync(SpaceContext context, SpaceJobLease lease,
        ISpaceFileStore? files, ISpaceArtifactWriteAuthority? authority, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return new(context, lease, files, null, null, null, null);

        var main = context.Database.IsNpgsql() ? authority : null;
        for (var attempt = 1; ; attempt++)
        {
            var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                if (main is null)
                    return new(context, lease, files, tx, null, null, null);

                var expiry = await main.AuthorizeTenantAsync(context, cancellationToken);
                var job = await context.Jobs.FromSqlInterpolated($"""
                    SELECT j.* FROM public."Space_Job" AS j
                    WHERE j."TenantId" = {lease.TenantId} AND j."Id" = {lease.JobId}
                    FOR SHARE
                    """).AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                    ?? throw new SpaceJobLeaseLostException("The artifact Job is no longer available.");
                await VerifyLeaseAsync(context, job, lease, cancellationToken);
                return new(context, lease, files, tx, main, expiry, job);
            }
            catch (Exception error) when (main is not null && DatabaseFailureClassifier.Classify(error).CanRetryTransaction)
            {
                await tx.DisposeAsync();
                if (attempt == 3)
                    throw new SpaceJobProcessingException(SpaceJobFailureKind.Transient, "SPACE_ARTIFACT_AUTHORITY_CHANGED",
                        "The artifact write authority changed concurrently.");
            }
            catch
            {
                await tx.DisposeAsync();
                throw;
            }
        }
    }

    internal async Task PublishAsync(Guid fileId, ISpaceQuarantineWriteSession session, CancellationToken cancellationToken)
    {
        if (files is null)
            throw new InvalidOperationException("Artifact publication requires private file storage.");
        await session.CommitAsync(cancellationToken);
        _published.Add((fileId, session.StorageKey));
    }

    internal async Task<SpaceModelSource> LockMetadataSourceAsync(SpaceModelSource input, SpaceFile inputFile,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
            return input;

        // The preceding step may have tracked these rows. Lock and read the current
        // file before its source, in the same order as the file-state owner.
        context.ChangeTracker.Clear();
        var file = await SpaceFileReferenceLock.LoadAsync(context, lease.TenantId, inputFile.Id, includeDeleted: false, cancellationToken);
        if (file is not { State: SpaceFileState.Clean } || file.Sha256 != inputFile.Sha256 ||
            file.StorageKey != inputFile.StorageKey || file.SizeBytes != inputFile.SizeBytes)
        {
            throw new SpaceJobProcessingException(SpaceJobFailureKind.Security, SpaceErrorCodes.SourceUnsafe,
                "The current source file is not clean or no longer matches the Job input.");
        }

        var source = await context.Sources.FromSqlInterpolated($"""
            SELECT s.* FROM public."Space_ModelSource" AS s
            WHERE s."TenantId" = {lease.TenantId} AND s."Id" = {input.Id}
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (source is null)
            throw new SpaceJobProcessingException(SpaceJobFailureKind.Input, SpaceErrorCodes.SourceNotFound,
                "The source is no longer available.");
        if (source.ModelVersionId != input.ModelVersionId || source.SourceType != input.SourceType ||
            source.FileId != file.Id || source.Sha256 != input.Sha256 || source.ParserVersion != input.ParserVersion ||
            source.MappingProfileId != input.MappingProfileId || source.MappingProfileVersion != input.MappingProfileVersion ||
            source.Unit != input.Unit || source.ScaleToMillimeters != input.ScaleToMillimeters || source.TransformJson != input.TransformJson)
        {
            throw new SpaceJobProcessingException(SpaceJobFailureKind.Input, SpaceErrorCodes.SourceUnsafe,
                "The current source no longer matches the Job input.");
        }

        return source;
    }

    internal async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            _preservePublished = true;
            return;
        }

        if (currentJob is not null)
        {
            await VerifyLeaseAsync(context, currentJob, lease, cancellationToken);
            authority!.ConfirmTenantExpiry(expiresAt);
        }
        cancellationToken.ThrowIfCancellationRequested();

        // A lost acknowledgement can follow a committed catalog that owns these objects.
        _preservePublished = true;
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            throw new SpaceJobProcessingException(SpaceJobFailureKind.Transient, "SPACE_ARTIFACT_COMMIT_UNKNOWN",
                "The Job write commit outcome is unknown. A later Job attempt recovers its persisted output.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
        finally
        {
            if (!_preservePublished)
            {
                foreach (var (fileId, storageKey) in _published)
                {
                    try
                    {
                        await files!.DeleteAsync(lease.TenantId, fileId, storageKey, CancellationToken.None);
                    }
                    catch
                    {
                        // Retention cleanup remains the fallback for orphaned objects.
                    }
                }
            }
        }
    }

    private static async Task VerifyLeaseAsync(SpaceContext context, SpaceJob job, SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        if (job.JobType != lease.JobType || job.SubjectType != lease.SubjectType || job.SubjectId != lease.SubjectId ||
            job.InputHash != lease.InputHash || job.AttemptCount != lease.AttemptNo)
            throw new SpaceJobLeaseLostException("The Job no longer matches this worker lease.");
        job.EnsureLease(lease.AttemptId, lease.WorkerId, await DatabaseUtcClock.ReadUtcNowAsync(context, cancellationToken));
        if (job.CancellationRequestedAtUtc.HasValue)
            throw new SpaceJobProcessingException(SpaceJobFailureKind.Transient, "SPACE_ARTIFACT_JOB_CANCEL_PENDING",
                "The artifact Job has a pending cancellation.");
    }
}
