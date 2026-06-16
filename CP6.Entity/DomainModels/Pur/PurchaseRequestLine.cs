using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 采购申请行（PR 行，采购 章05 §2）。一物料一行，记申请量、要求交期、估价与建议供应商。
/// <see cref="EstPrice"/> 估价（取价表/历史）供审批参考≠成交价；转 PO 时按 <see cref="SuggestSupplierId"/> 分组拆单并回填 <see cref="ConvertedPoNo"/>。
/// </summary>
[Table("Pur_PurchaseRequestLine")]
public class PurchaseRequestLine : BaseBizEntity
{
    /// <summary>所属采购申请号</summary>
    [Required, MaxLength(20)]
    public string PrNo { get; set; } = string.Empty;

    /// <summary>行号</summary>
    public int LineNo { get; set; }

    /// <summary>物料 CD（= Product.ProductCd / 工单材料 MaterialCd / 缺料 ProductCd，业务键引用）</summary>
    [Required, MaxLength(40)]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>申请数量（缺口量）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>单位 CD</summary>
    [MaxLength(10)]
    public string? UnitCd { get; set; }

    /// <summary>要求交期</summary>
    public DateTime? RequiredDate { get; set; }

    /// <summary>估价（原币；取采购价表/历史 PO，供审批参考；无则 null）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? EstPrice { get; set; }

    /// <summary>建议供应商 CD（= BusinessPartner.BpCd，按价表/历史 PO 推荐；无则 null，走 RFQ 选定）</summary>
    [MaxLength(20)]
    public string? SuggestSupplierId { get; set; }

    /// <summary>转出的 PO 号（转 PO 后回填，可追溯需求→订单）</summary>
    [MaxLength(20)]
    public string? ConvertedPoNo { get; set; }

    /// <summary>行状态：0=有效 / 9=取消</summary>
    public int Status { get; set; } = 0;
}
