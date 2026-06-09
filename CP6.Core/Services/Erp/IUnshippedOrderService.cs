using CP6.Entity.DTOs;
using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Erp;

public interface IUnshippedOrderService
{
    /// <summary>
    /// Search orders that are placed but not yet fully shipped, joined with current MES and WMS status.
    /// </summary>
    Task<PagedResultDto<UnshippedOrderItemDto>> SearchAsync(UnshippedOrderQuery query);

    /// <summary>
    /// Export matching unshipped orders as UTF-8 BOM CSV, capped for operational safety.
    /// </summary>
    Task<byte[]> ExportCsvAsync(UnshippedOrderQuery query);
}
