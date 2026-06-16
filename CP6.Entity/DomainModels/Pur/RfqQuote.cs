using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 报价（RFQ，采购 章06 §2）。"各家答什么"——<c>(供应商 × 询价行)</c> 的报价矩阵（3 家 × 4 行 = 12 条）。
/// 比价（章06 §4）按行在此矩阵上排 <see cref="Rank"/>、置 <see cref="IsSelected"/>；本任务（B-1）仅录入，Rank/IsSelected 保持默认。
/// </summary>
[Table("Pur_RfqQuote")]
public class RfqQuote : BaseBizEntity
{
    /// <summary>所属询价单号</summary>
    [Required, MaxLength(20)]
    public string RfqNo { get; set; } = string.Empty;

    /// <summary>供应商 CD（= BusinessPartner.BpCd，须先被邀请）</summary>
    [Required, MaxLength(20)]
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>对应询价行号（= RfqLine.LineNo）</summary>
    public int LineNo { get; set; }

    /// <summary>报价单价（原币）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal QuotedPrice { get; set; }

    /// <summary>币种 CD（null/JPY=本位币）</summary>
    [MaxLength(3)]
    public string? CurrencyCd { get; set; }

    /// <summary>交期（天）</summary>
    public int? LeadDays { get; set; }

    /// <summary>报价有效期（硬约束：过期报价不能被选中转 PO，章06 §3/§4）</summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>★选中标记（章06 §4 选定时置；B-1 默认 false）</summary>
    public bool IsSelected { get; set; } = false;

    /// <summary>比价名次（1=最优；章06 §4 比价算出；B-1 默认 0）</summary>
    public int Rank { get; set; } = 0;
}
