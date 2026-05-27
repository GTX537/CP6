using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// スロッティング最適化（MSBBWM110）
/// </summary>
/// <remarks>
/// 仕様書 §21 ABC 分析（過去 N 日の OUT 頻度・数量）→ 推奨棚位算出。
/// 業務 PK：SlottingPlanNo（SLPYYYYMMDD-NNNN）
/// 状态：0=分析中 1=推奨完了 2=承認済（棚移動指示生成） 9=取消
///
/// 推奨ロジック：
///   - 出庫頻度（rank）でランク A/B/C 分類（パレート 80/15/5）
///   - A→ピッキング棚（前棚／低位置 PIK-*）、B→中段、C→上段・奥
///   - 現在ロケと推奨ロケが違う行のみ移動候補となる
///
/// 注：実際の棚移動は本テーブルでは作成しない（承認後に MOVE トランザクション発行
///     する別バッチで対応）。本テーブルは「提案」までを記録。
/// </remarks>
[Table("T_SlottingPlan")]
public class SlottingPlan : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string SlottingPlanNo { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>分析対象期間（過去日数）</summary>
    public int AnalysisDays { get; set; } = 90;

    /// <summary>分析対象 OUT トランザクション件数</summary>
    public int TxnSampleCount { get; set; }

    /// <summary>分析実行日時</summary>
    public DateTime? AnalyzedAt { get; set; }

    /// <summary>ステータス：0=分析中 1=推奨完了 2=承認済 9=取消</summary>
    public int Status { get; set; } = 0;

    /// <summary>推奨件数（A/B/C 合計）</summary>
    public int RecommendationCount { get; set; }

    /// <summary>承認者CD</summary>
    [MaxLength(20)] public string? ApproverCd { get; set; }

    /// <summary>提案 JSON（明細リスト）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? RecommendationsJson { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}
