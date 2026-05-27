using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// インキ・接着剤 ロット（MSBBWM230）
/// </summary>
/// <remarks>
/// 仕様書 §32 液体資材は ロット混合・賞味期限・色番号 管理が必要。
/// 業務 PK：InkLotNo
/// 未開封180日 / 開封後30日 等の賞味期限切替もここで管理。
/// ロット混合：旧ロット2件OUT + 新「混合ロット」1件IN（賞味期限はより早い方を継承）
/// </remarks>
[Table("T_InkLot")]
public class InkLot : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string InkLotNo { get; set; } = string.Empty;

    /// <summary>色番号（DIC-XXX / Pantone-XXX 等）</summary>
    [Required, MaxLength(30)]
    public string ColorCode { get; set; } = string.Empty;

    /// <summary>インキ種別：オフセット / フレキソ / UV / その他</summary>
    [Required, MaxLength(20)]
    public string InkType { get; set; } = "OFFSET";

    /// <summary>開封状態：UNOPENED / OPENED</summary>
    [Required, MaxLength(10)]
    public string OpenStatus { get; set; } = "UNOPENED";

    /// <summary>賞味期限</summary>
    public DateTime ExpiryDate { get; set; }

    /// <summary>容量(kg or L)</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Quantity { get; set; }

    /// <summary>単位（kg / L）</summary>
    [MaxLength(10)] public string UnitCd { get; set; } = "kg";

    /// <summary>粘度(cP)（任意）</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? ViscosityCp { get; set; }

    /// <summary>固形分(%)</summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal? SolidContent { get; set; }

    /// <summary>混合元ロットA NO</summary>
    [MaxLength(25)] public string? ParentLotNoA { get; set; }
    /// <summary>混合元ロットB NO</summary>
    [MaxLength(25)] public string? ParentLotNoB { get; set; }

    /// <summary>保管 ロケーションCD</summary>
    [MaxLength(30)] public string? LocationCd { get; set; }

    /// <summary>仕入先CD</summary>
    [MaxLength(20)] public string? SupplierCd { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}
