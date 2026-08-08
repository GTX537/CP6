using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpacePersonnelEventService
{
    Task<IngestSpacePersonnelEventsResponse> IngestAsync(
        Guid siteId,
        IngestSpacePersonnelEventsRequest request,
        CancellationToken cancellationToken = default);
}
