using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed class SpaceFileLifecycleService
{
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceFileRetentionStore _retention;
    private readonly ISpaceFileStore _files;
    private readonly ISpaceClock _clock;

    public SpaceFileLifecycleService(
        ISpaceExecutionContext execution,
        ISpaceFileRetentionStore retention,
        ISpaceFileStore files,
        ISpaceClock clock)
    {
        _execution = execution;
        _retention = retention;
        _files = files;
        _clock = clock;
    }

    public async Task DeleteAsync(
        SpaceFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (_execution.TenantId == Guid.Empty ||
            file.TenantId != _execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "The Space operation crossed the verified tenant boundary.");
        }

        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        var result = await _retention.TryTombstoneAsync(
            _execution.TenantId,
            file.Id,
            now,
            requireExpired: false,
            cancellationToken);
        if (result.Status == SpaceFileTombstoneStatus.Referenced)
        {
            throw new SpaceFileReferenceException(
                "A referenced Space file cannot be deleted.");
        }
        if (result.Status == SpaceFileTombstoneStatus.NotFound)
            throw new KeyNotFoundException("The Space file was not found.");
        if (result.Status == SpaceFileTombstoneStatus.NotExpired)
        {
            throw new SpaceFileStateException(
                "An uploading or scanning file cannot be deleted.");
        }

        if (result.Candidate is null)
            return;
        if (result.Candidate.TenantId != _execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "The file tombstone crossed the verified tenant boundary.");
        }
        file.RequestDeletion(0, now);
        await _files.DeleteAsync(
            result.Candidate.TenantId,
            result.Candidate.FileId,
            result.Candidate.StorageKey,
            cancellationToken);
        await _retention.MarkContentDeletedAsync(
            result.Candidate,
            now,
            cancellationToken);
    }
}
