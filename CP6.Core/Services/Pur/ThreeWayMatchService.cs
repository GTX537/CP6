using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>三单匹配服务实现（采购 章04，★MVP 核心）。</summary>
public class ThreeWayMatchService : IThreeWayMatchService
{
    private const string SeqKey = "TWM";
    private const int DueDays = 30;   // 默认账期（到期日 = 发票日 + 30）

    private readonly CP6Context _db;
    private readonly IFinApService _finAp;
    private readonly IPurchaseOrderService _po;
    private readonly ISeqService _seq;

    public ThreeWayMatchService(CP6Context db, IFinApService finAp, IPurchaseOrderService po, ISeqService seq)
    {
        _db = db;
        _finAp = finAp;
        _po = po;
        _seq = seq;
    }

    /// <inheritdoc />
    public async Task<ThreeWayMatch?> GetAsync(string matchNo)
    {
        var m = await _db.ThreeWayMatches.FirstOrDefaultAsync(x => x.MatchNo == matchNo && !x.IsDeleted);
        if (m != null)
            m.Lines = await _db.ThreeWayMatchLines
                .Where(l => l.MatchNo == matchNo && !l.IsDeleted).OrderBy(l => l.LineNo).ToListAsync();
        return m;
    }

    /// <inheritdoc />
    public async Task<List<ThreeWayMatch>> ListAsync(string? poNo = null, MatchStatus? status = null)
    {
        var q = _db.ThreeWayMatches.Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(poNo)) q = q.Where(x => x.PoNo == poNo);
        if (status.HasValue) q = q.Where(x => x.Status == status.Value);
        return await q.OrderByDescending(x => x.MatchDate).ThenByDescending(x => x.MatchNo).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<MatchResult> MatchInvoiceAsync(MatchInvoiceDto dto, string? userName)
    {
        if (dto.Lines == null || dto.Lines.Count == 0) throw new InvalidOperationException("E-PUR-040"); // 至少一行

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == dto.PoNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-027"); // PO 不存在

        // 幂等：同 (PO,供应商发票号) 已通过/放行则挡（防重复开票）
        var dup = await _db.ThreeWayMatches.AnyAsync(x => !x.IsDeleted && x.PoNo == dto.PoNo
            && x.SupplierInvoiceNo == dto.SupplierInvoiceNo
            && (x.Status == MatchStatus.Passed || x.Status == MatchStatus.Released));
        if (dup) throw new InvalidOperationException("E-PUR-043"); // 该发票已匹配建票

        var poLines = await _db.PurchaseOrderLines
            .Where(l => l.PoNo == dto.PoNo && !l.IsDeleted && l.Status != 9).ToListAsync();
        var tol = await ResolveToleranceAsync(po.SupplierId);
        var invoiceDate = dto.InvoiceDate ?? DateTime.Now;

        var match = new ThreeWayMatch
        {
            MatchNo = await _seq.NextAsync(SeqKey),
            PoNo = po.PoNo,
            SupplierInvoiceNo = dto.SupplierInvoiceNo,
            MatchDate = invoiceDate,
            Creator = userName,
            CreateDate = DateTime.Now,
        };

        var allWithin = true;
        var lineNo = 0;
        foreach (var ld in dto.Lines)
        {
            var poLine = poLines.FirstOrDefault(l => l.LineNo == ld.PoLineNo)
                         ?? throw new InvalidOperationException("E-PUR-034"); // PO 行不存在
            if (ld.Qty <= 0) throw new InvalidOperationException("E-PUR-026");

            var remainAccepted = poLine.AcceptedQty - poLine.InvoicedQty;       // 可开票量
            var priceVar = poLine.UnitPrice == 0 ? 0m : Math.Abs(ld.UnitPrice - poLine.UnitPrice) / poLine.UnitPrice;
            var amountVar = ld.Qty * Math.Abs(ld.UnitPrice - poLine.UnitPrice);

            // 硬约束：开票量不得超可开票量（防超量/重复开票）
            var qtyOk = ld.Qty <= remainAccepted;
            // 价格：率容差内，或行金额差在绝对容差内（小额放行）
            var priceOk = priceVar <= tol.PriceTolPct || amountVar <= tol.AmountTolAbs;
            var within = qtyOk && priceOk;
            if (!within) allWithin = false;

            var qtyVarPct = poLine.Qty == 0 ? 0m : Math.Max(0m, (ld.Qty - remainAccepted) / poLine.Qty);
            match.MaxQtyVarPct = Math.Max(match.MaxQtyVarPct, qtyVarPct);
            match.MaxPriceVarPct = Math.Max(match.MaxPriceVarPct, priceVar);

            match.Lines.Add(new ThreeWayMatchLine
            {
                MatchNo = match.MatchNo,
                LineNo = ++lineNo,
                PoLineNo = poLine.LineNo,
                ItemId = poLine.ItemId,
                Qty = ld.Qty,
                UnitPrice = ld.UnitPrice,
                TaxCodeId = ld.TaxCodeId ?? poLine.TaxCodeId,
                PriceVarPct = priceVar,
                RemainAccepted = remainAccepted,
                WithinTolerance = within,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
        }

        var result = new MatchResult { Match = match };

        if (allWithin)
        {
            // 容差内 → 建 AP（填 PurchaseOrderId）
            var ap = await BuildApAsync(po, match, invoiceDate, userName);
            if (ap.Ok)
            {
                match.Status = MatchStatus.Passed;
                match.ApInvoiceId = ap.InvoiceId;
                match.ApInvoiceNo = ap.InvoiceNo;
                await AccumulateInvoicedAsync(poLines, match);
                result.ApCreated = true;
                result.ApInvoiceNo = ap.InvoiceNo;
            }
            else
            {
                // 财务建票失败（如未初始化科目）→ 挂起留痕，不动锚
                match.Status = MatchStatus.Suspended;
                match.Note = $"AP build failed: {ap.Code}";
            }
        }
        else
        {
            match.Status = MatchStatus.Suspended;   // 超容差 → 差异挂起
            match.Note = "variance over tolerance";
        }

        _db.ThreeWayMatches.Add(match);
        _db.ThreeWayMatchLines.AddRange(match.Lines);
        await _db.SaveChangesAsync();

        if (result.ApCreated) await _po.RefreshStatusAsync(po.PoNo);
        return result;
    }

    /// <inheritdoc />
    public async Task<MatchResult> ReleaseAsync(string matchNo, string? userName, string? note)
    {
        var match = await GetAsync(matchNo) ?? throw new InvalidOperationException("E-PUR-041"); // 匹配单不存在
        if (match.Status != MatchStatus.Suspended) throw new InvalidOperationException("E-PUR-045"); // 仅挂起单可放行

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == match.PoNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-027");
        var poLines = await _db.PurchaseOrderLines
            .Where(l => l.PoNo == match.PoNo && !l.IsDeleted && l.Status != 9).ToListAsync();

        var ap = await BuildApAsync(po, match, match.MatchDate, userName);
        if (!ap.Ok) throw new InvalidOperationException(ap.Code ?? "E-PUR-046"); // 建票失败

        match.Status = MatchStatus.Released;
        match.ApInvoiceId = ap.InvoiceId;
        match.ApInvoiceNo = ap.InvoiceNo;
        match.HandledBy = userName;
        match.Note = note ?? "manual release";
        match.ModifyDate = DateTime.Now;
        await AccumulateInvoicedAsync(poLines, match);
        await _db.SaveChangesAsync();

        await _po.RefreshStatusAsync(match.PoNo);
        return new MatchResult { Match = match, ApCreated = true, ApInvoiceNo = ap.InvoiceNo };
    }

    /// <inheritdoc />
    public async Task<ThreeWayMatch> RejectAsync(string matchNo, string? userName, string? note)
    {
        var match = await _db.ThreeWayMatches.FirstOrDefaultAsync(x => x.MatchNo == matchNo && !x.IsDeleted)
                    ?? throw new InvalidOperationException("E-PUR-041");
        if (match.Status != MatchStatus.Suspended) throw new InvalidOperationException("E-PUR-045");
        match.Status = MatchStatus.Rejected;
        match.HandledBy = userName;
        match.Note = note ?? "rejected";
        match.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return match;
    }

    // ───── 私有 ─────

    /// <summary>建 AP（填 PurchaseOrderId + PO 冻结汇率 + 来源匹配号）。</summary>
    private Task<PurApResult> BuildApAsync(PurchaseOrder po, ThreeWayMatch match, DateTime invoiceDate, string? userName)
        => _finAp.CreateApInvoiceAsync(new PurApInvoiceDto
        {
            PurchaseOrderId = po.Id,
            SupplierId = po.SupplierId,
            SupplierInvoiceNo = match.SupplierInvoiceNo,
            InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(DueDays),
            CurrencyCd = po.CurrencyCd,
            FxRate = po.FxRate,
            Description = $"采购三单匹配 {match.MatchNo} / PO {po.PoNo}",
            Lines = match.Lines.Select(l => new PurApLineDto
            {
                ItemId = l.ItemId, Qty = l.Qty, UnitPrice = l.UnitPrice, TaxCodeId = l.TaxCodeId,
            }).ToList(),
        }, userName);

    /// <summary>建票成功后累加 PO 行 InvoicedQty + 置 MatchStatus（收齐→2）。</summary>
    private static Task AccumulateInvoicedAsync(List<PurchaseOrderLine> poLines, ThreeWayMatch match)
    {
        foreach (var ml in match.Lines)
        {
            var poLine = poLines.FirstOrDefault(l => l.LineNo == ml.PoLineNo);
            if (poLine == null) continue;
            poLine.InvoicedQty += ml.Qty;
            poLine.MatchStatus = poLine.InvoicedQty >= poLine.Qty ? 2 : 1;
            poLine.ModifyDate = DateTime.Now;
        }
        return Task.CompletedTask;
    }

    /// <summary>解析容差：具体供应商 &gt; 全局缺省 &gt; 硬缺省(0/0/0 严格)。</summary>
    private async Task<MatchTolerance> ResolveToleranceAsync(string supplierId)
    {
        var specific = await _db.MatchTolerances.FirstOrDefaultAsync(t => !t.IsDeleted && t.SupplierId == supplierId);
        if (specific != null) return specific;
        var global = await _db.MatchTolerances.FirstOrDefaultAsync(t => !t.IsDeleted && t.SupplierId == null);
        return global ?? new MatchTolerance { QtyTolPct = 0m, PriceTolPct = 0m, AmountTolAbs = 0m };
    }
}
