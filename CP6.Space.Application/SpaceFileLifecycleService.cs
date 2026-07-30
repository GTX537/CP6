using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed class SpaceFileLifecycleService
{
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceFileCatalog _catalog;

    public SpaceFileLifecycleService(
        ISpaceExecutionContext execution,
        ISpaceFileCatalog catalog)
    {
        _execution = execution;
        _catalog = catalog;
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

        var references = await _catalog.CountActiveReferencesAsync(
            _execution.TenantId,
            file.Id,
            cancellationToken);
        file.Delete(references);
        await _catalog.SaveChangesAsync(cancellationToken);
    }
}
