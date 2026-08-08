using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceAiAdministrationService
{
    Task<SpaceAiPolicyDto> GetPolicyAsync(
        CancellationToken cancellationToken = default);

    Task<UpdateSpaceAiPolicyResponse> UpdatePolicyAsync(
        UpdateSpaceAiPolicyRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceAiUsagePageDto> GetUsageAsync(
        SpaceAiUsageQuery query,
        CancellationToken cancellationToken = default);
}
