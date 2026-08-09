using CP6.Space.Domain;

namespace CP6.Space.Application;

/// <summary>
/// Coordinates the model header and version aggregate for the E01 active-draft invariant.
/// Persistence transactions are supplied by E01-S05 application use cases.
/// </summary>
public sealed class SpaceModelVersionCoordinator
{
    private readonly ISpaceExecutionContext _execution;

    public SpaceModelVersionCoordinator(ISpaceExecutionContext execution)
    {
        _execution = execution;
    }

    public SpaceModelVersion CreateDraft(
        SpaceModel model,
        long versionNo,
        string name,
        Guid? basedOnVersionId = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        EnsureTenant(model.TenantId);

        var version = SpaceModelVersion.CreateDraft(
            _execution.TenantId,
            model.Id,
            versionNo,
            name,
            basedOnVersionId);
        model.ReserveDraft(version);
        return version;
    }

    public void ActivatePublished(
        SpaceModel model,
        SpaceModelVersion version,
        string materializedHash)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(version);
        EnsureTenant(model.TenantId);
        EnsureTenant(version.TenantId);
        model.SetPublishedVersion(version, materializedHash);
    }

    private void EnsureTenant(Guid tenantId)
    {
        if (_execution.TenantId == Guid.Empty || tenantId != _execution.TenantId)
            throw new SpaceTenantScopeException("The Space operation crossed the verified tenant boundary.");
    }
}
