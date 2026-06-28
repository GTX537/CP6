using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>WMS 库存只读查询接真实现（读 T_Stock/T_Location/T_OutboundOrder*；纯读，多租户全局过滤自动隔离）。</summary>
public class WmsStockQuery : IWmsStockQuery
{
    private readonly CP6Context _db;
    public WmsStockQuery(CP6Context db) => _db = db;

    public async Task<decimal> GetStockQtyAsync(string locationCode, CancellationToken ct = default)
        => await _db.Stocks.Where(s => s.LocationCd == locationCode).SumAsync(s => s.PhysicalQty, ct);

    public Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default)
        => throw new NotImplementedException();   // Task 2

    public Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default)
        => throw new NotImplementedException();   // Task 3
}
