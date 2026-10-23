using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
namespace CP6.Core.Services.Mes;

public interface IProcessCostRateService
{
    Task<List<ProcessCostRate>> ListAsync(string? wgCd);
    Task<ProcessCostRate?> ResolveAsync(string wgCd, DateTime onDate);
    Task UpsertAsync(ProcessCostRate dto, string? user);
    Task DeleteAsync(Guid id, string? user);
    Task<List<ProcessCostRateOwnerV1>> ListOwnerAsync(string? filter);
    Task<ProcessCostRateOwnerV1?> ResolveOwnerAsync(string code, DateTime onDate);
    Task SaveOwnerAsync(ProcessCostRateOwnerV1 input);
    Task DeleteOwnerAsync(Guid id, byte[]? version);
}
