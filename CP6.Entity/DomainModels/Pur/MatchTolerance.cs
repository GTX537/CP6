using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 三单匹配容差配置（采购 章04）。按供应商配（<see cref="SupplierId"/>=null 为全局缺省）：
/// 数量/价格百分比容差 + 金额绝对放行（小额价差免挂起）。具体供应商优先于全局。
/// </summary>
[Table("Pur_MatchTolerance")]
public class MatchTolerance : BaseBizEntity
{
    /// <summary>供应商 CD（null=全局缺省）</summary>
    [MaxLength(20)]
    public string? SupplierId { get; set; }

    /// <summary>数量容差率（预留；当前可开票量为硬约束）</summary>
    [Column(TypeName = "decimal(9,6)")]
    public decimal QtyTolPct { get; set; }

    /// <summary>价格容差率（如 0.02 = 2%）</summary>
    [Column(TypeName = "decimal(9,6)")]
    public decimal PriceTolPct { get; set; }

    /// <summary>金额绝对容差（行金额差 ≤ 此值即放行，覆盖价格率）</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountTolAbs { get; set; }
}
