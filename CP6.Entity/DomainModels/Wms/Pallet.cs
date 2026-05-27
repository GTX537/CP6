using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// パレット（MSBBWM240）
/// </summary>
/// <remarks>
/// 仕様書 §33 完成段ボール製品のパレット単位管理。
/// 業務 PK：PalletNo（PLTYYYYMMDD-NNNNN）
/// 1パレット = 1 製品 1 ロット が原則（混載禁止）
/// 段ボール完成品の "段積み崩れ防止" + "FIFO厳守" + "出荷待機ゾーン管理" が肝。
/// 状态：0=組成中 1=保管中 2=出荷待機 3=出荷済
/// </remarks>
[Table("T_Pallet")]
public class Pallet : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string PalletNo { get; set; } = string.Empty;

    /// <summary>製品CD（混載禁止＝1 パレット 1 製品）</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>ロットNO</summary>
    [Required, MaxLength(30)]
    public string LotNo { get; set; } = string.Empty;

    /// <summary>ケース数</summary>
    public int CartonQty { get; set; }

    /// <summary>重量(kg)</summary>
    [Column(TypeName = "decimal(10,3)")]
    public decimal? WeightKg { get; set; }

    /// <summary>高さ(mm)（段積み制限チェック用）</summary>
    public int? HeightMm { get; set; }

    /// <summary>段積み制限（このパレットの上に何段まで載せられるか）</summary>
    public int? MaxStackLayers { get; set; }

    /// <summary>倉庫CD</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>ロケーションCD</summary>
    [Required, MaxLength(30)]
    public string LocationCd { get; set; } = string.Empty;

    /// <summary>状态：0=組成中 1=保管中 2=出荷待機 3=出荷済</summary>
    public int Status { get; set; } = 0;

    /// <summary>関連出荷指示NO（出荷待機/出荷済時）</summary>
    [MaxLength(20)] public string? ShippedOutboundNo { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}
