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

    /// <summary>红冲（出货取消）：红冲收入 + 成本两凭证（系统直过）+ 发票 Reversed。</summary>
    Task<FinResult> ReverseAsync(Guid invoiceId, string user, string reason);
}

/// <summary>出货自动开票请求（C-2，FinBridgeHook 据出货/订单数据填）。</summary>
public class FinShipmentInvoiceRequest
{
    public string ShipmentId { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public string? CurrencyCd { get; set; }
    public decimal? FxRate { get; set; }
    /// <summary>估算成本（本位币，F2-D2；成本会计 06 章落地后切真实）</summary>
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
