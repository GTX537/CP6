using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
namespace CP6.Core.Services.Mes;

public interface IWorkCenterService
{
    Task<List<WorkCenter>> ListAsync(string? keyword);
    Task<WorkCenter?> GetAsync(string wgCd);
    Task UpsertAsync(WorkCenter dto, string? user);
    Task DeleteAsync(string wgCd, string? user);
    Task<List<WorkCenterOwnerV1>> ListOwnerAsync(string? filter);
    Task<WorkCenterOwnerV1?> GetOwnerAsync(string code);
    Task SaveOwnerAsync(WorkCenterOwnerV1 input);
    Task DeleteOwnerAsync(Guid id, byte[]? version);
}
