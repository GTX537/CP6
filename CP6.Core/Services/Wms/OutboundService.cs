using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 出庫サービス実装。
/// </summary>
/// <remarks>
/// 仕様書 §6 §9。
/// 採番 OUT = 出庫指示、PKG = 出荷梱包。
/// FIFO + 期限優先 引当：Stock を ReceiveDate ASC, ExpiryDate ASC で抽出して順次採用。
/// 第一版では 1 明細 = 1 ロット の引当に限定（単一 Stock 行で必要量を確保できないと例外）。
/// </remarks>
public class OutboundService : IOutboundService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;
    private readonly IWmsNotifier _notifier;

    private const string OrderPrefix = "OUT";
    private const string PackagePrefix = "PKG";

    public OutboundService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock, IWmsNotifier? notifier = null)
    {
        _db = db;
        _seq = seq;
        _stock = stock;
        _notifier = notifier ?? new NoOpWmsNotifier();
    }

    // ═══════════════════════════════════════════════════════════
    //  CRUD
    // ═══════════════════════════════════════════════════════════

    public async Task<List<OutboundOrder>> SearchOrdersAsync(OutboundOrderSearchQuery q)
    {
        var query = _db.OutboundOrders.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.OutboundNo)) query = query.Where(x => x.OutboundNo.Contains(q.OutboundNo));
        if (q.OutboundType.HasValue) query = query.Where(x => x.OutboundType == q.OutboundType.Value);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (!string.IsNullOrWhiteSpace(q.WorkOrderNo)) query = query.Where(x => x.WorkOrderNo == q.WorkOrderNo);
        if (!string.IsNullOrWhiteSpace(q.WebOrderNo)) query = query.Where(x => x.WebOrderNo == q.WebOrderNo);
        if (!string.IsNullOrWhiteSpace(q.CustomerCd)) query = query.Where(x => x.CustomerCd == q.CustomerCd);
        if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) query = query.Where(x => x.WarehouseCd == q.WarehouseCd);
        if (q.PlannedFrom.HasValue) query = query.Where(x => x.PlannedDate >= q.PlannedFrom.Value);
        if (q.PlannedTo.HasValue) query = query.Where(x => x.PlannedDate <= q.PlannedTo.Value);

        return await query
            .OrderByDescending(x => x.PlannedDate)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync();
    }

    public async Task<OutboundOrderDto?> GetOrderAsync(string outboundNo)
    {
        var h = await _db.OutboundOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted);
        if (h == null) return null;

        var details = await _db.OutboundOrderDetails.AsNoTracking()
            .Where(d => d.OutboundNo == outboundNo && !d.IsDeleted)
            .OrderBy(d => d.LineNo)
            .ToListAsync();

        return new OutboundOrderDto
        {
            OutboundNo = h.OutboundNo,
            OutboundType = h.OutboundType,
            WorkOrderNo = h.WorkOrderNo,
            WebOrderNo = h.WebOrderNo,
            CustomerCd = h.CustomerCd,
            CustomerName = h.CustomerName,
            WarehouseCd = h.WarehouseCd,
            PlannedDate = h.PlannedDate,
            Status = h.Status,
            Priority = h.Priority,
            ShipToAddress = h.ShipToAddress,
            CarrierCd = h.CarrierCd,
            Remarks = h.Remarks,
            Details = details.Select(d => new OutboundOrderDetailDto
            {
                LineNo = d.LineNo,
                ProductCd = d.ProductCd,
                ProductName = d.ProductName,
                RequiredQty = d.RequiredQty,
                AllocatedQty = d.AllocatedQty,
                ShippedQty = d.ShippedQty,
                LotNo = d.LotNo,
                LocationCd = d.LocationCd,
                UnitCd = d.UnitCd,
                UnitPrice = d.UnitPrice,
                AllocateTxnNo = d.AllocateTxnNo,
                ShipTxnNo = d.ShipTxnNo,
                Remarks = d.Remarks,
            }).ToList(),
        };
    }

    public async Task<string> CreateOrderAsync(OutboundOrderDto dto, string? userName)
    {
        ValidateOrder(dto);
        var no = await _seq.NextAsync(OrderPrefix);
        await CreateOrderInternalAsync(no, dto, userName);
        await _db.SaveChangesAsync();
        return no;
    }

    private Task CreateOrderInternalAsync(string outboundNo, OutboundOrderDto dto, string? userName)
    {
        var header = new OutboundOrder
        {
            OutboundNo = outboundNo,
            OutboundType = dto.OutboundType,
            WorkOrderNo = dto.WorkOrderNo,
            WebOrderNo = dto.WebOrderNo,
            CustomerCd = dto.CustomerCd,
            CustomerName = dto.CustomerName,
            WarehouseCd = dto.WarehouseCd,
            PlannedDate = dto.PlannedDate,
            Status = OutboundOrderStatus.Draft,
            Priority = dto.Priority == 0 ? 1 : dto.Priority,
            ShipToAddress = dto.ShipToAddress,
            CarrierCd = dto.CarrierCd,
            Remarks = dto.Remarks,
            Creator = userName,
        };
        _db.OutboundOrders.Add(header);

        int line = 1;
        foreach (var d in dto.Details)
        {
            _db.OutboundOrderDetails.Add(new OutboundOrderDetail
            {
                OutboundNo = outboundNo,
                LineNo = line++,
                ProductCd = d.ProductCd,
                ProductName = d.ProductName,
                RequiredQty = d.RequiredQty,
                UnitCd = d.UnitCd,
                UnitPrice = d.UnitPrice,
                Remarks = d.Remarks,
                Creator = userName,
            });
        }
        return Task.CompletedTask;
    }

    public async Task UpdateOrderAsync(string outboundNo, OutboundOrderDto dto, string? userName)
    {
        var header = await _db.OutboundOrders
            .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070: 出庫指示が見つかりません");

        if (header.Status != OutboundOrderStatus.Draft && header.Status != OutboundOrderStatus.Confirmed)
            throw new InvalidOperationException("WM-MSG-043: 当該ステータスでは訂正できません");

        ValidateOrder(dto);

        header.OutboundType = dto.OutboundType;
        header.WorkOrderNo = dto.WorkOrderNo;
        header.WebOrderNo = dto.WebOrderNo;
        header.CustomerCd = dto.CustomerCd;
        header.CustomerName = dto.CustomerName;
        header.WarehouseCd = dto.WarehouseCd;
        header.PlannedDate = dto.PlannedDate;
        header.Priority = dto.Priority;
        header.ShipToAddress = dto.ShipToAddress;
        header.CarrierCd = dto.CarrierCd;
        header.Remarks = dto.Remarks;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;

        var oldDetails = await _db.OutboundOrderDetails
            .Where(d => d.OutboundNo == outboundNo).ToListAsync();
        _db.OutboundOrderDetails.RemoveRange(oldDetails);

        int line = 1;
        foreach (var d in dto.Details)
        {
            _db.OutboundOrderDetails.Add(new OutboundOrderDetail
            {
                OutboundNo = outboundNo,
                LineNo = line++,
                ProductCd = d.ProductCd,
                ProductName = d.ProductName,
                RequiredQty = d.RequiredQty,
                UnitCd = d.UnitCd,
                UnitPrice = d.UnitPrice,
                Remarks = d.Remarks,
                Creator = userName,
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteOrderAsync(string outboundNo, string? userName)
    {
        var header = await _db.OutboundOrders
            .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

        if (header.Status != OutboundOrderStatus.Draft)
            throw new InvalidOperationException("WM-MSG-043: 確定済以降は削除不可");

        header.IsDeleted = true;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task ConfirmOrderAsync(string outboundNo, string? userName)
    {
        var header = await _db.OutboundOrders
            .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (header.Status != OutboundOrderStatus.Draft)
            throw new InvalidOperationException("WM-MSG-043: 下書きでないため確定不可");

        var detailCount = await _db.OutboundOrderDetails
            .CountAsync(d => d.OutboundNo == outboundNo && !d.IsDeleted);
        if (detailCount == 0)
            throw new InvalidOperationException("WM-MSG-020: 出庫明細が1件もありません");

        header.Status = OutboundOrderStatus.Confirmed;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CancelOrderAsync(string outboundNo, string? userName)
    {
        var header = await _db.OutboundOrders
            .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

        if (header.Status == OutboundOrderStatus.Completed)
            throw new InvalidOperationException("WM-MSG-043: 完了済の指示は取消不可");

        // 引当済の場合は UNRSV で在庫を戻す
        if (header.Status == OutboundOrderStatus.Allocated || header.Status == OutboundOrderStatus.Picking)
        {
            var details = await _db.OutboundOrderDetails
                .Where(d => d.OutboundNo == outboundNo && !d.IsDeleted)
                .ToListAsync();
            foreach (var d in details.Where(x => x.AllocatedQty > x.ShippedQty))
            {
                var releaseQty = d.AllocatedQty - d.ShippedQty;
                await _stock.ApplyAsync(new StockMovementRequest
                {
                    TxnType = WmsTxnType.UNRSV,
                    WarehouseCd = header.WarehouseCd,
                    LocationCd = d.LocationCd ?? "",
                    ProductCd = d.ProductCd,
                    LotNo = d.LotNo ?? "",
                    Qty = releaseQty,
                    UnitCd = d.UnitCd,
                    RelatedNo = outboundNo,
                    RelatedType = "OUTBOUND",
                    OperatorCd = userName,
                    Remark = $"取消による引当解除 {outboundNo}",
                });
                d.AllocatedQty -= releaseQty;
                d.Modifier = userName;
                d.ModifyDate = DateTime.Now;
            }
        }

        header.Status = OutboundOrderStatus.Cancelled;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Allocate（FIFO + 期限優先）
    // ═══════════════════════════════════════════════════════════

    public async Task AllocateAsync(string outboundNo, string? userName)
    {
        var header = await _db.OutboundOrders
            .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

        if (header.Status != OutboundOrderStatus.Confirmed)
            throw new InvalidOperationException("WM-MSG-043: 確定済でないため引当できません");

        var details = await _db.OutboundOrderDetails
            .Where(d => d.OutboundNo == outboundNo && !d.IsDeleted && d.AllocatedQty < d.RequiredQty)
            .OrderBy(d => d.LineNo)
            .ToListAsync();

        foreach (var d in details)
        {
            var needed = d.RequiredQty - d.AllocatedQty;
            if (needed <= 0) continue;

            // FIFO + 期限優先：単一の Stock 行で需要を賄えるものを探す
            var candidate = await _db.Stocks
                .Where(s => s.ProductCd == d.ProductCd
                            && s.WarehouseCd == header.WarehouseCd
                            && !s.IsDeleted
                            && !s.RecallFlag
                            && s.AvailableQty >= needed
                            && s.OwnerType == StockOwnerType.Self)
                .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(s => s.ReceiveDate ?? DateTime.MaxValue)
                .ThenBy(s => s.LotNo)
                .FirstOrDefaultAsync()
                ?? throw new InsufficientStockException(d.ProductCd, d.LotNo ?? "", needed, 0m);

            // 引当（RSV）
            var txnNo = await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.RSV,
                WarehouseCd = header.WarehouseCd,
                LocationCd = candidate.LocationCd,
                ProductCd = candidate.ProductCd,
                LotNo = candidate.LotNo,
                Qty = needed,
                UnitCd = candidate.UnitCd,
                UnitPrice = candidate.UnitPrice,
                RelatedNo = outboundNo,
                RelatedType = "OUTBOUND",
                OperatorCd = userName,
                Remark = $"引当 {outboundNo} 行 {d.LineNo}",
            });

            // 明細に引当結果を反映
            d.AllocatedQty += needed;
            d.LotNo = candidate.LotNo;
            d.LocationCd = candidate.LocationCd;
            d.UnitCd ??= candidate.UnitCd;
            d.UnitPrice ??= candidate.UnitPrice;
            d.AllocateTxnNo = txnNo;
            d.Modifier = userName;
            d.ModifyDate = DateTime.Now;
        }

        header.Status = OutboundOrderStatus.Allocated;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Ship（出庫確定）
    // ═══════════════════════════════════════════════════════════

    public async Task<string?> ShipAsync(string outboundNo, ShipRequest req, string? userName)
    {
        var header = await _db.OutboundOrders
            .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

        if (header.Status != OutboundOrderStatus.Allocated && header.Status != OutboundOrderStatus.Picking)
            throw new InvalidOperationException("WM-MSG-043: 引当済 or ピッキング中のみ出庫確定可");

        var details = await _db.OutboundOrderDetails
            .Where(d => d.OutboundNo == outboundNo && !d.IsDeleted && d.ShippedQty < d.AllocatedQty)
            .OrderBy(d => d.LineNo)
            .ToListAsync();

        if (details.Count == 0)
            throw new InvalidOperationException("WM-MSG-041: 出庫対象明細がありません");

        foreach (var d in details)
        {
            var shipQty = d.AllocatedQty - d.ShippedQty;
            if (shipQty <= 0) continue;

            // 出庫（OUT）— OUT が AllocatedQty も同時に減らすので、UNRSV は不要
            var txnNo = await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.OUT,
                WarehouseCd = header.WarehouseCd,
                LocationCd = d.LocationCd ?? "",
                ProductCd = d.ProductCd,
                LotNo = d.LotNo ?? "",
                Qty = shipQty,
                UnitCd = d.UnitCd,
                UnitPrice = d.UnitPrice,
                RelatedNo = outboundNo,
                RelatedType = "OUTBOUND",
                OperatorCd = userName,
                Remark = $"出庫確定 {outboundNo} 行 {d.LineNo}",
            });

            d.ShippedQty += shipQty;
            d.ShipTxnNo = txnNo;
            d.Modifier = userName;
            d.ModifyDate = DateTime.Now;
        }

        header.Status = OutboundOrderStatus.Completed;
        header.Modifier = userName;
        header.ModifyDate = DateTime.Now;

        // 出荷区分の場合は梱包レコードを作成
        string? packageNo = null;
        if (header.OutboundType == OutboundType.Shipping)
        {
            packageNo = await _seq.NextAsync(PackagePrefix);
            _db.ShippingPackages.Add(new ShippingPackage
            {
                PackageNo = packageNo,
                OutboundNo = outboundNo,
                CaseQty = req.CaseQty <= 0 ? 1 : req.CaseQty,
                TotalWeightKg = req.TotalWeightKg,
                TotalVolumeM3 = req.TotalVolumeM3,
                CarrierCd = req.CarrierCd ?? header.CarrierCd,
                TrackingNo = req.TrackingNo,
                DepartureTime = DateTime.Now,
                Remarks = req.Remarks,
                Creator = userName,
            });
        }

        await _db.SaveChangesAsync();

        try { await _notifier.NotifyOutboundShippedAsync(outboundNo, packageNo); }
        catch { /* best-effort */ }

        return packageNo;
    }

    // ═══════════════════════════════════════════════════════════
    //  自動展開
    // ═══════════════════════════════════════════════════════════

    public async Task<string> CreateFromWorkOrderAsync(string workOrderNo, string? userName)
    {
        // 二重生成防止
        var existing = await _db.OutboundOrders
            .Where(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted && x.Status != OutboundOrderStatus.Cancelled)
            .FirstOrDefaultAsync();
        if (existing != null)
            throw new InvalidOperationException($"WM-MSG-043: 既に該当指図 [{workOrderNo}] の材料出庫指示 [{existing.OutboundNo}] が生成されています");

        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException($"指図 [{workOrderNo}] が見つかりません");

        var materials = await _db.WorkOrderMaterials.AsNoTracking()
            .Where(m => m.WorkOrderNo == workOrderNo && !m.IsDeleted)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        if (materials.Count == 0)
            throw new InvalidOperationException($"指図 [{workOrderNo}] に必要材料が登録されていません");

        var no = await _seq.NextAsync(OrderPrefix);
        var dto = new OutboundOrderDto
        {
            OutboundType = OutboundType.Material,
            WorkOrderNo = workOrderNo,
            WarehouseCd = "W01", // 既定原材料倉庫。本来は ProductMaster.DefaultRawMaterialWarehouse などから取得
            PlannedDate = wo.PlanStartDate?.Date ?? DateTime.Today,
            Priority = wo.Priority == 0 ? 1 : wo.Priority,
            Remarks = $"製造指図 {workOrderNo} 自動展開",
            Details = materials.Select((m, i) => new OutboundOrderDetailDto
            {
                LineNo = i + 1,
                ProductCd = m.MaterialCd,
                ProductName = m.MaterialName,
                RequiredQty = m.PlanQty ?? 0,
                UnitCd = m.Unit,
                Remarks = m.Remarks,
            }).ToList(),
        };
        await CreateOrderInternalAsync(no, dto, userName);
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task<string> CreateFromOrderAsync(string webOrderNo, string? userName)
    {
        var existing = await _db.OutboundOrders
            .Where(x => x.WebOrderNo == webOrderNo && !x.IsDeleted && x.Status != OutboundOrderStatus.Cancelled)
            .FirstOrDefaultAsync();
        if (existing != null)
            throw new InvalidOperationException($"WM-MSG-043: 既に該当受注 [{webOrderNo}] の出荷指示 [{existing.OutboundNo}] が生成されています");

        var order = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WebOrderNo == webOrderNo && !x.IsDeleted)
            ?? throw new InvalidOperationException($"受注 [{webOrderNo}] が見つかりません");

        var orderDetails = await _db.OrderDetails.AsNoTracking()
            .Where(d => d.WebOrderNo == webOrderNo && !d.IsDeleted)
            .OrderBy(d => d.WebOrderDetailNo)
            .ToListAsync();

        if (orderDetails.Count == 0)
            throw new InvalidOperationException($"受注 [{webOrderNo}] に明細がありません");

        var no = await _seq.NextAsync(OrderPrefix);
        var dto = new OutboundOrderDto
        {
            OutboundType = OutboundType.Shipping,
            WebOrderNo = webOrderNo,
            CustomerCd = order.CustomerCd,
            CustomerName = null, // 必要なら BP マスタから取得
            WarehouseCd = "W01", // 既定完成品倉庫
            PlannedDate = DateTime.Today,
            Priority = 1,
            Remarks = $"受注 {webOrderNo} 自動展開",
            Details = orderDetails.Select((d, i) => new OutboundOrderDetailDto
            {
                LineNo = i + 1,
                ProductCd = d.ProductCd,
                RequiredQty = d.Quantity ?? 0,
                UnitCd = d.UnitPriceUnit,
                UnitPrice = d.IndividualUnitPrice,
            }).ToList(),
        };
        await CreateOrderInternalAsync(no, dto, userName);
        await _db.SaveChangesAsync();
        return no;
    }

    // ═══════════════════════════════════════════════════════════
    //  バリデーション
    // ═══════════════════════════════════════════════════════════

    private static void ValidateOrder(OutboundOrderDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd))
            throw new InvalidOperationException("出庫倉庫CDは必須です");
        if (dto.Details.Count == 0)
            throw new InvalidOperationException("WM-MSG-020: 出庫明細が1件もありません");
        for (int i = 0; i < dto.Details.Count; i++)
        {
            var d = dto.Details[i];
            if (string.IsNullOrWhiteSpace(d.ProductCd))
                throw new InvalidOperationException($"行 {i + 1}: 製品CDが未入力です");
            if (d.RequiredQty <= 0)
                throw new InvalidOperationException($"WM-MSG-021: 行 {i + 1}: 必要数量は0より大きい値を入力してください");
        }
    }
}
