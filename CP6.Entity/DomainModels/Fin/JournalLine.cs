using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 记账凭证分录行（章01 §4）。每行记一个科目的借或贷（二选一，另一为 0）。
/// 金额永远存"本位币"在 <see cref="Debit"/>/<see cref="Credit"/>；原币信息单独存（多币种见 07 章）。
/// </summary>
[Table("Fin_JournalLine")]
public class JournalLine : BaseTenantEntity
{
    /// <summary>所属凭证头 Id</summary>
    public Guid EntryId { get; set; }

    /// <summary>行号</summary>
    public int LineNo { get; set; }

    /// <summary>科目（必须末级、启用）</summary>
    public Guid AccountId { get; set; }

    /// <summary>借方金额（本位币），与 Credit 互斥</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Debit { get; set; }

    /// <summary>贷方金额（本位币），与 Debit 互斥</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Credit { get; set; }

    /// <summary>往来单位（应收应付科目必填）</summary>
    [MaxLength(50)]
    public string? PartnerId { get; set; }

    /// <summary>成本对象类型：工单/订单</summary>
    [MaxLength(20)]
    public string? CostObjectType { get; set; }

    /// <summary>成本对象 Id（成本归集用）</summary>
    [MaxLength(50)]
    public string? CostObjectId { get; set; }

    /// <summary>成本中心（机台/工序/部门，分析性会计维度，MVP 可不填）</summary>
    public Guid? CostCenterId { get; set; }

    // —— 多币种（07 章，本位币始终用 Debit/Credit）——
    /// <summary>原币种，null=本位币</summary>
    [MaxLength(10)]
    public string? CurrencyCd { get; set; }

    /// <summary>冻结汇率</summary>
    [Column(TypeName = "decimal(18,6)")]
    public decimal? FxRate { get; set; }

    /// <summary>原币金额</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? OrigAmount { get; set; }

    /// <summary>行摘要</summary>
    [MaxLength(500)]
    public string? Memo { get; set; }
}
