using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceDispatchOutcomeEvaluationService
{
    Task<SpaceDispatchOutcomeEvaluationDto> GetAsync(
        Guid siteId,
        Guid recommendationId,
        Guid approvalRequestId,
        CancellationToken cancellationToken = default);
}
