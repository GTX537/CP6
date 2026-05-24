using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// ロケーション（棚位）マスタ（MSBBWM010）
/// </summary>
/// <remarks>
/// 仕様書 §2.4 ロケーション。
/// 業務 PK：LocationCd（倉庫CD + 階層キー）。階層 5 段（ゾーン → 通路 → 棚 → 棚段 → ビン）。
/// ParentLocationCd で自己参照ツリーを形成。
/// </remarks>
[Table("T_Location")]
public class Location : BaseBizEntity
{
    /// <summary>ロケーションCD（業務 PK）</summary>
    [Required, MaxLength(30)]
    public string LocationCd { get; set; } = string.Empty;

    /// <summary>所属倉庫CD（FK → T_Warehouse.WarehouseCd）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>親ロケーションCD（null = 倉庫直下）</summary>
    [MaxLength(30)] public string? ParentLocationCd { get; set; }

    /// <summary>階層レベル：1=ゾーン 2=通路 3=棚 4=棚段 5=ビン</summary>
    public int LocationLevel { get; set; } = 1;

    /// <summary>表示名</summary>
    [MaxLength(100)] public string? LocationName { get; set; }

    /// <summary>X 座標（倉庫マップ用、任意）</summary>
    public int? XCoord { get; set; }

    /// <summary>Y 座標</summary>
    public int? YCoord { get; set; }

    /// <summary>Z 座標</summary>
    public int? ZCoord { get; set; }

    /// <summary>最大収容数（0 = 制限なし）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal CapacityQty { get; set; } = 0m;

    /// <summary>収容可能区分（カンマ区切り）</summary>
    [MaxLength(50)] public string? AllowedProductType { get; set; }

    /// <summary>ピッキング可能フラグ</summary>
    public bool IsPickable { get; set; } = true;

    /// <summary>凍結中（入出庫禁止）</summary>
    public bool IsBlocked { get; set; } = false;

    /// <summary>バーコード（CODE128/QR）</summary>
    [MaxLength(50)] public string? Barcode { get; set; }
}
