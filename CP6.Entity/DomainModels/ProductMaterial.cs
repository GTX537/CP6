using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 製品加工材料マスタ（MSBBPA050 第4页材料設定）
/// </summary>
/// <remarks>
/// 仕様書 §14.3 T_ProductMaterial
/// 業務複合 PK：製品コード + 工程コード + 材料CD
/// </remarks>
[Table("T_ProductMaterial")]
public class ProductMaterial : BaseBizEntity
{
    /// <summary>製品コード（FK + PK1）</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>工程コード（PK2）</summary>
    [Required, MaxLength(10)]
    public string ProcessCd { get; set; } = string.Empty;

    /// <summary>材料CD（PK3）</summary>
    [Required, MaxLength(20)]
    public string MaterialCd { get; set; } = string.Empty;

    /// <summary>工程材料区分：1=仕掛品 / 2=連産品 / 3=原料 / 4=印刷原紙</summary>
    [Required, MaxLength(1)]
    public string MaterialTypeDiv { get; set; } = "1";

    // ───── 品目CD+枝番（材料区分=3 時 対応品目マスタ） ─────
    [MaxLength(15)] public string? ItemCd { get; set; }
    [MaxLength(10)] public string? Branch1 { get; set; }
    [MaxLength(10)] public string? Branch2 { get; set; }
    [MaxLength(10)] public string? Branch3 { get; set; }

    /// <summary>受給区分（無償支給 / 有償支給、汎用マスタ）</summary>
    [MaxLength(4)] public string? SupplyDiv { get; set; }

    /// <summary>受給単価</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? SupplyPrice { get; set; }

    /// <summary>並び順（システム自動採番）</summary>
    public int SortOrder { get; set; }
}
