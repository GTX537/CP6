using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceWmsRuntimeService
{
    Task<SpaceWmsRuntimeInventoryResponse> QueryInventoryAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds = null,
        CancellationToken cancellationToken = default);

    Task<SpaceWmsRuntimeInventoryLocateResponse> LocateInventoryAsync(
        Guid siteId,
        SpaceWmsInventoryLocateCriteria criteria,
        CancellationToken cancellationToken = default);

    Task<SpaceWmsRuntimeTaskResponse> QueryTasksAsync(
        Guid siteId,
        IReadOnlyCollection<Guid>? locationLogicalIds = null,
        CancellationToken cancellationToken = default);

    Task<SpaceWmsRuntimeTaskPathResponse> GetTaskPathAsync(
        Guid siteId,
        string taskId,
        CancellationToken cancellationToken = default);

    Task<SpaceWmsRuntimeWarehouseOverviewResponse> GetWarehouseOverviewAsync(
        Guid siteId,
        int abcWindowDays = 90,
        CancellationToken cancellationToken = default);
}
