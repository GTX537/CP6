using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 不良品記録（MSBBME080）
/// </summary>
/// <remarks>
/// 仕様書 §9 No.7 T_DefectRecord
/// 業務 PK：DEFECT_NO（DFYYYYMMDD-NNNN）
/// ステータス：0=起票済み 1=分析中 2=是正中 3=完了
/// </remarks>
[Table("T_DefectRecord")]
public class DefectRecord : BaseBizEntity
{
    /// <summary>不良NO（業務 PK、DFYYYYMMDD-NNNN）</summary>
    [Required, MaxLength(20)]
    public string DefectNo { get; set; } = string.Empty;

    /// <summary>指図NO（FK）</summary>
    [Required, MaxLength(20)]
    public string WorkOrderNo { get; set; } = string.Empty;

    /// <summary>工程CD</summary>
    [MaxLength(10)] public string? ProcessCd { get; set; }

    /// <summary>検査NO（品質検査由来の場合のみ）</summary>
    [MaxLength(20)] public string? InspectionNo { get; set; }

    /// <summary>発生日</summary>
    public DateTime OccurDate { get; set; } = DateTime.Today;

    /// <summary>発見者CD</summary>
    [MaxLength(20)] public string? ReporterCd { get; set; }

    /// <summary>不良大分類CD（M_DefectCategory.CategoryCd）</summary>
    [Required, MaxLength(10)]
    public string CategoryCd { get; set; } = string.Empty;

    /// <summary>不良小分類CD（M_DefectCategory.DetailCd）</summary>
    [MaxLength(10)] public string? DetailCd { get; set; }

    /// <summary>不良数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal DefectQty { get; set; }

    /// <summary>不良内容</summary>
    [Required, MaxLength(500)]
    public string DefectDescription { get; set; } = string.Empty;

    /// <summary>原因分析（5Why 等）</summary>
    [MaxLength(2000)] public string? CauseAnalysis { get; set; }

    /// <summary>是正処置</summary>
    [MaxLength(2000)] public string? CorrectiveAction { get; set; }

    /// <summary>担当者CD（是正処置担当）</summary>
    [MaxLength(20)] public string? AssigneeCd { get; set; }

    /// <summary>処置期限</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>完了日</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>ステータス：0=起票済 1=分析中 2=是正中 3=完了</summary>
    public int Status { get; set; } = 0;

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
