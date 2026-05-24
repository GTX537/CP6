using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 入荷検品ヘッダ（MSBBWM100）
/// </summary>
/// <remarks>
/// 仕様書 §20 入荷検品（QC受入）。
/// 業務 PK：InspectionNo（QCYYYYMMDD-NNNN）
/// ワークフロー：
///   0=作成（未検品）→ 1=検品中 → 2=判定済 → 9=取消
/// 判定区分（FinalJudgement）：
///   PASS=合格、CONDITIONAL=条件付合格、HOLD=保留、FAIL=不合格、RETURN=即返品
/// </remarks>
[Table("T_QcInspection")]
public class QcInspection : BaseBizEntity
{
    /// <summary>検品NO（業務 PK）</summary>
    [Required, MaxLength(20)]
    public string InspectionNo { get; set; } = string.Empty;

    /// <summary>関連入庫予定NO（FK → T_InboundOrder.InboundNo、直入検品時は null）</summary>
    [MaxLength(20)] public string? InboundNo { get; set; }

    /// <summary>仕入先CD（スナップショット）</summary>
    [MaxLength(20)] public string? SupplierCd { get; set; }

    /// <summary>仕入先名</summary>
    [MaxLength(100)] public string? SupplierName { get; set; }

    /// <summary>到着日時</summary>
    public DateTime ArrivalDateTime { get; set; } = DateTime.Now;

    /// <summary>検品者CD</summary>
    [MaxLength(20)] public string? InspectorCd { get; set; }

    /// <summary>ステータス：0=作成 1=検品中 2=判定済 9=取消</summary>
    public int Status { get; set; } = 0;

    /// <summary>最終判定：PASS/CONDITIONAL/HOLD/FAIL/RETURN（判定済時にセット）</summary>
    [MaxLength(20)] public string? FinalJudgement { get; set; }

    /// <summary>判定理由</summary>
    [MaxLength(500)] public string? JudgementReason { get; set; }

    /// <summary>判定後の自動生成 入庫実績NO（合格時のみ）</summary>
    [MaxLength(20)] public string? GeneratedReceiptNo { get; set; }

    /// <summary>写真URL（カンマ区切り）</summary>
    [MaxLength(2000)] public string? PhotoUrls { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    [NotMapped] public List<QcInspectionItem> Items { get; set; } = new();
}
