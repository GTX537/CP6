using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 品質検査項目明細
/// </summary>
/// <remarks>
/// 仕様書 §9 No.6 T_QualityInspectionItem
/// 業務複合 PK：INSPECTION_NO + ITEM_SEQ_NO
/// </remarks>
[Table("T_QualityInspectionItem")]
public class QualityInspectionItem : BaseBizEntity, IAuditable
{
    /// <summary>検査NO（FK → T_QualityInspection.InspectionNo）</summary>
    [Required, MaxLength(20)]
    public string InspectionNo { get; set; } = string.Empty;

    /// <summary>項目順序（PK2）</summary>
    public int ItemSeqNo { get; set; }

    /// <summary>検査項目名</summary>
    [MaxLength(100)] public string? ItemName { get; set; }

    /// <summary>検査方法</summary>
    [MaxLength(100)] public string? InspectionMethod { get; set; }

    /// <summary>規格値</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? StandardValue { get; set; }

    /// <summary>上限値</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? UpperLimit { get; set; }

    /// <summary>下限値</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? LowerLimit { get; set; }

    /// <summary>計測値</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? MeasuredValue { get; set; }

    /// <summary>計測値（文字列／目視判定用）</summary>
    [MaxLength(100)] public string? MeasuredText { get; set; }

    /// <summary>項目判定：1=合格 2=不合格 9=保留</summary>
    public int? Result { get; set; }

    /// <summary>単位</summary>
    [MaxLength(10)] public string? Unit { get; set; }

    /// <summary>備考</summary>
    [MaxLength(200)] public string? Remarks { get; set; }
}
