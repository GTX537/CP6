using CP6.Entity.DomainModels.Mes;
namespace CP6.Core.Services.Mes;

public interface IProcessCostRateService
{
    Task<List<ProcessCostRate>> ListAsync(string? wgCd);
    Task<ProcessCostRate?> ResolveAsync(string wgCd, DateTime onDate);
    Task UpsertAsync(ProcessCostRate dto, string? user);
    Task DeleteAsync(Guid id, string? user);
}
