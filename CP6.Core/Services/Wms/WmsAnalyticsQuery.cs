using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed class WmsAnalyticsQuery : IWmsAnalyticsQuery
{
    private readonly CP6Context _db;
    public WmsAnalyticsQuery(CP6Context db) => _db = db;

    public async Task<IReadOnlyList<WmsOutboundAggregate>> GetOutboundAggregatesAsync(
        string warehouseCd, DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
    {
        return await _db.StockTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.WarehouseCd == warehouseCd
                        && x.TxnType == WmsTxnType.OUT
                        && x.TxnDateTime >= fromInclusive
                        && x.TxnDateTime < toExclusive)
            .GroupBy(x => x.ProductCd)
            .Select(g => new WmsOutboundAggregate
            {
                ProductCd = g.Key,
                OutCount = g.Count(),
                OutQty = g.Sum(x => x.Qty),
            })
            .ToListAsync(ct);
    }

    public async Task<WmsActivitySummary> GetActivitySummaryAsync(
        string warehouseCd, DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default)
    {
        var rows = await _db.StockTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.WarehouseCd == warehouseCd
                        && x.TxnDateTime >= fromInclusive
                        && x.TxnDateTime < toExclusive
                        && (x.TxnType == WmsTxnType.IN || x.TxnType == WmsTxnType.OUT))
            .GroupBy(x => x.TxnType)
            .Select(g => new { TxnType = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new WmsActivitySummary
        {
            InboundCount = rows.FirstOrDefault(x => x.TxnType == WmsTxnType.IN)?.Count ?? 0,
            OutboundCount = rows.FirstOrDefault(x => x.TxnType == WmsTxnType.OUT)?.Count ?? 0,
        };
    }
}
