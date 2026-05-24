using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// キット品マスタ（MSBBWM140）
/// </summary>
/// <remarks>
/// 仕様書 §24 キッティング・組立。
/// 業務 PK：KitSku（キット品の製品CD）
/// 1 KitMaster → N KitMasterComponent（部品 BOM）
/// </remarks>
[Table("T_KitMaster")]
public class KitMaster : BaseBizEntity
{
    /// <summary>キット品の SKU CD（業務 PK、組立後の製品CD）</summary>
    [Required, MaxLength(20)]
    public string KitSku { get; set; } = string.Empty;

    /// <summary>キット名</summary>
    [Required, MaxLength(100)]
    public string KitName { get; set; } = string.Empty;

    /// <summary>既定保管倉庫CD</summary>
    [MaxLength(10)] public string? DefaultWarehouseCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    /// <summary>有効フラグ（false=廃版）</summary>
    public bool ActiveFlg { get; set; } = true;

    [NotMapped] public List<KitMasterComponent> Components { get; set; } = new();
}
