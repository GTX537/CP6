using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 采购订单头（发注书，采购 章02 §2）。标准采购与外注委托同表，以 <see cref="Type"/> 区分。
/// 供应商主数据建单时快照冻结（名称/币种/入账基准/汇率），后续只读不随主数据漂移。
/// </summary>
/// <remarks>
/// 状态机：<see cref="Status"/> 是三累计锚（行 ReceivedQty/AcceptedQty/InvoicedQty）的**派生投影**，
/// 非手工输入（草稿/送审/确认除外）。冻结汇率 <see cref="FxRate"/> 一路传 PO→GR→AP。
/// 多租户继承 <see cref="BaseBizEntity"/>。
/// </remarks>
[Table("Pur_PurchaseOrder")]
public class PurchaseOrder : BaseBizEntity
{
    /// <summary>采购订单号（采番 "PO"+yyyyMMdd+流水，同租户唯一）</summary>
    [Required, MaxLength(20)]
    public string PoNo { get; set; } = string.Empty;

    /// <summary>供应商 CD（= BusinessPartner.BpCd，须 SupplierFlg=true）</summary>
    [Required, MaxLength(20)]
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>供应商名称（建单快照）</summary>
    [MaxLength(100)]
    public string? SupplierName { get; set; }

    /// <summary>类型：1=标准采购 / 2=外注委托（外注流程归采购 Plan2 章07）</summary>
    public int Type { get; set; } = 1;

    /// <summary>币种 CD（建单从供应商快照，null/JPY=本位币）</summary>
    [MaxLength(3)]
    public string? CurrencyCd { get; set; }

    /// <summary>冻结汇率（外币1单位→本位币；本位币=1）。建单冻结，一路传 GR→AP</summary>
    [Column(TypeName = "decimal(18,6)")]
    public decimal FxRate { get; set; } = 1m;

    /// <summary>入账基准（从供应商 PurchasePostingDiv 快照）：1=着荷基准 / 2=检收基准</summary>
    [MaxLength(4)]
    public string PostingBasis { get; set; } = "2";

    /// <summary>派生状态（<see cref="PoStatus"/>）</summary>
    public PoStatus Status { get; set; } = PoStatus.Draft;

    /// <summary>订单日期（汇率冻结基准日）</summary>
    public DateTime OrderDate { get; set; }

    /// <summary>净额（原币，不含税）= Σ 行净额</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal NetAmount { get; set; }

    /// <summary>税额（原币）= Σ 行税额</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    /// <summary>价税合计（原币）= Net + Tax</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal GrossAmount { get; set; }

    /// <summary>来源询价单号（采购 章06 RFQ 转 PO 时回填）</summary>
    [MaxLength(20)]
    public string? SourceRfqNo { get; set; }

    /// <summary>审批引用（送审后由审批服务回填）</summary>
    [MaxLength(50)]
    public string? ApprovalRef { get; set; }

    /// <summary>备注</summary>
    [MaxLength(500)]
    public string? Remarks { get; set; }

    /// <summary>明细行（业务键 PoNo 关联，级联）</summary>
    public List<PurchaseOrderLine> Lines { get; set; } = new();
}

/// <summary>
/// 采购订单状态（采购 章02 §4）。送审/确认后的"收货-开票"进度段由 DeriveStatus 从三累计锚派生。
/// </summary>
public enum PoStatus
{
    /// <summary>草稿</summary>
    Draft = 0,
    /// <summary>送审中（待审批）</summary>
    PendingApproval = 1,
    /// <summary>已确认（审批通过，可收货）</summary>
    Confirmed = 2,
    /// <summary>部分收货</summary>
    PartiallyReceived = 3,
    /// <summary>收货完毕</summary>
    Received = 4,
    /// <summary>部分开票</summary>
    PartiallyInvoiced = 5,
    /// <summary>关闭（开票收齐 + 匹配完成）</summary>
    Closed = 6,
    /// <summary>取消</summary>
    Cancelled = 9,
}
