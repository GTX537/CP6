using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public interface IApproverMapService
{
    Task<IReadOnlyList<Wf_ApproverMap>> ListAsync(string? mapKey);
    Task<IReadOnlyList<string>> DistinctKeysAsync();
    Task<Wf_ApproverMap> CreateAsync(string mapKey, string matchValue, Guid? approverUserId, int? approverRoleId, int orderNo = 0);
    Task UpdateAsync(Guid id, string matchValue, Guid? approverUserId, int? approverRoleId, int orderNo, bool enable);
    Task DeleteAsync(Guid id);
}
