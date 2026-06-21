using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>采购订单服务实现（采购 章02）。</summary>
public class PurchaseOrderService : IPurchaseOrderService
{
    private const string SeqKey = "PO";
    private const string ApprovalBizType = "PUR_PO";

    private readonly CP6Context _db;
    private readonly ISupplierPriceService _price;
    private readonly IFxRateService _fx;
    private readonly ISeqService _seq;
    private readonly IApprovalService _approval;

    public PurchaseOrderService(CP6Context db, ISupplierPriceService price, IFxRateService fx,
        ISeqService seq, IApprovalService approval)
    {
        _db = db;
        _price = price;
        _fx = fx;
        _seq = seq;
        _approval = approval;
    }

    /// <inheritdoc />
    public async Task<PurchaseOrder?> GetAsync(string poNo)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted);
        if (po != null)
            po.Lines = await _db.PurchaseOrderLines
                .Where(l => l.PoNo == poNo && !l.IsDeleted)
                .OrderBy(l => l.LineNo).ToListAsync();
        return po;
    }

    /// <inheritdoc />
    public async Task<List<PurchaseOrder>> ListAsync(string? supplierId = null, PoStatus? status = null)
    {
        var q = _db.PurchaseOrders.Where(p => !p.IsDeleted);
        if (!string.IsNullOrWhiteSpace(supplierId)) q = q.Where(p => p.SupplierId == supplierId);
        if (status.HasValue) q = q.Where(p => p.Status == status.Value);
        return await q.OrderByDescending(p => p.OrderDate).ThenByDescending(p => p.PoNo).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<PurchaseOrder> CreateAsync(PoCreateDto dto, string? userName)
    {
        if (dto.Lines == null || dto.Lines.Count == 0) throw new InvalidOperationException("E-PUR-024"); // 至少一行

        // 1. 供应商校验：须存在且 SupplierFlg=true（发注先）
        var supplier = await _db.BusinessPartners.FirstOrDefaultAsync(b => b.BpCd == dto.SupplierId && !b.IsDeleted)
                       ?? throw new InvalidOperationException("E-PUR-020"); // 供应商不存在
        if (!supplier.SupplierFlg) throw new InvalidOperationException("E-PUR-021"); // 非发注先

        var orderDate = dto.OrderDate ?? DateTime.Now;
        var currencyCd = string.IsNullOrWhiteSpace(supplier.CurrencyCd) ? FxConstants.BaseCurrency : supplier.CurrencyCd!;

        // 2. 冻结汇率（本位币=1；外货取基准日以前最新；未登记挡单）
        var fxRate = FxConstants.IsBase(currencyCd)
            ? 1m
            : await _fx.ResolveRateAsync(currencyCd, orderDate) ?? throw new InvalidOperationException("E-PUR-023"); // 外货汇率未登记

        var po = new PurchaseOrder
        {
            PoNo = await _seq.NextAsync(SeqKey),
            SupplierId = supplier.BpCd,
            SupplierName = supplier.BpName,
            Type = dto.Type,
            CurrencyCd = currencyCd,
            FxRate = fxRate,
            PostingBasis = string.IsNullOrWhiteSpace(supplier.PurchasePostingDiv) ? "2" : supplier.PurchasePostingDiv!,
            Status = PoStatus.Draft,
            OrderDate = orderDate,
            SourceRfqNo = dto.SourceRfqNo,
            Remarks = dto.Remarks,
            Creator = userName,
            CreateDate = DateTime.Now,
        };

        // 供应商默认税码（PurchaseTaxCd 字符串 → 解析为 TaxCode 行，拿 Id + Rate）
        TaxCode? defaultTax = null;
        if (!string.IsNullOrWhiteSpace(supplier.PurchaseTaxCd))
            defaultTax = await _db.TaxCodes.FirstOrDefaultAsync(t => t.Code == supplier.PurchaseTaxCd && t.IsActive);

        var lineNo = 0;
        foreach (var ld in dto.Lines)
        {
            if (string.IsNullOrWhiteSpace(ld.ItemId)) throw new InvalidOperationException("E-PUR-025"); // 物料必填
            if (ld.Qty <= 0) throw new InvalidOperationException("E-PUR-026");                            // 数量须>0

            // 单价：手填优先，否则按阶梯价解析；无价挡单
            var unitPrice = ld.UnitPrice is > 0
                ? ld.UnitPrice.Value
                : await _price.ResolvePriceAsync(supplier.BpCd, ld.ItemId, ld.Qty, orderDate)
                  ?? throw new InvalidOperationException("E-PUR-022"); // 无适用采购价

            // 税码：行指定优先，否则供应商默认
            TaxCode? tax = defaultTax;
            if (ld.TaxCodeId.HasValue)
                tax = await _db.TaxCodes.FirstOrDefaultAsync(t => t.Id == ld.TaxCodeId.Value);

            var net = Math.Round(ld.Qty * unitPrice, 2, MidpointRounding.AwayFromZero);
            var rate = tax?.Rate ?? 0m;
            var taxAmt = Math.Round(net * rate, 2, MidpointRounding.AwayFromZero);

            po.Lines.Add(new PurchaseOrderLine
            {
                PoNo = po.PoNo,
                LineNo = ++lineNo,
                ItemId = ld.ItemId,
                Qty = ld.Qty,
                UnitPrice = unitPrice,
                TaxCodeId = tax?.Id,
                TaxRate = rate,
                NetAmount = net,
                TaxAmount = taxAmt,
                RequiredDate = ld.RequiredDate,
                ReceivedQty = 0m,
                AcceptedQty = 0m,
                InvoicedQty = 0m,
                MatchStatus = 0,
                Status = 0,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
        }

        po.NetAmount = po.Lines.Sum(l => l.NetAmount);
        po.TaxAmount = po.Lines.Sum(l => l.TaxAmount);
        po.GrossAmount = po.NetAmount + po.TaxAmount;

        _db.PurchaseOrders.Add(po);
        _db.PurchaseOrderLines.AddRange(po.Lines);
        await _db.SaveChangesAsync();
        return po;
    }

    /// <inheritdoc />
    public async Task<PurchaseOrder> SubmitForApprovalAsync(string poNo, string? userName)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-027"); // PO 不存在
        if (po.Status != PoStatus.Draft) throw new InvalidOperationException("E-PUR-028"); // 仅草稿可送审

        var result = await _approval.SubmitAsync(new ApprovalSubmitRequest
        {
            BizType = ApprovalBizType,
            BizKey = po.PoNo,
            Amount = po.GrossAmount,
            Submitter = userName,
        });

        po.ApprovalRef = result.ApprovalRef;
        // 桩即时通过 → 直接确认；真实流程未即时通过则置送审中，待回调确认
        po.Status = result.AutoApproved ? PoStatus.Confirmed : PoStatus.PendingApproval;
        po.Modifier = userName;
        po.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return po;
    }

    /// <inheritdoc />
    public async Task ConfirmFromApprovalAsync(string poNo, string decidedBy)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted);
        if (po == null || po.Status != PoStatus.PendingApproval) return;   // 幂等：仅待审批可确认
        po.Status = PoStatus.Confirmed;
        po.Modifier = decidedBy;
        po.ModifyDate = DateTime.Now;
        // 不 SaveChanges —— OA 引擎统一持久化（回调共享 DbContext，同事务原子）
    }

    /// <inheritdoc />
    public async Task RejectFromApprovalAsync(string poNo, string reason)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted);
        if (po == null || po.Status != PoStatus.PendingApproval) return;   // 幂等
        po.Status = PoStatus.Draft;   // 回退草稿可重编重送（驳回意见在 OA Wf_FlowHistory 留痕）
        po.ModifyDate = DateTime.Now;
        // 不 SaveChanges
    }

    /// <inheritdoc />
    public async Task CancelAsync(string poNo, string? userName)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-027");
        // 仅草稿/确认（尚未发生收货）可取消
        if (po.Status != PoStatus.Draft && po.Status != PoStatus.PendingApproval && po.Status != PoStatus.Confirmed)
            throw new InvalidOperationException("E-PUR-029"); // 已收货/开票不可取消
        po.Status = PoStatus.Cancelled;
        po.Modifier = userName;
        po.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task RefreshStatusAsync(string poNo)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted);
        if (po == null) return;
        // 草稿/送审/取消 不参与收货-开票进度派生
        if (po.Status is PoStatus.Draft or PoStatus.PendingApproval or PoStatus.Cancelled) return;

        var lines = await _db.PurchaseOrderLines
            .Where(l => l.PoNo == poNo && !l.IsDeleted && l.Status != 9).ToListAsync();

        var derived = DeriveStatus(lines);
        if (derived != po.Status)
        {
            po.Status = derived;
            po.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 从三累计锚派生收货-开票进度状态（章02 §4，非手工）。前提：PO 已确认。
    /// 开票收齐 ∧ 全匹配→关闭；任一已开票→部分开票；全收齐→收货完毕；任一已收→部分收货；否则确认。
    /// </summary>
    public static PoStatus DeriveStatus(IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        if (lines.Count == 0) return PoStatus.Confirmed;

        var allInvoiced = lines.All(l => l.InvoicedQty >= l.Qty);
        var anyInvoiced = lines.Any(l => l.InvoicedQty > 0);
        var allReceived = lines.All(l => l.ReceivedQty >= l.Qty);
        var anyReceived = lines.Any(l => l.ReceivedQty > 0);
        var allMatched = lines.All(l => l.MatchStatus == 2);

        if (allInvoiced && allMatched) return PoStatus.Closed;
        if (anyInvoiced) return PoStatus.PartiallyInvoiced;
        if (allReceived) return PoStatus.Received;
        if (anyReceived) return PoStatus.PartiallyReceived;
        return PoStatus.Confirmed;
    }
}
