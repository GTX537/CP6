using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>采购对账服务实现（采购 章08/09）。PO↔GR↔AP 三方累计量核对 + 完整性异常诊断；外注并入防吞料对账。</summary>
public class PurReconcileService : IPurReconcileService
{
    private const int SubcontractPoType = 2;

    private readonly CP6Context _db;
    private readonly ISubcontractService _subcontract;

    public PurReconcileService(CP6Context db, ISubcontractService subcontract)
    {
        _db = db;
        _subcontract = subcontract;
    }

    /// <inheritdoc />
    public async Task<PoReconcileReport> ReconcilePoAsync(string poNo)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-027"); // 采购订单不存在

        var poLines = await _db.PurchaseOrderLines
            .Where(l => l.PoNo == poNo && !l.IsDeleted && l.Status != 9)
            .OrderBy(l => l.LineNo).ToListAsync();

        var tol = await ResolveToleranceAsync(po.SupplierId);
        var lines = new List<PoReconcileLine>();
        var hasIssue = false;

        foreach (var l in poLines)
        {
            // 累计锚（章02 定义、03/04 累加）是完整性总基准——超限即异常
            var overInvoice = l.InvoicedQty > l.AcceptedQty * (1 + tol.QtyTolPct);          // ① 虚开/重复开票
            var overReceipt = l.ReceivedQty > l.Qty * (1 + tol.QtyTolPct);                  // ② 超量/重复收货
            var isIssue = overInvoice || overReceipt;
            if (isIssue) hasIssue = true;

            string status =
                overInvoice ? "虚开嫌疑" :
                overReceipt ? "超量收货" :
                l.ReceivedQty < l.Qty ? "待收" :
                l.AcceptedQty < l.ReceivedQty ? "待检" :
                l.InvoicedQty < l.AcceptedQty ? "待开票" :
                "完成";

            lines.Add(new PoReconcileLine
            {
                LineNo = l.LineNo,
                ItemId = l.ItemId,
                Ordered = l.Qty,
                Received = l.ReceivedQty,
                Accepted = l.AcceptedQty,
                Invoiced = l.InvoicedQty,
                OpenToReceive = l.Qty - l.ReceivedQty,
                OpenToInvoice = l.AcceptedQty - l.InvoicedQty,
                Status = status,
                IsIssue = isIssue,
            });
        }

        // ③ 外协吞料：外注 PO 按各行合格成品数（AcceptedQty）反推应耗，对实发 IssuedQty（复用章07 对账）
        var consignReconciles = new List<ConsignReconcileResult>();
        if (po.Type == SubcontractPoType)
        {
            foreach (var l in poLines)
            {
                if (l.AcceptedQty <= 0) continue;   // 尚无合格成品 → 暂不对账
                var anyConsign = await _db.PoConsignMaterials
                    .AnyAsync(c => c.PoNo == poNo && c.LineNo == l.LineNo && !c.IsDeleted);
                if (!anyConsign) continue;

                var cr = await _subcontract.ReconcileConsignAsync(poNo, l.LineNo, l.AcceptedQty, tol.QtyTolPct, userName: null);
                consignReconciles.Add(cr);
                if (cr.HasAnomaly) hasIssue = true;
            }
        }

        return new PoReconcileReport
        {
            PoNo = poNo,
            Type = po.Type,
            SupplierId = po.SupplierId,
            HasIssue = hasIssue,
            Lines = lines,
            ConsignReconciles = consignReconciles,
        };
    }

    /// <summary>解析容差：具体供应商 &gt; 全局缺省 &gt; 硬缺省(0 严格)。同章04 口径。</summary>
    private async Task<MatchTolerance> ResolveToleranceAsync(string supplierId)
    {
        var specific = await _db.MatchTolerances.FirstOrDefaultAsync(t => !t.IsDeleted && t.SupplierId == supplierId);
        if (specific != null) return specific;
        var global = await _db.MatchTolerances.FirstOrDefaultAsync(t => !t.IsDeleted && t.SupplierId == null);
        return global ?? new MatchTolerance { QtyTolPct = 0m, PriceTolPct = 0m, AmountTolAbs = 0m };
    }
}
