using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed record SpaceCadPreparationProviderRequest(
    Guid TenantId,
    Guid SiteId,
    Guid FileId,
    Guid SourceId,
    string SourceSha256,
    SpaceCadSourceFormat SourceFormat,
    SpaceWorkerSandboxPolicy Sandbox);

/// <summary>
/// Gateway to the controlled CAD inspection worker. Implementations may use an
/// ICadConverter inside that worker, but raw CAD bytes must not leave the
/// approved isolation boundary.
/// </summary>
public interface ISpaceCadPreparationProvider
{
    Task<SpaceCadIrPackageV1> InspectAsync(
        SpaceCadPreparationProviderRequest request,
        Stream source,
        CancellationToken cancellationToken = default);
}

public interface ISpaceCadMappingProfileCatalog
{
    Task<IReadOnlyList<SpaceCadMappingProfileV1>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<SpaceCadMappingProfileV1?> FindAsync(
        Guid profileId,
        int version,
        CancellationToken cancellationToken = default);
}

public interface ISpaceCadMappingProfileService :
    ISpaceCadMappingProfileCatalog
{
    Task<IReadOnlyList<SpaceCadMappingProfileDto>> GetProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<SpaceCadMappingProfileDto> GetProfileAsync(
        Guid profileId,
        int? version = null,
        CancellationToken cancellationToken = default);

    Task<SaveSpaceCadMappingProfileResponse> SaveProfileAsync(
        SaveSpaceCadMappingProfileRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface ISpaceCadPreparationService
{
    Task<SpaceCadPreparationStatusDto> GetStatusAsync(
        Guid versionId,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpaceCadMappingProfileSummaryDto>> ListProfilesAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<PreviewSpaceCadPreparationResponse> PreviewAsync(
        Guid versionId,
        Guid sourceId,
        PreviewSpaceCadPreparationRequest request,
        CancellationToken cancellationToken = default);
}
