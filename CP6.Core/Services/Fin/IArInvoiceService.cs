using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应收发票服务（章04 §2，镜像 AP）。录入/过账（收入确认 + 成本结转双凭证）+ 出货自动开票（幂等 ShipmentId）
/// + 红冲（出货取消时冲两张凭证）。成本用估算成本起步（F2-D2）。
/// </summary>
public interface IArInvoiceService
{
    Task<ArInvoice?> GetAsync(Guid id);
    Task<List<ArInvoice>> ListAsync(string? customerId = null, ArInvoiceStatus? status = null);

    /// <summary>录入草稿：行级算销项税 + 采番。回填 Net/Tax/Gross。</summary>
    Task<FinResult> CreateAsync(ArInvoice invoice, string user);

    /// <summary>过账：发 AR.Revenue（借应收/贷收入+销项税）+（CostAmount&gt;0 时）AR.Cogs（借COGS/贷FG）。回填两凭证 Id + Status。</summary>
    Task<FinResult> PostAsync(Guid invoiceId, string user);

    /// <summary>出货自动开票（幂等 ShipmentId）：据出货请求建票 + 过账两凭证。返回发票 Id/No。</summary>
    Task<(FinResult Result, Guid? InvoiceId, string? No)> CreateFromShipmentAsync(FinShipmentInvoiceRequest request, string user);

    /// <summary>销售退货红字（幂等 CreditNoteId，接 CP6 CreditNote）：建红字发票 + 过账红冲收入（AR.CreditMemo）+ 成本回冲（AR.CogsReversal）。返回发票 Id/No。</summary>
    Task<(FinResult Result, Guid? InvoiceId, string? No)> CreateCreditMemoAsync(FinCreditMemoRequest request, string user);

    /// <summary>红冲（出货取消）：红冲收入 + 成本两凭证（系统直过）+ 发票 Reversed。</summary>
    Task<FinResult> ReverseAsync(Guid invoiceId, string user, string reason);
}

/// <summary>出货自动开票请求（C-2，FinBridgeHook 据出货/订单数据填）。</summary>
public class FinShipmentInvoiceRequest
{
    public string ShipmentId { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    /// <summary>来源 MES 工单号（可选）。提供且存在成本单时，成本切真实 FG 单位成本×出货数（F3-D3），否则用 <see cref="EstimatedCost"/>。</summary>
    public string? WorkOrderNo { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public string? CurrencyCd { get; set; }
    public decimal? FxRate { get; set; }
    /// <summary>估算成本（本位币，F2-D2 起步值；无 <see cref="WorkOrderNo"/> 或无成本单时回退用它）</summary>
    public decimal EstimatedCost { get; set; }
    public List<FinShipmentInvoiceLine> Lines { get; set; } = new();
}

/// <summary>出货自动开票请求明细行。</summary>
public class FinShipmentInvoiceLine
{
    public string? ItemId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? TaxCodeId { get; set; }
    public Guid? RevenueAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
}

/// <summary>销售退货红字请求（C-3，据 CP6 CreditNote 数据填）。行结构复用出货明细。</summary>
public class FinCreditMemoRequest
{
    /// <summary>来源销售退货单 CreditNote.Id（幂等防重开红字）</summary>
    public Guid CreditNoteId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public string? CurrencyCd { get; set; }
    public decimal? FxRate { get; set; }
    /// <summary>退货成本（本位币，回冲 FG）</summary>
    public decimal EstimatedCost { get; set; }
    /// <summary>原始应收发票 Id（可选，留痕）</summary>
    public Guid? OriginInvoiceId { get; set; }
    public List<FinShipmentInvoiceLine> Lines { get; set; } = new();
}
