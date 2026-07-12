using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 品質検査ヘッダ（MSBBME060）
/// </summary>
/// <remarks>
/// 仕様書 §9 No.5 T_QualityInspection
/// 業務 PK：INSPECTION_NO（QCYYYYMMDD-NNNN）
/// </remarks>
[Table("T_QualityInspection")]
public class QualityInspection : BaseBizEntity, IAuditable
{
    /// <summary>検査NO（業務 PK、QCYYYYMMDD-NNNN）</summary>
    [Required, MaxLength(20)]
    public string InspectionNo { get; set; } = string.Empty;

    /// <summary>指図NO（FK → T_WorkOrder）</summary>
    [Required, MaxLength(20)]
    public string WorkOrderNo { get; set; } = string.Empty;

    /// <summary>工程CD</summary>
    [MaxLength(10)] public string? ProcessCd { get; set; }

    /// <summary>検査日</summary>
    public DateTime InspectionDate { get; set; } = DateTime.Today;

    /// <summary>検査者CD</summary>
    [Required, MaxLength(20)]
    public string InspectorCd { get; set; } = string.Empty;

    /// <summary>検査者名</summary>
    [MaxLength(100)] public string? InspectorName { get; set; }

    /// <summary>検査種類：1=受入 2=工程内 3=最終 4=出荷前</summary>
    public int InspectionType { get; set; } = 3;

    /// <summary>検査テンプレートCD（M_InspectionTemplate.TemplateCd）</summary>
    [MaxLength(20)] public string? TemplateCd { get; set; }

    /// <summary>検査対象数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? InspectionQty { get; set; }

    /// <summary>サンプル数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? SampleQty { get; set; }

    /// <summary>総合判定：1=合格 2=不合格 9=保留</summary>
    public int? OverallResult { get; set; }

    /// <summary>処置内容：1=手直し 2=特別採用 3=返品 4=廃棄</summary>
    public int? DispositionAction { get; set; }

    /// <summary>判定理由</summary>
    [MaxLength(500)] public string? JudgmentReason { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    // ───── ナビゲーション ─────
    public List<QualityInspectionItem> Items { get; set; } = new();
}
