namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// 财务建应付契约（采购侧端口，P-D1）。三单匹配通过后经本端口请财务建并过账应付发票（填 PurchaseOrderId）。
/// 应付唯一真相在财务——采购不双写。真实实现 <see cref="FinApServiceAdapter"/> 委托财务 IFinAp（已落地）；
/// 单测用 Fake 注入，匹配逻辑不依赖财务 GL 种子。
/// </summary>
public interface IFinApService
{
    /// <summary>据匹配结果建并过账应付发票。幂等：同(供应商,发票号)已建返回既有。</summary>
    Task<PurApResult> CreateApInvoiceAsync(PurApInvoiceDto dto, string? operatorId);
}

/// <summary>采购→财务 建应付请求（采购形状，由适配器映射到财务契约）。</summary>
public class PurApInvoiceDto
{
    /// <summary>关联采购订单 Id（兑现 ApInvoice.PurchaseOrderId 钩子）</summary>
    public Guid? PurchaseOrderId { get; set; }

    /// <summary>供应商 CD</summary>
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>供应商发票号（幂等键）</summary>
    public string SupplierInvoiceNo { get; set; } = string.Empty;

    /// <summary>发票日期</summary>
    public DateTime InvoiceDate { get; set; }

    /// <summary>到期日</summary>
    public DateTime DueDate { get; set; }

    /// <summary>币种（PO 冻结，null/JPY=本位币）</summary>
    public string? CurrencyCd { get; set; }

    /// <summary>记账汇率（PO 冻结）</summary>
    public decimal? FxRate { get; set; }

    /// <summary>摘要</summary>
    public string? Description { get; set; }

    /// <summary>明细行</summary>
    public List<PurApLineDto> Lines { get; set; } = new();
}

/// <summary>采购→财务 建应付明细行。</summary>
public class PurApLineDto
{
    /// <summary>物料 CD</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>数量</summary>
    public decimal Qty { get; set; }

    /// <summary>原币单价</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>税码 Id（PO 冻结）</summary>
    public Guid? TaxCodeId { get; set; }
}

/// <summary>建应付结果。</summary>
public class PurApResult
{
    /// <summary>是否成功</summary>
    public bool Ok { get; init; }

    /// <summary>失败错误码</summary>
    public string? Code { get; init; }

    /// <summary>应付发票 Id</summary>
    public Guid? InvoiceId { get; init; }

    /// <summary>应付发票号</summary>
    public string? InvoiceNo { get; init; }

    /// <summary>成功。</summary>
    public static PurApResult Pass(Guid? id, string? no) => new() { Ok = true, InvoiceId = id, InvoiceNo = no };

    /// <summary>失败。</summary>
    public static PurApResult Fail(string code) => new() { Ok = false, Code = code };
}
