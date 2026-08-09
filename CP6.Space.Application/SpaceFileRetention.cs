using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed class SpaceFileRetentionOptions
{
    public TimeSpan? SourceRetention { get; init; }
    public TimeSpan? ArtifactRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan? TemporaryRetention { get; init; } = TimeSpan.FromDays(1);

    public DateTime? GetRetainUntilUtc(
        SpaceFileRetentionClass retentionClass,
        DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Time must be UTC.", nameof(nowUtc));

        var duration = retentionClass switch
        {
            SpaceFileRetentionClass.Source => SourceRetention,
            SpaceFileRetentionClass.Artifact => ArtifactRetention,
            SpaceFileRetentionClass.Temporary => TemporaryRetention,
            _ => throw new ArgumentOutOfRangeException(nameof(retentionClass)),
        };
        if (!duration.HasValue)
            return null;
        if (duration <= TimeSpan.Zero)
            throw new InvalidOperationException(
                "Configured Space file retention must be positive.");
        return nowUtc.Add(duration.Value);
    }
}

public enum SpaceFileTombstoneStatus
{
    Tombstoned,
    AlreadyTombstoned,
    Referenced,
    NotExpired,
    NotFound,
}

public sealed record SpaceFileDeletionCandidate(
    Guid TenantId,
    Guid FileId,
    string StorageKey);

public sealed record SpaceFileTombstoneResult(
    SpaceFileTombstoneStatus Status,
    SpaceFileDeletionCandidate? Candidate = null);

public interface ISpaceFileRetentionStore
{
    Task<IReadOnlyList<Guid>> FindExpiredFileIdsAsync(
        Guid tenantId,
        DateTime nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpaceFileDeletionCandidate>>
        FindPendingContentDeletionAsync(
            Guid tenantId,
            int batchSize,
            CancellationToken cancellationToken = default);

    Task<SpaceFileTombstoneResult> TryTombstoneAsync(
        Guid tenantId,
        Guid fileId,
        DateTime nowUtc,
        bool requireExpired,
        CancellationToken cancellationToken = default);

    Task MarkContentDeletedAsync(
        SpaceFileDeletionCandidate candidate,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}

public interface ISpaceFileCleanupAuthorization
{
    bool IsRetentionServicePrincipal { get; }
}

public sealed record SpaceFileRetentionCleanupResult(
    int ExpiredCandidates,
    int Tombstoned,
    int ReferencedSkipped,
    int ObjectsDeleted,
    int ObjectDeleteFailures);

public sealed class SpaceFileRetentionCleanupService
{
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceFileCleanupAuthorization _authorization;
    private readonly ISpaceFileRetentionStore _retention;
    private readonly ISpaceFileStore _files;
    private readonly ISpaceClock _clock;

    public SpaceFileRetentionCleanupService(
        ISpaceExecutionContext execution,
        ISpaceFileCleanupAuthorization authorization,
        ISpaceFileRetentionStore retention,
        ISpaceFileStore files,
        ISpaceClock clock)
    {
        _execution = execution;
        _authorization = authorization;
        _retention = retention;
        _files = files;
        _clock = clock;
    }

    public async Task<SpaceFileRetentionCleanupResult> RunAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (_execution.TenantId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified Space tenant context is required.");
        if (!_authorization.IsRetentionServicePrincipal)
            throw new UnauthorizedAccessException(
                "Space file retention cleanup requires its restricted service principal.");
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");

        var expiredIds = await _retention.FindExpiredFileIdsAsync(
            _execution.TenantId,
            now,
            batchSize,
            cancellationToken);
        var newlyTombstoned = new Dictionary<Guid, SpaceFileDeletionCandidate>();
        var tombstoned = 0;
        var referenced = 0;
        foreach (var fileId in expiredIds)
        {
            var result = await _retention.TryTombstoneAsync(
                _execution.TenantId,
                fileId,
                now,
                requireExpired: true,
                cancellationToken);
            if (result.Status == SpaceFileTombstoneStatus.Referenced)
            {
                referenced++;
                continue;
            }
            if (result.Status == SpaceFileTombstoneStatus.Tombstoned)
                tombstoned++;
            if (result.Candidate is not null)
            {
                EnsureCandidateTenant(result.Candidate);
                newlyTombstoned[result.Candidate.FileId] = result.Candidate;
            }
        }

        var priorPending = await _retention.FindPendingContentDeletionAsync(
            _execution.TenantId,
            batchSize,
            cancellationToken);
        var pending = new Dictionary<Guid, SpaceFileDeletionCandidate>();
        foreach (var candidate in priorPending)
        {
            EnsureCandidateTenant(candidate);
            pending[candidate.FileId] = candidate;
        }
        foreach (var candidate in newlyTombstoned.Values)
            pending.TryAdd(candidate.FileId, candidate);

        var deleted = 0;
        var failures = 0;
        foreach (var candidate in pending.Values.Take(batchSize))
        {
            try
            {
                await _files.DeleteAsync(
                    candidate.TenantId,
                    candidate.FileId,
                    candidate.StorageKey,
                    cancellationToken);
                await _retention.MarkContentDeletedAsync(
                    candidate,
                    now,
                    cancellationToken);
                deleted++;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failures++;
            }
        }

        return new SpaceFileRetentionCleanupResult(
            expiredIds.Count,
            tombstoned,
            referenced,
            deleted,
            failures);
    }

    private void EnsureCandidateTenant(SpaceFileDeletionCandidate candidate)
    {
        if (candidate.TenantId != _execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "The retention store returned a cross-tenant file tombstone.");
        }
    }
}
