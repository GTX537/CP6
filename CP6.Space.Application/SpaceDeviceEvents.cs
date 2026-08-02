using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceDeviceEventService
{
    Task<SpaceDeviceMappingPageDto> GetMappingsAsync(
        Guid siteId,
        string? sourceId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    Task<SpaceDeviceMappingDto> CreateMappingAsync(
        Guid siteId,
        CreateSpaceDeviceMappingRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceDeviceMappingDto> UpdateMappingAsync(
        Guid siteId,
        Guid mappingId,
        UpdateSpaceDeviceMappingRequest request,
        CancellationToken cancellationToken = default);

    Task<IngestSpaceDeviceEventsResponse> IngestAsync(
        Guid siteId,
        IngestSpaceDeviceEventsRequest request,
        CancellationToken cancellationToken = default);
}
