using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 询价行（RFQ 行，采购 章06 §2）。"买什么"——一物料一行，记询价量与要求交期。
/// <see cref="SourcePrNo"/>/<see cref="SourcePrLineNo"/> 是**行级追溯**：不管多张 PR 怎么并、一张 PR 怎么拆，每条都追得回源头需求。
/// </summary>
[Table("Pur_RfqLine")]
public class RfqLine : BaseBizEntity
{
    /// <summary>所属询价单号</summary>
    [Required, MaxLength(20)]
    public string RfqNo { get; set; } = string.Empty;

    /// <summary>行号</summary>
    public int LineNo { get; set; }

    /// <summary>物料 CD（= Product.ProductCd，业务键引用；与 PR/PO 行一致用字符串）</summary>
    [Required, MaxLength(40)]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>询价数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>单位 CD</summary>
    [MaxLength(10)]
    public string? UnitCd { get; set; }

    /// <summary>要求交期</summary>
    public DateTime? RequiredDate { get; set; }

    /// <summary>来源采购申请号（行级追溯回 PR；多 PR 汇并时各行可来自不同 PR）</summary>
    [MaxLength(20)]
    public string? SourcePrNo { get; set; }

    /// <summary>来源采购申请行号（行级追溯回 PR 行）</summary>
    public int? SourcePrLineNo { get; set; }
}
