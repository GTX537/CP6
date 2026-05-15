using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 検査項目テンプレート（M_InspectionTemplate）
/// </summary>
/// <remarks>
/// 仕様書 §9 No.8
/// 業務複合 PK：TEMPLATE_CD + ITEM_SEQ_NO
/// </remarks>
[Table("M_InspectionTemplate")]
public class InspectionTemplate : BaseBizEntity
{
    /// <summary>テンプレートCD（PK1）</summary>
    [Required, MaxLength(20)]
    public string TemplateCd { get; set; } = string.Empty;

    /// <summary>項目順序（PK2）</summary>
    public int ItemSeqNo { get; set; }

    /// <summary>テンプレート名</summary>
    [MaxLength(100)] public string? TemplateName { get; set; }

    /// <summary>検査項目名</summary>
    [Required, MaxLength(100)]
    public string ItemName { get; set; } = string.Empty;

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

    /// <summary>単位</summary>
    [MaxLength(10)] public string? Unit { get; set; }

    /// <summary>適用工程CD（NULLの場合全工程に適用）</summary>
    [MaxLength(10)] public string? ProcessCd { get; set; }

    /// <summary>必須FLG</summary>
    public bool RequiredFlg { get; set; } = true;

    /// <summary>有効FLG</summary>
    public bool ActiveFlg { get; set; } = true;
}
