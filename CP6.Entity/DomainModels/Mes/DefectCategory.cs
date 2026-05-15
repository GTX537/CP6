using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 不良分類マスタ（M_DefectCategory / ME-M002）
/// </summary>
/// <remarks>
/// 仕様書 §7.1 / §9 No.9
/// 業務複合 PK：CATEGORY_CD + DETAIL_CD
/// 例：D01-寸法不良 / 巾寸法外れ
/// </remarks>
[Table("M_DefectCategory")]
public class DefectCategory : BaseBizEntity
{
    /// <summary>大分類CD（PK1、D01〜D07）</summary>
    [Required, MaxLength(10)]
    public string CategoryCd { get; set; } = string.Empty;

    /// <summary>小分類CD（PK2）</summary>
    [Required, MaxLength(10)]
    public string DetailCd { get; set; } = string.Empty;

    /// <summary>大分類名</summary>
    [MaxLength(50)] public string? CategoryName { get; set; }

    /// <summary>小分類名</summary>
    [MaxLength(100)] public string? DetailName { get; set; }

    /// <summary>並び順</summary>
    public int SortOrder { get; set; }

    /// <summary>有効FLG</summary>
    public bool ActiveFlg { get; set; } = true;

    /// <summary>備考</summary>
    [MaxLength(200)] public string? Remarks { get; set; }
}
