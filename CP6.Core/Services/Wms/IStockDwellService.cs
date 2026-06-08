using CP6.Entity.DTOs;

namespace CP6.Core.Services.Wms;

public interface IStockDwellService
{
    Task<StockDwellSummaryDto> GetSummaryAsync(StockDwellQuery query, CancellationToken ct = default);
}
