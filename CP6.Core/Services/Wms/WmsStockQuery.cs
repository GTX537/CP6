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

    public CP6.Entity.DTOs.Space.SpaceDataSourceKind DataSourceKind =>
        CP6.Entity.DTOs.Space.SpaceDataSourceKind.Real;

    public string DataSourceId => "CP6_WMS";

    public async Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default)
    {
        var q = _db.Stocks.Where(s => s.LocationCd == locationCode);
        if (!string.IsNullOrEmpty(warehouseCd)) q = q.Where(s => s.WarehouseCd == warehouseCd);
        return await q.SumAsync(s => s.PhysicalQty, ct);
    }

    public async Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default)
    {
        var codes = locationCodes.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        if (codes.Count == 0) return Array.Empty<WmsStockDto>();

        var stockRows = await _db.Stocks.Where(s => codes.Contains(s.LocationCd)).ToListAsync(ct);
        var stockByLoc = stockRows.GroupBy(s => s.LocationCd).ToDictionary(g => g.Key, g => new
        {
            Qty = g.Sum(x => x.PhysicalQty),
            Allocated = g.Sum(x => x.AllocatedQty),
            Kinds = g.Select(x => x.ProductCd).Distinct().Count(),
            Top = g.OrderByDescending(x => x.PhysicalQty).Select(x => x.ProductCd).FirstOrDefault(),
        });

        var locs = await _db.Locations.Where(l => codes.Contains(l.LocationCd)).ToListAsync(ct);
        var locByCode = locs.GroupBy(l => l.LocationCd).ToDictionary(g => g.Key, g => g.First());

        var pickingCodes = await (
            from d in _db.OutboundOrderDetails
            where d.LocationCd != null && codes.Contains(d.LocationCd) && d.AllocatedQty > d.ShippedQty
            join o in _db.OutboundOrders on d.OutboundNo equals o.OutboundNo
            where o.Status == OutboundOrderStatus.Picking
            select d.LocationCd!).Distinct().ToListAsync(ct);
        var pickingSet = pickingCodes.ToHashSet();

        var result = new List<WmsStockDto>();
        foreach (var code in codes)
        {
            stockByLoc.TryGetValue(code, out var st);
            locByCode.TryGetValue(code, out var loc);
            if (st is null && loc is null) continue;   // 无数据 → 不返回

            var qty = st?.Qty ?? 0m;
            var cap = (loc is not null && loc.CapacityQty > 0) ? loc.CapacityQty : (decimal?)null;

            int status;
            if (loc?.IsBlocked == true)                 status = 3; // 锁定
            else if (pickingSet.Contains(code))         status = 4; // 在拣
            else if (cap.HasValue && qty >= cap.Value)  status = 2; // 满
            else if (qty > 0)                           status = 1; // 有货
            else                                        status = 0; // 空

            result.Add(new WmsStockDto
            {
                LocationCode = code, BinStatus = status, Qty = qty,
                AllocatedQty = st?.Allocated ?? 0m, Capacity = cap,
                TopMaterial = st?.Top, ProductKinds = st?.Kinds ?? 0,
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default)
    {
        var hasMat = !string.IsNullOrWhiteSpace(query.MaterialNo);
        var hasLot = !string.IsNullOrWhiteSpace(query.Lot);
        var hasCon = !string.IsNullOrWhiteSpace(query.Container);
        if (!hasMat && !hasLot && !hasCon) return Array.Empty<WmsLocationHit>();

        // 容器：经 Pallet 反查库位
        if (hasCon)
        {
            return await _db.Pallets
                .Where(p => p.PalletNo == query.Container && p.LocationCd != null)
                .GroupBy(p => p.LocationCd!)
                .Select(g => new WmsLocationHit { LocationCode = g.Key, Qty = 0m, Lot = null })
                .ToListAsync(ct);
        }

        // 物料/批次：经 Stock 反查
        var q = _db.Stocks.Where(s => s.PhysicalQty > 0);
        if (hasMat) q = q.Where(s => s.ProductCd == query.MaterialNo);
        if (hasLot) q = q.Where(s => s.LotNo == query.Lot);
        return await q
            .GroupBy(s => s.LocationCd)
            .Select(g => new WmsLocationHit
            {
                LocationCode = g.Key,
                Qty = g.Sum(x => x.PhysicalQty),
                Lot = hasLot ? query.Lot : null,
            })
            .ToListAsync(ct);
    }
}
