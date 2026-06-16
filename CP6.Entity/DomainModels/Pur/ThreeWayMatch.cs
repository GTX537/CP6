using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 三单匹配记录（采购 章04，★MVP 核心）。把"PO + 收货验收 + 供应商发票"三方对齐：
/// 比 PO 行可开票量（AcceptedQty−InvoicedQty）与发票价差，容差内自动建应付、超容差挂起人工裁决。
/// </summary>
[Table("Pur_ThreeWayMatch")]
public class ThreeWayMatch : BaseBizEntity
{
    /// <summary>匹配单号（采番 "TWM"+yyyyMMdd+流水）</summary>
    [Required, MaxLength(20)]
    public string MatchNo { get; set; } = string.Empty;

    /// <summary>采购订单号</summary>
    [Required, MaxLength(20)]
    public string PoNo { get; set; } = string.Empty;

    /// <summary>供应商发票号（幂等键之一，透传财务防重）</summary>
    [Required, MaxLength(50)]
    public string SupplierInvoiceNo { get; set; } = string.Empty;

    /// <summary>匹配日期</summary>
    public DateTime MatchDate { get; set; }

    /// <summary>状态：0=通过(已建AP) / 1=差异挂起 / 2=人工放行(已建AP) / 3=拒绝</summary>
    public MatchStatus Status { get; set; } = MatchStatus.Passed;

    /// <summary>最大数量偏差率（超可开票量占订购量）</summary>
    [Column(TypeName = "decimal(9,6)")]
    public decimal MaxQtyVarPct { get; set; }

    /// <summary>最大价格偏差率</summary>
    [Column(TypeName = "decimal(9,6)")]
    public decimal MaxPriceVarPct { get; set; }

    /// <summary>建出的应付发票号（通过/放行时回填）</summary>
    [MaxLength(30)]
    public string? ApInvoiceNo { get; set; }

    /// <summary>建出的应付发票 Id</summary>
    public Guid? ApInvoiceId { get; set; }

    /// <summary>差异说明 / 处理留痕</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>处理人（人工放行/拒绝留痕）</summary>
    [MaxLength(100)]
    public string? HandledBy { get; set; }

    /// <summary>匹配明细（业务键 MatchNo 关联，级联）</summary>
    public List<ThreeWayMatchLine> Lines { get; set; } = new();
}

/// <summary>三单匹配明细（采购 章04）。留存本次发票行 + 偏差，供挂起后人工放行重建 AP。</summary>
[Table("Pur_ThreeWayMatchLine")]
public class ThreeWayMatchLine : BaseBizEntity
{
    /// <summary>所属匹配单号</summary>
    [Required, MaxLength(20)]
    public string MatchNo { get; set; } = string.Empty;

    /// <summary>行号</summary>
    public int LineNo { get; set; }

    /// <summary>对应 PO 行号</summary>
    public int PoLineNo { get; set; }

    /// <summary>物料 CD</summary>
    [Required, MaxLength(40)]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>发票数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>发票单价（原币）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal UnitPrice { get; set; }

    /// <summary>税码 Id（透传财务）</summary>
    public Guid? TaxCodeId { get; set; }

    /// <summary>价格偏差率（|发票价−PO价|/PO价）</summary>
    [Column(TypeName = "decimal(9,6)")]
    public decimal PriceVarPct { get; set; }

    /// <summary>匹配时 PO 行可开票量（AcceptedQty−InvoicedQty）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal RemainAccepted { get; set; }

    /// <summary>本行是否在容差内</summary>
    public bool WithinTolerance { get; set; }
}

/// <summary>三单匹配状态（采购 章04）。</summary>
public enum MatchStatus
{
    /// <summary>通过（容差内，已自动建 AP）</summary>
    Passed = 0,
    /// <summary>差异挂起（超容差，待人工裁决）</summary>
    Suspended = 1,
    /// <summary>人工放行（已建 AP）</summary>
    Released = 2,
    /// <summary>拒绝</summary>
    Rejected = 3,
}
