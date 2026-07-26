using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed class SpaceSourceCoordinator
{
    private readonly ISpaceExecutionContext _execution;

    public SpaceSourceCoordinator(ISpaceExecutionContext execution)
    {
        _execution = execution;
    }

    public SpaceModelSource AddFileSource(
        SpaceModelVersion version,
        SpaceFile file,
        SpaceSourceType sourceType,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(file);
        EnsureTenant(version.TenantId);
        EnsureTenant(file.TenantId);
        if (file.State != SpaceFileState.Clean)
        {
            throw new SpaceFileValidationException(
                SpaceErrorCodes.SourceUnsafe,
                "Only a clean file can be attached as a model source.");
        }

        var source = SpaceModelSource.CreateFileSource(
            _execution.TenantId,
            version.Id,
            sourceType,
            file,
            displayName);
        version.TouchContent();
        return source;
    }

    public SpaceArtifact AddArtifact(
        SpaceModelVersion version,
        SpaceModelSource? source,
        SpaceFile file,
        SpaceArtifactType artifactType,
        string schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(file);
        EnsureTenant(version.TenantId);
        EnsureTenant(file.TenantId);
        if (source is not null)
            EnsureTenant(source.TenantId);

        return SpaceArtifact.Create(
            _execution.TenantId,
            version.Id,
            source,
            file,
            artifactType,
            schemaVersion);
    }

    private void EnsureTenant(Guid tenantId)
    {
        if (_execution.TenantId == Guid.Empty || tenantId != _execution.TenantId)
            throw new SpaceTenantScopeException(
                "The Space operation crossed the verified tenant boundary.");
    }
}
