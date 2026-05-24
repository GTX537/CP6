using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// キット品構成部品（BOM）
/// </summary>
/// <remarks>
/// 業務 UK：(KitSku, LineNo)
/// 1 キット品 = N 部品 × 各構成数量
/// </remarks>
[Table("T_KitMasterComponent")]
public class KitMasterComponent : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string KitSku { get; set; } = string.Empty;

    public int LineNo { get; set; }

    /// <summary>部品 製品CD</summary>
    [Required, MaxLength(20)]
    public string ComponentProductCd { get; set; } = string.Empty;

    /// <summary>部品名（スナップショット）</summary>
    [MaxLength(100)] public string? ComponentName { get; set; }

    /// <summary>1 キット組立に必要な数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal RequiredQty { get; set; }

    /// <summary>単位</summary>
    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
