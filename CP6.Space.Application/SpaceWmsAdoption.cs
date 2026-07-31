using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed record SpaceWarehouseIdentity(
    Guid SiteId,
    string SiteCode,
    string WarehouseCode);

public interface ISpaceWarehouseResolver
{
    Task<SpaceWarehouseIdentity?> ResolveAsync(
        Guid siteId,
        CancellationToken cancellationToken = default);
}

public interface ISpaceWmsAdoptionService
{
    Task<RefreshSpaceWmsAdoptionResponse> RefreshAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<SpacePage<SpaceWmsAdoptionDto>> GetLocationsAsync(
        Guid versionId,
        string? status,
        string? differenceCode,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    Task<SpaceWmsAdoptionCommandResponse> BindAsync(
        Guid versionId,
        Guid adoptionId,
        BindSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceWmsAdoptionCommandResponse> BindBatchAsync(
        Guid versionId,
        BatchBindSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceWmsAdoptionCommandResponse> PlaceAsync(
        Guid versionId,
        Guid adoptionId,
        PlaceSpaceWmsAdoptionRequest request,
        CancellationToken cancellationToken = default);
}
