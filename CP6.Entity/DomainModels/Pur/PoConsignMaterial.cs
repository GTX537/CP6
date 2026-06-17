using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 有償支給材追踪（外注，采购 章07 §2）。挂在外注 PO（<see cref="PurchaseOrder.Type"/>=2）成品行下的子表，
/// 记"发给外协的料"——纸箱厂把原纸/油墨发给外协加工，料发出后仍是你的资产（不算卖、不算消耗），只是物理位置在外协。
/// </summary>
/// <remarks>
/// ★<see cref="IssuedQty"/> 是防吞料的锚：<see cref="ConsignQty"/> 是"应发"（按成品 BOM 算的计划），
/// IssuedQty 是"实发"（分批发料累加的实际）——只有实发量能和成品反推的应耗对账（章07 §6）。
/// <see cref="ConsignUnitCost"/> 是内部入库成本（非售价），收成品时并入成品成本（章07 §5）。
/// </remarks>
[Table("Pur_PoConsignMaterial")]
public class PoConsignMaterial : BaseBizEntity
{
    /// <summary>所属外注采购订单号（= <see cref="PurchaseOrder.PoNo"/>，Type=2）</summary>
    [Required, MaxLength(20)]
    public string PoNo { get; set; } = string.Empty;

    /// <summary>对应外注成品行号（<see cref="PurchaseOrderLine.LineNo"/>）</summary>
    public int LineNo { get; set; }

    /// <summary>支給材物料 CD（原纸/油墨等，= Product.ProductCd）</summary>
    [Required, MaxLength(40)]
    public string ConsignItemId { get; set; } = string.Empty;

    /// <summary>应发数量（按成品 BOM 单耗 × 成品数算的计划发料量）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ConsignQty { get; set; }

    /// <summary>支給材单位成本（内部入库成本，非售价；收成品时并入成品成本）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal ConsignUnitCost { get; set; }

    /// <summary>★已发数量（发料累加，防吞料对账的锚）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal IssuedQty { get; set; } = 0m;

    /// <summary>WMS 出库单号（物理出库委托返回，回填末次发料）</summary>
    [MaxLength(40)]
    public string? WmsIssueNo { get; set; }
}
