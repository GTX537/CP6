using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// WMS ダッシュボード集計実装（EF Core ベース）。
/// </summary>
/// <remarks>
/// 仕様書 §11。
/// 第一版は EF Core 集計のみ。大規模化したら Dapper or SqlView へ移行予定。
/// 滞留品 = 90 日以内に IN/OUT/ADJ どれも発生していない Stock。
/// </remarks>
public partial class WmsDashboardService : IWmsDashboardService
{
    private readonly CP6Context _db;
    private readonly CP6.Core.Services.Integration.OperationsOwnerV1? _owner;
    public WmsDashboardService(CP6Context db, CP6.Core.Services.Integration.OperationsOwnerV1? owner=null) { _db=db; _owner=owner; }

    private async Task<WmsKpiDto> GetKpiCoreAsync(WmsDashboardData? data=null)
    {
        var today = DateTime.Today;
        var ninetyDaysAgo = today.AddDays(-90);

        // 在庫サマリ
        var stockSummary = await (data?.Stocks ?? _db.Stocks.AsNoTracking())
            .Where(s => !s.IsDeleted && s.PhysicalQty != 0m)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Value = g.Sum(s => s.PhysicalQty * (s.UnitPrice ?? 0m)),
                Skus = g.Select(s => s.ProductCd).Distinct().Count(),
                Physical = g.Sum(s => s.PhysicalQty),
                Allocated = g.Sum(s => s.AllocatedQty),
            })
            .FirstOrDefaultAsync();

        // 90 日以内に動いた ProductCd
        var movedProducts = await (data?.StockTransactions ?? _db.StockTransactions.AsNoTracking())
            .Where(t => t.TxnDateTime >= ninetyDaysAgo && !t.IsDeleted)
            .Select(t => t.ProductCd)
            .Distinct()
            .ToListAsync();
        var movedSet = new HashSet<string>(movedProducts);

        var allActiveSkus = await (data?.Stocks ?? _db.Stocks.AsNoTracking())
            .Where(s => !s.IsDeleted && s.PhysicalQty > 0m)
            .Select(s => s.ProductCd)
            .Distinct()
            .ToListAsync();
        var stagnantCount = allActiveSkus.Count(p => !movedSet.Contains(p));

        // 今日の予定
        var todayInbound = await (data?.InboundOrders ?? _db.InboundOrders.AsNoTracking())
            .CountAsync(x => !x.IsDeleted
                && x.ExpectedArrivalDate.Date == today
                && (x.Status == InboundOrderStatus.Confirmed
                    || x.Status == InboundOrderStatus.PartialReceived));

        var todayShipping = await (data?.OutboundOrders ?? _db.OutboundOrders.AsNoTracking())
            .CountAsync(x => !x.IsDeleted
                && x.OutboundType == OutboundType.Shipping
                && x.PlannedDate.Date == today
                && (x.Status == OutboundOrderStatus.Confirmed
                    || x.Status == OutboundOrderStatus.Allocated
                    || x.Status == OutboundOrderStatus.Picking));

        var openStockTakes = await (data?.StockTakes ?? _db.StockTakes.AsNoTracking())
            .CountAsync(x => !x.IsDeleted
                && x.Status != StockTakeStatus.Completed
                && x.Status != StockTakeStatus.Cancelled);

        return new WmsKpiDto
        {
            TotalStockValue = stockSummary?.Value ?? 0m,
            ActiveSkuCount = stockSummary?.Skus ?? 0,
            TotalPhysicalQty = stockSummary?.Physical ?? 0m,
            TotalAllocatedQty = stockSummary?.Allocated ?? 0m,
            StagnantSkuCount = stagnantCount,
            TodayInboundPlanCount = todayInbound,
            TodayShippingPlanCount = todayShipping,
            OpenStockTakeCount = openStockTakes,
        };
    }

    private async Task<List<WmsTrendPoint>> GetTransactionTrendCoreAsync(int days = 30,WmsDashboardData? data=null)
    {
        var fromDate = DateTime.Today.AddDays(-days + 1);
        var toDate = DateTime.Today.AddDays(1);

        var rawData = await (data?.StockTransactions ?? _db.StockTransactions.AsNoTracking())
            .Where(t => t.TxnDateTime >= fromDate && t.TxnDateTime < toDate && !t.IsDeleted)
            .GroupBy(t => new { Date = t.TxnDateTime.Date, t.TxnType })
            .Select(g => new { g.Key.Date, g.Key.TxnType, Qty = g.Sum(x => Math.Abs(x.Qty)) })
            .ToListAsync();

        // 日付シーケンスを生成
        var result = new List<WmsTrendPoint>();
        for (int i = 0; i < days; i++)
        {
            var d = fromDate.AddDays(i);
            var inQty = rawData.Where(r => r.Date == d && r.TxnType == WmsTxnType.IN).Sum(r => r.Qty);
            var outQty = rawData.Where(r => r.Date == d && r.TxnType == WmsTxnType.OUT).Sum(r => r.Qty);
            var adjQty = rawData.Where(r => r.Date == d && r.TxnType == WmsTxnType.ADJ).Sum(r => r.Qty);
            result.Add(new WmsTrendPoint
            {
                Date = d.ToString("yyyy-MM-dd"),
                InQty = inQty,
                OutQty = outQty,
                AdjQty = adjQty,
            });
        }
        return result;
    }

    private async Task<List<WmsWarehouseValueDto>> GetWarehouseValueCoreAsync(WmsDashboardData? data=null)
    {
        var stockByWh = await (data?.Stocks ?? _db.Stocks.AsNoTracking())
            .Where(s => !s.IsDeleted && s.PhysicalQty != 0m)
            .GroupBy(s => s.WarehouseCd)
            .Select(g => new
            {
                WarehouseCd = g.Key,
                Value = g.Sum(x => x.PhysicalQty * (x.UnitPrice ?? 0m)),
                Skus = g.Select(x => x.ProductCd).Distinct().Count(),
            })
            .ToListAsync();

        var whNames = await _db.Warehouses.AsNoTracking()
            .Where(w => !w.IsDeleted)
            .ToDictionaryAsync(w => w.WarehouseCd, w => w.WarehouseName);

        return stockByWh.Select(x => new WmsWarehouseValueDto
        {
            WarehouseCd = x.WarehouseCd,
            WarehouseName = whNames.GetValueOrDefault(x.WarehouseCd),
            StockValue = x.Value,
            SkuCount = x.Skus,
        }).OrderByDescending(x => x.StockValue).ToList();
    }

    private async Task<WmsAlertSummaryDto> GetAlertsCoreAsync(WmsDashboardData? data=null)
    {
        var today = DateTime.Today;
        var expiryThreshold = today.AddDays(30);

        // 賞味期限間近（30 日以内）
        var expiryItems = await (data?.Stocks ?? _db.Stocks.AsNoTracking())
            .Where(s => !s.IsDeleted
                && s.PhysicalQty > 0m
                && s.ExpiryDate != null
                && s.ExpiryDate <= expiryThreshold)
            .OrderBy(s => s.ExpiryDate)
            .Take(50)
            .Select(s => new ExpiryAlert
            {
                ProductCd = s.ProductCd,
                LotNo = s.LotNo,
                WarehouseCd = s.WarehouseCd,
                LocationCd = s.LocationCd,
                ExpiryDate = s.ExpiryDate,
                DaysUntilExpiry = 0,
                PhysicalQty = s.PhysicalQty,
            })
            .ToListAsync();

        // 入庫予定遅延
        var delayedInbounds = await (data?.InboundOrders ?? _db.InboundOrders.AsNoTracking())
            .Where(x => !x.IsDeleted
                && x.ExpectedArrivalDate < today
                && (x.Status == InboundOrderStatus.Confirmed
                    || x.Status == InboundOrderStatus.PartialReceived))
            .OrderBy(x => x.ExpectedArrivalDate)
            .Take(50)
            .Select(x => new DelayedInboundAlert
            {
                InboundNo = x.InboundNo,
                SupplierName = x.SupplierName,
                ExpectedArrivalDate = x.ExpectedArrivalDate,
                DelayDays = 0,
                Status = x.Status,
            })
            .ToListAsync();

        foreach(var row in expiryItems)row.DaysUntilExpiry=row.ExpiryDate.HasValue?(row.ExpiryDate.Value.Date-today).Days:0;
        foreach(var row in delayedInbounds)row.DelayDays=(today-row.ExpectedArrivalDate.Date).Days;

        var pendingApproval = await (data?.StockTakes ?? _db.StockTakes.AsNoTracking())
            .CountAsync(x => !x.IsDeleted && x.Status == StockTakeStatus.AwaitingApproval);

        return new WmsAlertSummaryDto
        {
            ExpiryAlerts = expiryItems,
            DelayedInbounds = delayedInbounds,
            PendingApprovalStockTakeCount = pendingApproval,
        };
    }
}
