namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付（AP）对外契约（F2-D3 预定义契约）。采购模块（#10）三单匹配（PO+GR+发票）通过后，
/// 经本契约请 AP 自动建票并过账——采购不依赖 AP 内部实体，AP 侧 <c>ApInvoiceService</c>（Phase B）实现本接口。
/// 本阶段 AP 发票以手工录入为主，本契约即采购落地后的低耦合入口。
/// </summary>
public interface IFinAp
{
    /// <summary>
    /// 据采购三单匹配结果创建并过账 AP 发票（含原币/汇率，F2-D1 全外币）。
    /// 幂等：同 (SupplierId, SupplierInvoiceNo) 已建则返回既有发票不重复建。
    /// </summary>
    Task<FinApInvoiceResult> CreateInvoiceFromPurchaseAsync(FinApInvoiceRequest request, string operatorId);
}

/// <summary>采购→AP 建票请求（稳定契约，不随 AP 内部实体变动）。</summary>
public class FinApInvoiceRequest
{
    /// <summary>供应商 Id</summary>
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>供应商发票号（幂等键之一）</summary>
    public string SupplierInvoiceNo { get; set; } = string.Empty;

    /// <summary>发票日期</summary>
    public DateTime InvoiceDate { get; set; }

    /// <summary>到期日（账龄/付款用）</summary>
    public DateTime DueDate { get; set; }

    /// <summary>原币种，null/JPY=本位币</summary>
    public string? CurrencyCd { get; set; }

    /// <summary>记账汇率（外币1单位→JPY），本位币留空</summary>
    public decimal? FxRate { get; set; }

    /// <summary>关联采购订单 Id（预留，采购落地后填）</summary>
    public Guid? PurchaseOrderId { get; set; }

    /// <summary>摘要</summary>
    public string? Description { get; set; }

    /// <summary>发票明细行</summary>
    public List<FinApInvoiceRequestLine> Lines { get; set; } = new();
}

/// <summary>采购→AP 建票请求明细行。</summary>
public class FinApInvoiceRequestLine
{
    /// <summary>物料 Id</summary>
    public string? ItemId { get; set; }

    /// <summary>数量</summary>
    public decimal Qty { get; set; }

    /// <summary>原币单价</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>税码 Id（可空）</summary>
    public Guid? TaxCodeId { get; set; }

    /// <summary>费用/资产科目 Id（借方进哪个科目）</summary>
    public Guid ExpenseAccountId { get; set; }

    /// <summary>成本中心 Id（可空）</summary>
    public Guid? CostCenterId { get; set; }
}

/// <summary>采购→AP 建票结果：携带 <see cref="FinResult"/> 及建出的发票标识。</summary>
public class FinApInvoiceResult
{
    /// <summary>校验/过账结果</summary>
    public FinResult Result { get; init; } = FinResult.Pass();

    /// <summary>建出（或既有幂等命中）的 AP 发票 Id</summary>
    public Guid? InvoiceId { get; init; }

    /// <summary>AP 发票号</summary>
    public string? InvoiceNo { get; init; }

    /// <summary>失败结果快捷构造</summary>
    public static FinApInvoiceResult Fail(string code, params object[] args)
        => new() { Result = FinResult.Fail(code, args) };
}
