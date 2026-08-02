using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpacePlanningDatasetService
{
    Task<CreateSpacePlanningHistoricalDatasetResponse> CreateAsync(
        Guid siteId,
        Guid branchId,
        Guid datasetId,
        CreateSpacePlanningHistoricalDatasetRequest request,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningHistoricalDatasetDto> GetAsync(
        Guid siteId,
        Guid branchId,
        Guid datasetId,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningHistoricalDatasetListResponse> GetListAsync(
        Guid siteId,
        Guid branchId,
        int limit,
        CancellationToken cancellationToken = default);
}
