using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 入庫サービス実装。
/// </summary>
/// <remarks>
/// 仕様書 §4 §5。
/// 採番：IN = 入庫予定、RC = 入庫実績、TXN = StockMovementService 経由で発行される在庫履歴NO。
/// 確定処理は StockMovementService.ApplyAsync(IN) を明細件数分呼ぶ。
/// </remarks>
public class InboundService : IInboundService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;
    private readonly IWmsNotifier _notifier;

    private const string OrderPrefix = "IN";
    private const string ReceiptPrefix = "RC";

    public InboundService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock, IWmsNotifier? notifier = null)
    {
        _db = db;
        _seq = seq;
        _stock = stock;
        _notifier = notifier ?? new NoOpWmsNotifier();
    }

    // ═══════════════════════════════════════════════════════════
    //  WM030 入庫予定
    // ═══════════════════════════════════════════════════════════

    public async Task<List<InboundOrder>> SearchOrdersAsync(InboundOrderSearchQuery q)
    {
        var query = _db.InboundOrders.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.InboundNo)) query = query.Where(x => x.InboundNo.Contains(q.InboundNo));
        if (!string.IsNullOrWhiteSpace(q.SupplierCd)) query = query.Where(x => x.SupplierCd == q.SupplierCd);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) query = query.Where(x => x.WarehouseCd == q.WarehouseCd);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.ArrivalFrom.HasValue) query = query.Where(x => x.ExpectedArrivalDate >= q.ArrivalFrom.Value);
        if (q.ArrivalTo.HasValue) query = query.Where(x => x.ExpectedArrivalDate <= q.ArrivalTo.Value);

        return await query
            .OrderByDescending(x => x.ExpectedArrivalDate)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync();
    }

    public async Task<InboundOrderDto?> GetOrderAsync(string inboundNo)
    {
        var h = await _db.InboundOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.InboundNo == inboundNo && !x.IsDeleted);
        if (h == null) return null;

        var details = await _db.InboundOrderDetails.AsNoTracking()
            .Where(d => d.InboundNo == inboundNo && !d.IsDeleted)
            .OrderBy(d => d.LineNo)
            .ToListAsync();

        return new InboundOrderDto
        {
            InboundNo = h.InboundNo,
            InboundType = h.InboundType,
            SupplierCd = h.SupplierCd,
            SupplierName = h.SupplierName,
            PoNo = h.PoNo,
            ExpectedArrivalDate = h.ExpectedArrivalDate,
            WarehouseCd = h.WarehouseCd,
            Status = h.Status,
            Remarks = h.Remarks,
            Details = details.Select(d => new InboundOrderDetailDto
            {
                LineNo = d.LineNo,
                ProductCd = d.ProductCd,
                ProductName = d.ProductName,
                LotNo = d.LotNo,
                ExpectedQty = d.ExpectedQty,
                ReceivedQty = d.ReceivedQty,
                UnitCd = d.UnitCd,
                ExpectedLocationCd = d.ExpectedLocationCd,
                UnitPrice = d.UnitPrice,
                Remarks = d.Remarks,
            }).ToList(),
        };
    }

    public async Task<string> CreateOrderAsync(InboundOrderDto dto, string? userName)
    {
        ValidateOrder(dto);
        var no = await _seq.NextAsync(OrderPrefix);

        var header = new InboundOrder
        {
            InboundNo = no,
            InboundType = dto.InboundType,
            SupplierCd = dto.SupplierCd,
            SupplierName = dto.SupplierName,
            PoNo = dto.PoNo,
            ExpectedArrivalDate = dto.ExpectedArrivalDate,
            WarehouseCd = dto.WarehouseCd,
            Status = InboundOrderStatus.Draft,
            Remarks = dto.Remarks,
            Creator = userName,
        };
        _db.InboundOrders.Add(header);

        int line = 1;
        foreach (var d in dto.Details)
        {
            _db.InboundOrderDetails.Add(new InboundOrderDetail
            {
                InboundNo = no,
                LineNo = line++,
                ProductCd = d.ProductCd,
                ProductName = d.ProductName,
                LotNo = d.LotNo,
                ExpectedQty = d.ExpectedQty,
                UnitCd = d.UnitCd,
                ExpectedLocationCd = d.ExpectedLocationCd,
                UnitPrice = d.UnitPrice,
                Remarks = d.Remarks,
                Creator = userName,
            });
        }

        await _db.SaveChangesAsync();
        return no;
    }

    public async Task UpdateOrderAsync(string inboundNo, InboundOrderDto dto, string? userName)
    {
        var header = await _db.InboundOrders
            .FirstOrDefaultAsync(x => x.InboundNo == inboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070: 入庫予定が見つかりません");

        if (header.Status != InboundOrderStatus.Draft && header.Status != InboundOrderStatus.Confirmed)
            throw new InvalidOperationException("WM-MSG-043: 当該ステータスでは訂正できません");

        ValidateOrder(dto);

        header.InboundType = dto.InboundType;
        header.SupplierCd = dto.SupplierCd;
        header.SupplierName = dto.SupplierName;
        header.PoNo = dto.PoNo;
        header.ExpectedArrivalDate = dto.ExpectedArrivalDate;
        header.WarehouseCd = dto.WarehouseCd;
        header.Remarks = dto.Remarks;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;

        // 明細：差分が複雑なので一括 物理削除→再 INSERT（ReceivedQty が累計値なので、訂正時は受領済明細以外のみ可）
        var oldDetails = await _db.InboundOrderDetails
            .Where(d => d.InboundNo == inboundNo).ToListAsync();
        _db.InboundOrderDetails.RemoveRange(oldDetails);

        int line = 1;
        foreach (var d in dto.Details)
        {
            _db.InboundOrderDetails.Add(new InboundOrderDetail
            {
                InboundNo = inboundNo,
                LineNo = line++,
                ProductCd = d.ProductCd,
                ProductName = d.ProductName,
                LotNo = d.LotNo,
                ExpectedQty = d.ExpectedQty,
                ReceivedQty = d.ReceivedQty, // 既往の累計は維持
                UnitCd = d.UnitCd,
                ExpectedLocationCd = d.ExpectedLocationCd,
                UnitPrice = d.UnitPrice,
                Remarks = d.Remarks,
                Creator = userName,
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteOrderAsync(string inboundNo, string? userName)
    {
        var header = await _db.InboundOrders
            .FirstOrDefaultAsync(x => x.InboundNo == inboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

        if (header.Status != InboundOrderStatus.Draft)
            throw new InvalidOperationException("WM-MSG-043: 確定済以降は削除不可");

        header.IsDeleted = true;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task ConfirmOrderAsync(string inboundNo, string? userName)
    {
        var header = await _db.InboundOrders
            .FirstOrDefaultAsync(x => x.InboundNo == inboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

        if (header.Status != InboundOrderStatus.Draft)
            throw new InvalidOperationException("WM-MSG-043: 下書きでないため確定不可");

        var detailCount = await _db.InboundOrderDetails
            .CountAsync(d => d.InboundNo == inboundNo && !d.IsDeleted);
        if (detailCount == 0)
            throw new InvalidOperationException("WM-MSG-020: 入庫明細が1件もありません");

        header.Status = InboundOrderStatus.Confirmed;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CancelOrderAsync(string inboundNo, string? userName)
    {
        var header = await _db.InboundOrders
            .FirstOrDefaultAsync(x => x.InboundNo == inboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

        if (header.Status == InboundOrderStatus.Completed)
            throw new InvalidOperationException("WM-MSG-043: 完了済の予定は取消不可");

        header.Status = InboundOrderStatus.Cancelled;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  WM040 入庫実績
    // ═══════════════════════════════════════════════════════════

    public async Task<List<InboundReceipt>> SearchReceiptsAsync(InboundReceiptSearchQuery q)
    {
        var query = _db.InboundReceipts.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.ReceiptNo)) query = query.Where(x => x.ReceiptNo.Contains(q.ReceiptNo));
        if (!string.IsNullOrWhiteSpace(q.InboundNo)) query = query.Where(x => x.InboundNo == q.InboundNo);
        if (!string.IsNullOrWhiteSpace(q.WorkOrderNo)) query = query.Where(x => x.WorkOrderNo == q.WorkOrderNo);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) query = query.Where(x => x.WarehouseCd == q.WarehouseCd);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.DateFrom.HasValue) query = query.Where(x => x.ReceiveDateTime >= q.DateFrom.Value);
        if (q.DateTo.HasValue) query = query.Where(x => x.ReceiveDateTime <= q.DateTo.Value);

        return await query
            .OrderByDescending(x => x.ReceiveDateTime)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync();
    }

    public async Task<InboundReceiptDto?> GetReceiptAsync(string receiptNo)
    {
        var h = await _db.InboundReceipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ReceiptNo == receiptNo && !x.IsDeleted);
        if (h == null) return null;

        var details = await _db.InboundReceiptDetails.AsNoTracking()
            .Where(d => d.ReceiptNo == receiptNo && !d.IsDeleted)
            .OrderBy(d => d.LineNo)
            .ToListAsync();

        return new InboundReceiptDto
        {
            ReceiptNo = h.ReceiptNo,
            InboundNo = h.InboundNo,
            SourceType = h.SourceType,
            WorkOrderNo = h.WorkOrderNo,
            ReceiveDateTime = h.ReceiveDateTime,
            OperatorCd = h.OperatorCd,
            WarehouseCd = h.WarehouseCd,
            Status = h.Status,
            Remarks = h.Remarks,
            Details = details.Select(d => new InboundReceiptDetailDto
            {
                LineNo = d.LineNo,
                RefOrderLineNo = d.RefOrderLineNo,
                ProductCd = d.ProductCd,
                ProductName = d.ProductName,
                LotNo = d.LotNo,
                ReceivedQty = d.ReceivedQty,
                UnitCd = d.UnitCd,
                LocationCd = d.LocationCd,
                UnitPrice = d.UnitPrice,
                ExpiryDate = d.ExpiryDate,
                PaperRollNo = d.PaperRollNo,
                StockTxnNo = d.StockTxnNo,
                Remarks = d.Remarks,
            }).ToList(),
        };
    }

    public async Task<string> ConfirmReceiptAsync(InboundReceiptDto dto, string? userName)
    {
        ValidateReceipt(dto);

        // 予定参照モード：予定が存在し、確定済 or 入庫中である必要
        InboundOrder? linkedOrder = null;
        Dictionary<int, InboundOrderDetail>? orderDetailMap = null;

        if (!string.IsNullOrWhiteSpace(dto.InboundNo))
        {
            linkedOrder = await _db.InboundOrders
                .FirstOrDefaultAsync(x => x.InboundNo == dto.InboundNo && !x.IsDeleted)
                ?? throw new InvalidOperationException($"WM-MSG-070: 入庫予定[{dto.InboundNo}]が見つかりません");
            if (linkedOrder.Status != InboundOrderStatus.Confirmed && linkedOrder.Status != InboundOrderStatus.PartialReceived)
                throw new InvalidOperationException("WM-MSG-043: 予定が確定済 or 入庫中ではありません");

            var details = await _db.InboundOrderDetails
                .Where(d => d.InboundNo == dto.InboundNo && !d.IsDeleted)
                .ToListAsync();
            orderDetailMap = details.ToDictionary(d => d.LineNo, d => d);
        }

        var receiptNo = await _seq.NextAsync(ReceiptPrefix);

        var header = new InboundReceipt
        {
            ReceiptNo = receiptNo,
            InboundNo = dto.InboundNo,
            SourceType = dto.SourceType,
            WorkOrderNo = dto.WorkOrderNo,
            ReceiveDateTime = dto.ReceiveDateTime == default ? DateTime.Now : dto.ReceiveDateTime,
            OperatorCd = dto.OperatorCd ?? userName,
            WarehouseCd = dto.WarehouseCd,
            Status = InboundReceiptStatus.Confirmed,
            Remarks = dto.Remarks,
            Creator = userName,
        };
        _db.InboundReceipts.Add(header);

        int line = 1;
        foreach (var d in dto.Details)
        {
            // StockMovementService 経由で IN トランザクションを発行
            var txnNo = await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.IN,
                WarehouseCd = dto.WarehouseCd,
                LocationCd = d.LocationCd,
                ProductCd = d.ProductCd,
                LotNo = d.LotNo ?? "",
                Qty = d.ReceivedQty,
                UnitCd = d.UnitCd,
                UnitPrice = d.UnitPrice,
                RelatedNo = receiptNo,
                RelatedType = "INBOUND",
                OperatorCd = dto.OperatorCd ?? userName,
                Remark = $"入庫実績 {receiptNo}",
                ReceiveDate = header.ReceiveDateTime,
                ExpiryDate = d.ExpiryDate,
                PaperRollNo = d.PaperRollNo,
            });

            _db.InboundReceiptDetails.Add(new InboundReceiptDetail
            {
                ReceiptNo = receiptNo,
                LineNo = line++,
                RefOrderLineNo = d.RefOrderLineNo,
                ProductCd = d.ProductCd,
                ProductName = d.ProductName,
                LotNo = d.LotNo,
                ReceivedQty = d.ReceivedQty,
                UnitCd = d.UnitCd,
                LocationCd = d.LocationCd,
                UnitPrice = d.UnitPrice,
                ExpiryDate = d.ExpiryDate,
                PaperRollNo = d.PaperRollNo,
                StockTxnNo = txnNo,
                Remarks = d.Remarks,
                Creator = userName,
            });

            // 予定明細の ReceivedQty 累計を加算
            if (orderDetailMap != null && d.RefOrderLineNo.HasValue
                && orderDetailMap.TryGetValue(d.RefOrderLineNo.Value, out var od))
            {
                od.ReceivedQty += d.ReceivedQty;
                od.Modifier = userName;
                od.ModifyDate = DateTime.Now;
            }
        }

        await _db.SaveChangesAsync();

        // 予定ステータス自動遷移
        if (linkedOrder != null)
        {
            var allLines = await _db.InboundOrderDetails
                .Where(x => x.InboundNo == linkedOrder.InboundNo && !x.IsDeleted)
                .ToListAsync();
            bool allReceived = allLines.All(x => x.ReceivedQty >= x.ExpectedQty);
            bool anyReceived = allLines.Any(x => x.ReceivedQty > 0);

            int newStatus = allReceived
                ? InboundOrderStatus.Completed
                : (anyReceived ? InboundOrderStatus.PartialReceived : linkedOrder.Status);

            if (newStatus != linkedOrder.Status)
            {
                linkedOrder.Status = newStatus;
                linkedOrder.Modifier = userName;
                linkedOrder.ModifyDate = DateTime.Now;
                await _db.SaveChangesAsync();
            }
        }

        try { await _notifier.NotifyInboundReceivedAsync(receiptNo, dto.WarehouseCd); }
        catch { /* best-effort */ }

        return receiptNo;
    }

    // ───────── バリデーション ─────────

    private static void ValidateOrder(InboundOrderDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd))
            throw new InvalidOperationException("入庫倉庫CDは必須です");
        if (dto.Details.Count == 0)
            throw new InvalidOperationException("WM-MSG-020: 入庫明細が1件もありません");
        for (int i = 0; i < dto.Details.Count; i++)
        {
            var d = dto.Details[i];
            if (string.IsNullOrWhiteSpace(d.ProductCd))
                throw new InvalidOperationException($"行 {i + 1}: 製品CDが未入力です");
            if (d.ExpectedQty <= 0)
                throw new InvalidOperationException($"WM-MSG-021: 行 {i + 1}: 予定数量は0より大きい値を入力してください");
        }
    }

    private static void ValidateReceipt(InboundReceiptDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd))
            throw new InvalidOperationException("入庫倉庫CDは必須です");
        if (dto.Details.Count == 0)
            throw new InvalidOperationException("WM-MSG-031: 受入明細が1件もありません");
        for (int i = 0; i < dto.Details.Count; i++)
        {
            var d = dto.Details[i];
            if (string.IsNullOrWhiteSpace(d.ProductCd))
                throw new InvalidOperationException($"行 {i + 1}: 製品CDが未入力です");
            if (string.IsNullOrWhiteSpace(d.LocationCd))
                throw new InvalidOperationException($"WM-MSG-031: 行 {i + 1}: 配置先ロケーションが未指定です");
            if (string.IsNullOrWhiteSpace(d.LotNo))
                d.LotNo = ""; // 空文字 = no-lot
            if (d.ReceivedQty <= 0)
                throw new InvalidOperationException($"WM-MSG-031: 行 {i + 1}: 受入数量は0より大きい値を入力してください");
        }
    }
}
