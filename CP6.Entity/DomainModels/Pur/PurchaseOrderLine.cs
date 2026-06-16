using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 采购订单行（采购 章02 §2）。★三累计锚 <see cref="ReceivedQty"/>/<see cref="AcceptedQty"/>/<see cref="InvoicedQty"/>
/// 是收货回写、三单匹配的共同锚点——PO 状态、可匹配量（Accepted−Invoiced）都从它们派生。
/// </summary>
[Table("Pur_PurchaseOrderLine")]
public class PurchaseOrderLine : BaseBizEntity
{
    /// <summary>所属采购订单号</summary>
    [Required, MaxLength(20)]
    public string PoNo { get; set; } = string.Empty;

    /// <summary>行号</summary>
    public int LineNo { get; set; }

    /// <summary>物料 CD（= Product.ProductCd）</summary>
    [Required, MaxLength(40)]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>采购数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>单价（原币；带价解析或手填）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal UnitPrice { get; set; }

    /// <summary>税码 Id（建单从供应商默认解析；接 AP 时透传给财务）</summary>
    public Guid? TaxCodeId { get; set; }

    /// <summary>冻结税率（建单快照，算税复现用）</summary>
    [Column(TypeName = "decimal(9,6)")]
    public decimal TaxRate { get; set; }

    /// <summary>行净额（原币）= Qty × UnitPrice</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal NetAmount { get; set; }

    /// <summary>行税额（原币）= NetAmount × TaxRate</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    /// <summary>要求交期</summary>
    public DateTime? RequiredDate { get; set; }

    // ───── ★三累计锚（收货/匹配回写） ─────
    /// <summary>累计收货量（GR 确认时累加）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ReceivedQty { get; set; } = 0m;

    /// <summary>累计验收合格量（着荷=收货即合格；检收=QC 通过才累加）。三单匹配的可开票上限</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal AcceptedQty { get; set; } = 0m;

    /// <summary>累计已开票量（三单匹配通过建 AP 时累加）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal InvoicedQty { get; set; } = 0m;

    /// <summary>匹配状态：0=未匹配 / 1=部分匹配 / 2=匹配完成</summary>
    public int MatchStatus { get; set; } = 0;

    /// <summary>行状态：0=有效 / 9=取消</summary>
    public int Status { get; set; } = 0;
}
