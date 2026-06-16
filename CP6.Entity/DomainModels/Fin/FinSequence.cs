using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 财务采番计数器（章01 §4，仿 MesSequence）。凭证号按"期间"采番，故 SeqDate 用 "yyyy-MM" 月度作用域。
/// 业务复合键：SeqKey + SeqDate，例：('GL','2026-06') → 12 → "GL-2026-06-00012"。
/// </summary>
[Table("Fin_Sequence")]
public class FinSequence : BaseTenantEntity
{
    /// <summary>采番键（PK1）：GL=总账凭证（后续 AP/AR 各自键）</summary>
    [Required, MaxLength(10)]
    public string SeqKey { get; set; } = string.Empty;

    /// <summary>采番作用域（PK2，"yyyy-MM" 月度，跨月归零）</summary>
    [Required, MaxLength(10)]
    public string SeqDate { get; set; } = string.Empty;

    /// <summary>当前值</summary>
    public int CurrentValue { get; set; }
}
