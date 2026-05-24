using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>ロット追溯サービス実装（純クエリ）</summary>
public class LotTraceService : ILotTraceService
{
    private readonly CP6Context _db;

    public LotTraceService(CP6Context db) => _db = db;

    public async Task<LotTraceResult> TraceForwardAsync(string productCd, string lotNo)
    {
        var nodes = await GetNodesAsync(productCd, lotNo);
        var affected = await ResolveCustomersAsync(nodes);
        return new LotTraceResult
        {
            ProductCd = productCd,
            LotNo = lotNo,
            Direction = "FORWARD",
            Nodes = nodes,
            AffectedCustomers = affected,
        };
    }

    public async Task<LotTraceResult> TraceBackwardAsync(string productCd, string lotNo)
    {
        var nodes = await GetNodesAsync(productCd, lotNo);
        var affected = await ResolveSuppliersAsync(nodes);
        return new LotTraceResult
        {
            ProductCd = productCd,
            LotNo = lotNo,
            Direction = "BACKWARD",
            Nodes = nodes,
            AffectedSuppliers = affected,
        };
    }

    public async Task<int> SetRecallFlagAsync(string productCd, string lotNo, bool flag, string? userName)
    {
        var stocks = await _db.Stocks
            .Where(s => s.ProductCd == productCd && s.LotNo == lotNo && !s.IsDeleted)
            .ToListAsync();
        foreach (var s in stocks)
        {
            s.RecallFlag = flag;
            s.Modifier = userName;
            s.ModifyDate = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        return stocks.Count;
    }

    public async Task<LotStockSummary?> GetStockSummaryAsync(string productCd, string lotNo)
    {
        var stocks = await _db.Stocks.AsNoTracking()
            .Where(s => s.ProductCd == productCd && s.LotNo == lotNo && !s.IsDeleted)
            .ToListAsync();
        if (stocks.Count == 0) return null;
        return new LotStockSummary
        {
            ProductCd = productCd,
            LotNo = lotNo,
            TotalPhysicalQty = stocks.Sum(s => s.PhysicalQty),
            TotalAvailableQty = stocks.Sum(s => s.AvailableQty),
            LocationCount = stocks.Count,
            RecallFlag = stocks.Any(s => s.RecallFlag),
            ExpiryDate = stocks.Where(s => s.ExpiryDate.HasValue).Min(s => s.ExpiryDate),
        };
    }

    // ───────── 内部ヘルパー ─────────

    private async Task<List<LotTraceNode>> GetNodesAsync(string productCd, string lotNo)
    {
        var txns = await _db.StockTransactions.AsNoTracking()
            .Where(t => t.ProductCd == productCd && t.LotNo == lotNo && !t.IsDeleted)
            .OrderBy(t => t.TxnDateTime)
            .ToListAsync();
        return txns.Select(t => new LotTraceNode
        {
            TxnNo = t.TxnNo,
            TxnType = t.TxnType,
            TxnAt = t.TxnDateTime,
            WarehouseCd = t.WarehouseCd,
            LocationCd = t.LocationCd,
            Qty = t.Qty,
            RelatedNo = t.RelatedNo,
            RelatedType = t.RelatedType,
            OperatorCd = t.OperatorCd,
            Remark = t.Remark,
        }).ToList();
    }

    private async Task<List<LotAffectedCustomer>> ResolveCustomersAsync(List<LotTraceNode> nodes)
    {
        // OUT（Qty 正数）または MOVE の源側（Qty 負数）= 在庫から出ていく方向
        var outboundNos = nodes
            .Where(n => n.RelatedType == "OUTBOUND"
                        && !string.IsNullOrWhiteSpace(n.RelatedNo)
                        && (n.TxnType == WmsTxnType.OUT
                            || (n.TxnType == WmsTxnType.MOVE && n.Qty < 0)))
            .Select(n => n.RelatedNo!)
            .Distinct()
            .ToList();
        if (outboundNos.Count == 0) return new List<LotAffectedCustomer>();

        var orders = await _db.OutboundOrders.AsNoTracking()
            .Where(o => outboundNos.Contains(o.OutboundNo))
            .ToListAsync();
        var orderMap = orders.ToDictionary(o => o.OutboundNo);

        return outboundNos
            .Where(no => orderMap.ContainsKey(no))
            .Select(no =>
            {
                var o = orderMap[no];
                // OUT 数量 + MOVE 源（負を絶対値化）の合計が出庫量
                var qty = nodes
                    .Where(n => n.RelatedNo == no && n.RelatedType == "OUTBOUND"
                                && (n.TxnType == WmsTxnType.OUT
                                    || (n.TxnType == WmsTxnType.MOVE && n.Qty < 0)))
                    .Sum(n => Math.Abs(n.Qty));
                var shippedAt = nodes
                    .Where(n => n.RelatedNo == no && n.RelatedType == "OUTBOUND")
                    .Max(n => n.TxnAt);
                return new LotAffectedCustomer
                {
                    OutboundNo = no,
                    WebOrderNo = o.WebOrderNo,
                    CustomerCd = o.CustomerCd,
                    CustomerName = o.CustomerName,
                    Qty = qty,
                    ShippedAt = shippedAt,
                };
            })
            .OrderByDescending(c => c.ShippedAt)
            .ToList();
    }

    private async Task<List<LotAffectedSupplier>> ResolveSuppliersAsync(List<LotTraceNode> nodes)
    {
        // IN 系 + RelatedType=INBOUND の RelatedNo を集約（= InboundReceipt.ReceiptNo）
        var receiptNos = nodes
            .Where(n => n.TxnType == WmsTxnType.IN
                        && n.RelatedType == "INBOUND"
                        && !string.IsNullOrWhiteSpace(n.RelatedNo))
            .Select(n => n.RelatedNo!)
            .Distinct()
            .ToList();
        if (receiptNos.Count == 0) return new List<LotAffectedSupplier>();

        // ReceiptNo → InboundOrder.SupplierCd / SupplierName
        var receipts = await _db.InboundReceipts.AsNoTracking()
            .Where(r => receiptNos.Contains(r.ReceiptNo))
            .ToListAsync();
        var inboundNos = receipts
            .Where(r => !string.IsNullOrWhiteSpace(r.InboundNo))
            .Select(r => r.InboundNo!)
            .Distinct()
            .ToList();
        var inbounds = await _db.InboundOrders.AsNoTracking()
            .Where(i => inboundNos.Contains(i.InboundNo))
            .ToListAsync();
        var inboundMap = inbounds.ToDictionary(i => i.InboundNo);

        return receipts.Select(r =>
        {
            var qty = nodes
                .Where(n => n.RelatedNo == r.ReceiptNo && n.TxnType == WmsTxnType.IN)
                .Sum(n => n.Qty);
            var receivedAt = nodes
                .Where(n => n.RelatedNo == r.ReceiptNo)
                .Max(n => n.TxnAt);
            string? supplierCd = null;
            string? supplierName = null;
            if (!string.IsNullOrWhiteSpace(r.InboundNo) && inboundMap.TryGetValue(r.InboundNo, out var inbound))
            {
                supplierCd = inbound.SupplierCd;
                supplierName = inbound.SupplierName;
            }
            return new LotAffectedSupplier
            {
                InboundNo = r.ReceiptNo,
                SupplierCd = supplierCd,
                SupplierName = supplierName,
                Qty = qty,
                ReceivedAt = receivedAt,
            };
        }).OrderByDescending(s => s.ReceivedAt).ToList();
    }
}
