using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>キット組立・バラシ サービス実装</summary>
public class KittingService : IKittingService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;
    private const string OrderPrefix = "KIT";

    public KittingService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock)
    {
        _db = db;
        _seq = seq;
        _stock = stock;
    }

    // ─── マスタ ───

    public async Task<List<KitMaster>> SearchMastersAsync(string? keyword)
    {
        var query = _db.KitMasters.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(x => x.KitSku.Contains(keyword) || x.KitName.Contains(keyword));
        return await query.OrderBy(x => x.KitSku).ToListAsync();
    }

    public async Task<KitMasterDto?> GetMasterAsync(string kitSku)
    {
        var m = await _db.KitMasters.AsNoTracking().FirstOrDefaultAsync(x => x.KitSku == kitSku && !x.IsDeleted);
        if (m == null) return null;
        var comps = await _db.KitMasterComponents.AsNoTracking()
            .Where(c => c.KitSku == kitSku && !c.IsDeleted)
            .OrderBy(c => c.LineNo).ToListAsync();
        return new KitMasterDto
        {
            KitSku = m.KitSku, KitName = m.KitName,
            DefaultWarehouseCd = m.DefaultWarehouseCd, Remarks = m.Remarks, ActiveFlg = m.ActiveFlg,
            Components = comps.Select(c => new KitMasterComponentDto
            {
                LineNo = c.LineNo, ComponentProductCd = c.ComponentProductCd,
                ComponentName = c.ComponentName, RequiredQty = c.RequiredQty,
                UnitCd = c.UnitCd, Remarks = c.Remarks,
            }).ToList(),
        };
    }

    public async Task CreateMasterAsync(KitMasterDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.KitSku) || string.IsNullOrWhiteSpace(dto.KitName))
            throw new InvalidOperationException("KitSku and KitName are required");
        if (dto.Components.Count == 0)
            throw new InvalidOperationException("WM-MSG-020: 構成部品が1件もありません");
        if (await _db.KitMasters.AnyAsync(x => x.KitSku == dto.KitSku && !x.IsDeleted))
            throw new InvalidOperationException($"WM-MSG-001: KitSku [{dto.KitSku}] は既に登録されています");

        _db.KitMasters.Add(new KitMaster
        {
            KitSku = dto.KitSku, KitName = dto.KitName,
            DefaultWarehouseCd = dto.DefaultWarehouseCd, Remarks = dto.Remarks,
            ActiveFlg = dto.ActiveFlg, Creator = userName,
        });
        int line = 1;
        foreach (var c in dto.Components)
        {
            if (string.IsNullOrWhiteSpace(c.ComponentProductCd) || c.RequiredQty <= 0)
                throw new InvalidOperationException($"WM-MSG-021: 行{line} 構成部品CD/数量(>0) 必須");
            _db.KitMasterComponents.Add(new KitMasterComponent
            {
                KitSku = dto.KitSku, LineNo = line++,
                ComponentProductCd = c.ComponentProductCd, ComponentName = c.ComponentName,
                RequiredQty = c.RequiredQty, UnitCd = c.UnitCd, Remarks = c.Remarks,
                Creator = userName,
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task UpdateMasterAsync(string kitSku, KitMasterDto dto, string? userName)
    {
        var m = await _db.KitMasters.FirstOrDefaultAsync(x => x.KitSku == kitSku && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (dto.Components.Count == 0)
            throw new InvalidOperationException("WM-MSG-020");

        m.KitName = dto.KitName;
        m.DefaultWarehouseCd = dto.DefaultWarehouseCd;
        m.Remarks = dto.Remarks;
        m.ActiveFlg = dto.ActiveFlg;
        m.Modifier = userName; m.ModifyDate = DateTime.Now;

        var old = await _db.KitMasterComponents.Where(c => c.KitSku == kitSku).ToListAsync();
        _db.KitMasterComponents.RemoveRange(old);
        int line = 1;
        foreach (var c in dto.Components)
        {
            _db.KitMasterComponents.Add(new KitMasterComponent
            {
                KitSku = kitSku, LineNo = line++,
                ComponentProductCd = c.ComponentProductCd, ComponentName = c.ComponentName,
                RequiredQty = c.RequiredQty, UnitCd = c.UnitCd, Remarks = c.Remarks,
                Creator = userName,
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteMasterAsync(string kitSku, string? userName)
    {
        var m = await _db.KitMasters.FirstOrDefaultAsync(x => x.KitSku == kitSku && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        // 実行中/実行済の指示が無い時のみ削除可
        var hasOrder = await _db.KitOrders.AnyAsync(o => o.KitSku == kitSku && !o.IsDeleted && o.Status != KitOrderStatus.Cancelled);
        if (hasOrder)
            throw new InvalidOperationException("WM-MSG-043: 関連する指示が存在するため削除不可");
        m.IsDeleted = true; m.Modifier = userName; m.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ─── 指示 ───

    public async Task<List<KitOrder>> SearchOrdersAsync(KitOrderSearchQuery q)
    {
        var query = _db.KitOrders.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.KitOrderNo)) query = query.Where(x => x.KitOrderNo.Contains(q.KitOrderNo));
        if (!string.IsNullOrWhiteSpace(q.KitSku)) query = query.Where(x => x.KitSku == q.KitSku);
        if (!string.IsNullOrWhiteSpace(q.Direction)) query = query.Where(x => x.Direction == q.Direction);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.ExecutedFrom.HasValue) query = query.Where(x => x.ExecutedAt >= q.ExecutedFrom.Value);
        if (q.ExecutedTo.HasValue) query = query.Where(x => x.ExecutedAt <= q.ExecutedTo.Value);
        return await query.OrderByDescending(x => x.CreateDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<KitOrder?> GetOrderAsync(string kitOrderNo)
        => await _db.KitOrders.AsNoTracking().FirstOrDefaultAsync(x => x.KitOrderNo == kitOrderNo && !x.IsDeleted);

    public async Task<string> CreateOrderAsync(KitOrderDto dto, string? userName)
    {
        if (dto.Qty <= 0) throw new InvalidOperationException("WM-MSG-021: Qty must be > 0");
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd) || string.IsNullOrWhiteSpace(dto.KitLocationCd))
            throw new InvalidOperationException("WarehouseCd / KitLocationCd is required");
        if (dto.Direction != KitOrderDirection.Assemble && dto.Direction != KitOrderDirection.Disassemble)
            throw new InvalidOperationException("Invalid Direction");

        var master = await _db.KitMasters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.KitSku == dto.KitSku && !x.IsDeleted)
            ?? throw new InvalidOperationException($"KitMaster [{dto.KitSku}] not found");

        var no = await _seq.NextAsync(OrderPrefix);
        _db.KitOrders.Add(new KitOrder
        {
            KitOrderNo = no, KitSku = dto.KitSku, KitName = master.KitName,
            Qty = dto.Qty, Direction = dto.Direction,
            WarehouseCd = dto.WarehouseCd, KitLocationCd = dto.KitLocationCd,
            KitLotNo = dto.KitLotNo,
            Status = KitOrderStatus.Draft,
            OperatorCd = dto.OperatorCd ?? userName, Remarks = dto.Remarks,
            Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task ExecuteAsync(string kitOrderNo, string? userName)
    {
        var order = await _db.KitOrders.FirstOrDefaultAsync(x => x.KitOrderNo == kitOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (order.Status != KitOrderStatus.Draft)
            throw new InvalidOperationException("WM-MSG-043: 下書きでないため実行不可");

        var components = await _db.KitMasterComponents.AsNoTracking()
            .Where(c => c.KitSku == order.KitSku && !c.IsDeleted)
            .OrderBy(c => c.LineNo).ToListAsync();
        if (components.Count == 0)
            throw new InvalidOperationException("WM-MSG-020: 構成部品マスタが空です");

        var kitLotNo = !string.IsNullOrWhiteSpace(order.KitLotNo)
            ? order.KitLotNo!
            : $"KIT{DateTime.Today:yyyyMMdd}-{order.KitOrderNo[^4..]}";

        var txnNos = new List<string>();

        if (order.Direction == KitOrderDirection.Assemble)
        {
            // 1. 部品 OUT × N（FIFO で最初に在庫がある単一ロットを採用）
            foreach (var c in components)
            {
                var needed = c.RequiredQty * order.Qty;
                var stock = await _db.Stocks
                    .Where(s => s.ProductCd == c.ComponentProductCd
                                && s.WarehouseCd == order.WarehouseCd
                                && !s.IsDeleted && !s.RecallFlag
                                && s.AvailableQty >= needed
                                && s.OwnerType == StockOwnerType.Self)
                    .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
                    .ThenBy(s => s.ReceiveDate ?? DateTime.MaxValue)
                    .FirstOrDefaultAsync()
                    ?? throw new InsufficientStockException(c.ComponentProductCd, "", needed, 0m);

                var txn = await _stock.ApplyAsync(new StockMovementRequest
                {
                    TxnType = WmsTxnType.OUT,
                    WarehouseCd = order.WarehouseCd,
                    LocationCd = stock.LocationCd,
                    ProductCd = c.ComponentProductCd,
                    LotNo = stock.LotNo,
                    Qty = needed,
                    UnitCd = c.UnitCd,
                    UnitPrice = stock.UnitPrice,
                    RelatedNo = kitOrderNo,
                    RelatedType = "KIT_ASSEMBLE",
                    OperatorCd = userName,
                    Remark = $"キット組立 {kitOrderNo} 部品 OUT",
                });
                txnNos.Add(txn);
            }

            // 2. キット品 IN × 1（Qty 分）
            var inTxn = await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.IN,
                WarehouseCd = order.WarehouseCd,
                LocationCd = order.KitLocationCd,
                ProductCd = order.KitSku,
                LotNo = kitLotNo,
                Qty = order.Qty,
                RelatedNo = kitOrderNo,
                RelatedType = "KIT_ASSEMBLE",
                OperatorCd = userName,
                Remark = $"キット組立 {kitOrderNo} キット IN",
            });
            txnNos.Add(inTxn);
            order.KitLotNo = kitLotNo;
        }
        else // DISASSEMBLE
        {
            if (string.IsNullOrWhiteSpace(order.KitLotNo))
                throw new InvalidOperationException("KitLotNo is required for DISASSEMBLE");

            // 1. キット品 OUT × 1（Qty 分）
            var outTxn = await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.OUT,
                WarehouseCd = order.WarehouseCd,
                LocationCd = order.KitLocationCd,
                ProductCd = order.KitSku,
                LotNo = order.KitLotNo,
                Qty = order.Qty,
                RelatedNo = kitOrderNo,
                RelatedType = "KIT_DISASSEMBLE",
                OperatorCd = userName,
                Remark = $"キットバラシ {kitOrderNo} キット OUT",
            });
            txnNos.Add(outTxn);

            // 2. 部品 IN × N（kit と同じロケーションに戻す。ロットは新規採番）
            foreach (var c in components)
            {
                var compLot = $"DKIT{DateTime.Today:yyyyMMdd}-{order.KitOrderNo[^4..]}-{c.LineNo:D2}";
                var inTxn = await _stock.ApplyAsync(new StockMovementRequest
                {
                    TxnType = WmsTxnType.IN,
                    WarehouseCd = order.WarehouseCd,
                    LocationCd = order.KitLocationCd,
                    ProductCd = c.ComponentProductCd,
                    LotNo = compLot,
                    Qty = c.RequiredQty * order.Qty,
                    UnitCd = c.UnitCd,
                    RelatedNo = kitOrderNo,
                    RelatedType = "KIT_DISASSEMBLE",
                    OperatorCd = userName,
                    Remark = $"キットバラシ {kitOrderNo} 部品 IN",
                });
                txnNos.Add(inTxn);
            }
        }

        order.Status = KitOrderStatus.Executed;
        order.ExecutedAt = DateTime.Now;
        order.ExecutedTxnNos = string.Join(";", txnNos);
        order.Modifier = userName; order.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(string kitOrderNo, string? userName)
    {
        var order = await _db.KitOrders.FirstOrDefaultAsync(x => x.KitOrderNo == kitOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (order.Status == KitOrderStatus.Executed)
            throw new InvalidOperationException("WM-MSG-043: 実行済は取消不可（在庫戻しは逆方向の指示で対応）");
        order.Status = KitOrderStatus.Cancelled;
        order.Modifier = userName; order.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
