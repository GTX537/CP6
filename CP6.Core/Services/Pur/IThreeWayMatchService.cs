using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 三单匹配服务（采购 章04，★MVP 核心）。容差匹配 → 自动建应付（填 PurchaseOrderId）/ 超容差挂起 → 人工放行或拒绝。
/// </summary>
public interface IThreeWayMatchService
{
    /// <summary>取匹配单（含行）。</summary>
    Task<ThreeWayMatch?> GetAsync(string matchNo);

    /// <summary>列出匹配单（可按 PO/状态过滤）。</summary>
    Task<List<ThreeWayMatch>> ListAsync(string? poNo = null, MatchStatus? status = null);

    /// <summary>
    /// 匹配一张供应商发票（章04 §3）：比每行可开票量（AcceptedQty−InvoicedQty）与价差；
    /// 容差内→建 AP + InvoicedQty 累加 + 刷新 PO 状态；超容差/超可开票→挂起，不建 AP。
    /// </summary>
    Task<MatchResult> MatchInvoiceAsync(MatchInvoiceDto dto, string? userName);

    /// <summary>人工放行挂起单（章04 §5）：接受差异 → 建 AP + 累加 + Status=放行，留痕。</summary>
    Task<MatchResult> ReleaseAsync(string matchNo, string? userName, string? note);

    /// <summary>拒绝挂起单（章04 §5）：Status=拒绝，留痕，不建 AP。</summary>
    Task<ThreeWayMatch> RejectAsync(string matchNo, string? userName, string? note);
}

/// <summary>匹配供应商发票入参。</summary>
public class MatchInvoiceDto
{
    /// <summary>采购订单号</summary>
    public string PoNo { get; set; } = string.Empty;

    /// <summary>供应商发票号（幂等键）</summary>
    public string SupplierInvoiceNo { get; set; } = string.Empty;

    /// <summary>发票日期（缺省取当天）</summary>
    public DateTime? InvoiceDate { get; set; }

    /// <summary>发票明细行</summary>
    public List<MatchInvoiceLineDto> Lines { get; set; } = new();
}

/// <summary>匹配发票明细行入参。</summary>
public class MatchInvoiceLineDto
{
    /// <summary>对应 PO 行号</summary>
    public int PoLineNo { get; set; }

    /// <summary>发票数量</summary>
    public decimal Qty { get; set; }

    /// <summary>发票单价（原币）</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>税码 Id（null → 取 PO 行冻结税码）</summary>
    public Guid? TaxCodeId { get; set; }
}

/// <summary>匹配结果。</summary>
public class MatchResult
{
    /// <summary>匹配记录</summary>
    public ThreeWayMatch Match { get; set; } = null!;

    /// <summary>本次是否建出 AP</summary>
    public bool ApCreated { get; set; }

    /// <summary>建出的 AP 发票号</summary>
    public string? ApInvoiceNo { get; set; }
}
