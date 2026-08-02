using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpacePlanningScenarioService
{
    Task<CreateSpacePlanningScenarioBranchResponse> CreateBranchAsync(
        Guid siteId,
        Guid branchId,
        CreateSpacePlanningScenarioBranchRequest request,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningScenarioBranchDto> GetBranchAsync(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken = default);

    Task<SpacePlanningScenarioBranchListResponse> GetBranchesAsync(
        Guid siteId,
        int limit,
        CancellationToken cancellationToken = default);
}
