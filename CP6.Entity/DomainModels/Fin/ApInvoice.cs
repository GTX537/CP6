using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 应付发票（章03 §2）。供应商发票的子账单据；过账后经自动凭证引擎生成"借费用+进项税/贷应付"凭证，
/// 余额由 AP_CONTROL 控制科目勾稽。全外币（F2-D1）：金额存原币，<see cref="FxRate"/> 为记账汇率，
/// 本位币 = 原币 × FxRate（GL 凭证里落本位币）。三单匹配前置：<see cref="PurchaseOrderId"/> 预留可空。
/// </summary>
[Table("Fin_ApInvoice")]
public class ApInvoice : BaseEntity
{
    /// <summary>系统发票号（采番 AP-yyyyMM-nnnn）</summary>
    [MaxLength(30)] public string No { get; set; } = string.Empty;

    /// <summary>供应商发票号（防重键之一；与 SupplierId 组合唯一）</summary>
    [MaxLength(50)] public string? SupplierInvoiceNo { get; set; }

    /// <summary>供应商编码（BusinessPartner.BpCd）</summary>
    [Required, MaxLength(20)] public string SupplierId { get; set; } = string.Empty;

    /// <summary>发票日期</summary>
    public DateTime InvoiceDate { get; set; }

    /// <summary>到期日（账龄/付款）</summary>
    public DateTime DueDate { get; set; }

    // —— 全外币（F2-D1）——
    /// <summary>币种，null/JPY=本位币</summary>
    [MaxLength(3)] public string? CurrencyCd { get; set; }

    /// <summary>记账汇率（外币1单位→本位币JPY），本位币=1</summary>
    [Column(TypeName = "decimal(18,6)")] public decimal FxRate { get; set; } = 1m;

    /// <summary>净额（原币，不含税）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal NetAmount { get; set; }

    /// <summary>税额（原币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }

    /// <summary>价税合计（原币）= Net + Tax</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal GrossAmount { get; set; }

    /// <summary>已核销金额（原币，含尾差核销）。Gross 全清后 = Gross</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal SettledAmount { get; set; }

    /// <summary>状态机</summary>
    public ApInvoiceStatus Status { get; set; } = ApInvoiceStatus.Draft;

    /// <summary>过账生成的凭证 Id</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>关联采购订单 Id（三单匹配预留，采购模块落地后填）</summary>
    public Guid? PurchaseOrderId { get; set; }

    // —— 红字（供应商红字 / 退货）——
    /// <summary>是否红字发票（负向，借应付/贷原材料+进项转出）</summary>
    public bool IsCreditMemo { get; set; }

    /// <summary>红字冲的原发票 Id</summary>
    public Guid? OriginInvoiceId { get; set; }

    /// <summary>来源 RMA 号（接 WMS 退货）</summary>
    [MaxLength(30)] public string? RmaId { get; set; }

    /// <summary>明细行</summary>
    public List<ApInvoiceLine> Lines { get; set; } = new();
}

/// <summary>应付发票明细行（章03 §2）。每行一个费用/资产科目（借方进项）+ 可选税码。</summary>
[Table("Fin_ApInvoiceLine")]
public class ApInvoiceLine : BaseEntity
{
    /// <summary>所属发票 Id</summary>
    public Guid InvoiceId { get; set; }

    /// <summary>行号</summary>
    public int LineNo { get; set; }

    /// <summary>物料编码（可空，费用类发票无物料）</summary>
    [MaxLength(40)] public string? ItemId { get; set; }

    /// <summary>数量</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal Qty { get; set; }

    /// <summary>原币单价</summary>
    [Column(TypeName = "decimal(18,4)")] public decimal UnitPrice { get; set; }

    /// <summary>行净额（原币）= Qty × UnitPrice（不含税）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }

    /// <summary>税码 Id（可空）</summary>
    public Guid? TaxCodeId { get; set; }

    /// <summary>行税额（原币）。不可抵扣税并入 Amount，不单列</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }

    /// <summary>借方进项科目 Id（费用/原材料/资产）</summary>
    public Guid ExpenseAccountId { get; set; }

    /// <summary>成本中心 Id（可空）</summary>
    public Guid? CostCenterId { get; set; }
}

/// <summary>应付发票状态机（章03）。财务单据不删，只走状态/红冲。</summary>
public enum ApInvoiceStatus
{
    /// <summary>草稿（可改可删）</summary>
    Draft = 0,
    /// <summary>已过账（生成凭证，冻结）</summary>
    Posted = 1,
    /// <summary>部分核销</summary>
    PartiallySettled = 2,
    /// <summary>已核销（全清）</summary>
    Settled = 3,
    /// <summary>已红冲/作废</summary>
    Reversed = 4,
}
