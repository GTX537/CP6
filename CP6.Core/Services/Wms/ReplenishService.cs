using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class ReplenishService : IReplenishService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;
    private const string Prefix = "RPL";
    private const string ForwardPrefix = "PIK-";   // ピッキング棚
    private const string ReservePrefix = "RES-";   // 保管棚

    public ReplenishService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock)
    {
        _db = db; _seq = seq; _stock = stock;
    }

    public async Task<List<ReplenishOrder>> SearchAsync(ReplenishSearchQuery q)
    {
        var query = _db.ReplenishOrders.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.ReplenishNo)) query = query.Where(x => x.ReplenishNo.Contains(q.ReplenishNo));
        if (!string.IsNullOrWhiteSpace(q.ProductCd)) query = query.Where(x => x.ProductCd == q.ProductCd);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) query = query.Where(x => x.WarehouseCd == q.WarehouseCd);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.Priority.HasValue) query = query.Where(x => x.Priority == q.Priority.Value);
        return await query.OrderBy(x => x.Priority).ThenByDescending(x => x.CreateDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<ReplenishOrder?> GetAsync(string replenishNo)
        => await _db.ReplenishOrders.AsNoTracking().FirstOrDefaultAsync(x => x.ReplenishNo == replenishNo && !x.IsDeleted);

    public async Task<string> CreateAsync(ReplenishOrderDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.ProductCd) || string.IsNullOrWhiteSpace(dto.WarehouseCd)
            || string.IsNullOrWhiteSpace(dto.FromLocationCd) || string.IsNullOrWhiteSpace(dto.ToLocationCd))
            throw new InvalidOperationException("Required fields missing");
        if (dto.Qty <= 0) throw new InvalidOperationException("WM-MSG-021");

        var no = await _seq.NextAsync(Prefix);
        _db.ReplenishOrders.Add(new ReplenishOrder
        {
            ReplenishNo = no, Priority = dto.Priority == 0 ? 2 : dto.Priority,
            ProductCd = dto.ProductCd, ProductName = dto.ProductName,
            WarehouseCd = dto.WarehouseCd,
            FromLocationCd = dto.FromLocationCd, ToLocationCd = dto.ToLocationCd,
            LotNo = dto.LotNo ?? "", Qty = dto.Qty, UnitCd = dto.UnitCd,
            TriggerType = dto.TriggerType ?? ReplenishTrigger.Manual,
            Status = ReplenishStatus.Pending,
            OperatorCd = dto.OperatorCd ?? userName, Remarks = dto.Remarks,
            Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task<int> GenerateBatchAsync(string warehouseCd, decimal minQty, string? userName)
    {
        if (string.IsNullOrWhiteSpace(warehouseCd))
            throw new InvalidOperationException("warehouseCd required");
        if (minQty <= 0) minQty = 10m; // 既定 10

        // ピッキング棚（PIK-*）で minQty 未満の在庫を抽出
        var lowStocks = await _db.Stocks.AsNoTracking()
            .Where(s => s.WarehouseCd == warehouseCd
                        && s.LocationCd.StartsWith(ForwardPrefix)
                        && !s.IsDeleted && !s.RecallFlag
                        && s.PhysicalQty < minQty
                        && s.OwnerType == StockOwnerType.Self)
            .ToListAsync();

        // 同 ProductCd で 保管棚（RES-*）に在庫あり の組合せ
        int created = 0;
        foreach (var low in lowStocks)
        {
            // 既存の未実行補充指示があればスキップ（重複防止）
            var dup = await _db.ReplenishOrders.AnyAsync(x =>
                !x.IsDeleted && x.Status == ReplenishStatus.Pending
                && x.WarehouseCd == warehouseCd
                && x.ProductCd == low.ProductCd
                && x.ToLocationCd == low.LocationCd);
            if (dup) continue;

            var source = await _db.Stocks.AsNoTracking()
                .Where(s => s.WarehouseCd == warehouseCd
                            && s.ProductCd == low.ProductCd
                            && s.LocationCd.StartsWith(ReservePrefix)
                            && !s.IsDeleted && !s.RecallFlag
                            && s.AvailableQty > 0
                            && s.OwnerType == StockOwnerType.Self)
                .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(s => s.ReceiveDate ?? DateTime.MaxValue)
                .FirstOrDefaultAsync();
            if (source == null) continue;

            var needed = minQty - low.PhysicalQty;
            var actualQty = Math.Min(needed, source.AvailableQty);
            if (actualQty <= 0) continue;

            var no = await _seq.NextAsync(Prefix);
            _db.ReplenishOrders.Add(new ReplenishOrder
            {
                ReplenishNo = no, Priority = 2,
                ProductCd = low.ProductCd, ProductName = null,
                WarehouseCd = warehouseCd,
                FromLocationCd = source.LocationCd, ToLocationCd = low.LocationCd,
                LotNo = source.LotNo, Qty = actualQty,
                UnitCd = source.UnitCd,
                TriggerType = ReplenishTrigger.Batch,
                Status = ReplenishStatus.Pending,
                OperatorCd = userName,
                Remarks = $"バッチ自動：MinQty={minQty}, 現状={low.PhysicalQty}",
                Creator = userName,
            });
            created++;
        }
        await _db.SaveChangesAsync();
        return created;
    }

    public async Task ExecuteAsync(string replenishNo, string? userName)
    {
        var order = await _db.ReplenishOrders.FirstOrDefaultAsync(x => x.ReplenishNo == replenishNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (order.Status != ReplenishStatus.Pending)
            throw new InvalidOperationException("WM-MSG-043: 未実行でないため実行不可");

        // MOVE：源 OUT
        var outTxn = await _stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.MOVE,
            WarehouseCd = order.WarehouseCd,
            LocationCd = order.FromLocationCd,
            ProductCd = order.ProductCd, LotNo = order.LotNo,
            Qty = -order.Qty, UnitCd = order.UnitCd,
            RelatedNo = replenishNo, RelatedType = "REPLENISH",
            OperatorCd = userName,
            Remark = $"補充 {replenishNo} 源 OUT",
        });
        // MOVE：先 IN
        var inTxn = await _stock.ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.MOVE,
            WarehouseCd = order.WarehouseCd,
            LocationCd = order.ToLocationCd,
            ProductCd = order.ProductCd, LotNo = order.LotNo,
            Qty = order.Qty, UnitCd = order.UnitCd,
            RelatedNo = replenishNo, RelatedType = "REPLENISH",
            OperatorCd = userName,
            Remark = $"補充 {replenishNo} 先 IN",
        });

        order.OutTxnNo = outTxn;
        order.InTxnNo = inTxn;
        order.Status = ReplenishStatus.Executed;
        order.ExecutedAt = DateTime.Now;
        order.Modifier = userName; order.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(string replenishNo, string? userName)
    {
        var order = await _db.ReplenishOrders.FirstOrDefaultAsync(x => x.ReplenishNo == replenishNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (order.Status == ReplenishStatus.Executed)
            throw new InvalidOperationException("WM-MSG-043: 実行済は取消不可");
        order.Status = ReplenishStatus.Cancelled;
        order.Modifier = userName; order.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
