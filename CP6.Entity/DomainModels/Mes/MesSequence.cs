using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// MES採番管理
/// </summary>
/// <remarks>
/// 仕様書 §9 No.10 T_MesSequence
/// 業務複合 PK：SEQ_KEY + SEQ_DATE
/// 例：('WO', '2026-05-15') → 0042
///     ('PR', '2026-05-15') → 0011
///     ('QC', '2026-05-15') → 0003
///     ('DF', '2026-05-15') → 0001
/// </remarks>
[Table("T_MesSequence")]
public class MesSequence : BaseEntity
{
    /// <summary>採番キー（PK1）：WO=指図 / PR=実績 / QC=検査 / DF=不良</summary>
    [Required, MaxLength(10)]
    public string SeqKey { get; set; } = string.Empty;

    /// <summary>採番日（PK2、YYYY-MM-DD）</summary>
    [Required, MaxLength(10)]
    public string SeqDate { get; set; } = string.Empty;

    /// <summary>現在値</summary>
    public int CurrentValue { get; set; } = 0;
}
