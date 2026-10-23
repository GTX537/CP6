using CP6.Entity.DTOs;

namespace CP6.Core.Services.Wms;

public interface IStockDwellService
{
    Task<CP6.Entity.DTOs.Wms.StockDwellObservationV1> GetObservationAsync(StockDwellQuery query, CancellationToken ct = default);
    Task<StockDwellSummaryDto> GetSummaryAsync(StockDwellQuery query, CancellationToken ct = default);
}
