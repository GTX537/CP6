using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>賞味期限管理サービス実装</summary>
public partial class ExpiryService : IExpiryService
{
    private readonly CP6Context _db;
    private readonly IStockMovementService _stock;

    private readonly CP6.Core.Services.Integration.OperationsOwnerV1? _owner;

    public ExpiryService(CP6Context db, IStockMovementService stock, CP6.Core.Services.Integration.OperationsOwnerV1? owner = null)
    {
        _db = db; _owner = owner;
        _stock = stock;
    }

    private async Task<List<ExpiryStockDto>> GetExpiringStocksCoreAsync(int days = 30, string? warehouseCd = null)
    {
        var threshold = DateTime.Today.AddDays(days);
        var query = _db.Stocks.AsNoTracking()
            .Where(s => !s.IsDeleted && s.PhysicalQty > 0 && s.ExpiryDate != null && s.ExpiryDate <= threshold);
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            query = query.Where(s => s.WarehouseCd == warehouseCd);

        var list = await query
            .OrderBy(s => s.ExpiryDate)
            .Take(500)
            .ToListAsync();

        var today = DateTime.Today;
        return list.Select(s => new ExpiryStockDto
        {
            StockId = s.Id,
            WarehouseCd = s.WarehouseCd,
            LocationCd = s.LocationCd,
            ProductCd = s.ProductCd,
            LotNo = s.LotNo,
            PhysicalQty = s.PhysicalQty,
            AvailableQty = s.AvailableQty,
            UnitPrice = s.UnitPrice,
            ExpiryDate = s.ExpiryDate,
            DaysUntilExpiry = s.ExpiryDate.HasValue ? (int)(s.ExpiryDate.Value.Date - today).TotalDays : 0,
            UnitCd = s.UnitCd,
            LossAmount = s.UnitPrice.HasValue ? s.UnitPrice.Value * s.PhysicalQty : null,
        }).ToList();
    }

    public async Task<int> DisposeAsync(DisposeRequest req, string? userName)
    {
        // Legacy ADJ does not carry the approved immutable DISP basis or an atomic disposition Owner.
        if (_db.Database.IsNpgsql()) throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("DISPOSITION_OWNER_REQUIRED", 503);
        if (req.StockIds.Count == 0) return 0;

        var stocks = await _db.Stocks.AsNoTracking()
            .Where(s => req.StockIds.Contains(s.Id) && !s.IsDeleted && s.PhysicalQty > 0)
            .ToListAsync();

        int disposed = 0;
        foreach (var s in stocks)
        {
            // 廃棄 = ADJ で物理在庫を 0 にする
            await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.ADJ,
                WarehouseCd = s.WarehouseCd,
                LocationCd = s.LocationCd,
                ProductCd = s.ProductCd,
                LotNo = s.LotNo,
                Qty = -s.PhysicalQty, // 全数 -
                UnitCd = s.UnitCd,
                UnitPrice = s.UnitPrice,
                RelatedType = "EXPIRY_DISPOSE",
                OperatorCd = userName,
                Remark = string.IsNullOrWhiteSpace(req.Reason) ? "賞味期限切れ自動廃棄" : req.Reason,
            });
            disposed++;
        }
        return disposed;
    }
}
