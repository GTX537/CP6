using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Wms;

public interface IMaterialShortageService
{
    Task<MaterialShortage> CreateAsync(MaterialShortage entity);
    Task<PagedResultDto<MaterialShortage>> SearchAsync(MaterialShortageQuery query);
    Task<MaterialShortage> ResolveAsync(Guid id, string? remark, string? userName);
    Task<MaterialShortage> DismissAsync(Guid id, string? remark, string? userName);
}

public class MaterialShortageQuery
{
    public string? Status { get; set; }
    public string? WorkOrderNo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
