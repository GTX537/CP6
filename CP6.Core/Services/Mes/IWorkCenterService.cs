using CP6.Entity.DomainModels.Mes;
namespace CP6.Core.Services.Mes;

public interface IWorkCenterService
{
    Task<List<WorkCenter>> ListAsync(string? keyword);
    Task<WorkCenter?> GetAsync(string wgCd);
    Task UpsertAsync(WorkCenter dto, string? user);
    Task DeleteAsync(string wgCd, string? user);
}
