using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 棚卸ヘッダ（MSBBWM090）
/// </summary>
/// <remarks>
/// 仕様書 §10 棚卸（実地棚卸）。
/// 業務 PK：StockTakeNo（STYYYYMMDD-NNN）。
/// 4 段階フロー：0=計画 → 1=カウント中 → 2=差異確認中 → 3=承認待ち → 4=完了 / 9=取消
/// </remarks>
[Table("T_StockTake")]
public class StockTake : BaseBizEntity, IAuditable
{
    /// <summary>棚卸NO（業務 PK）</summary>
    [Required, MaxLength(20)]
    public string StockTakeNo { get; set; } = string.Empty;

    /// <summary>棚卸区分：1=全棚卸 2=サイクル棚卸 3=臨時</summary>
    public int StockTakeType { get; set; } = 1;

    /// <summary>計画日</summary>
    public DateTime PlannedDate { get; set; } = DateTime.Today;

    /// <summary>実施日（カウント開始日）</summary>
    public DateTime? ActualDate { get; set; }

    /// <summary>完了日</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>ステータス：0~9</summary>
    public int Status { get; set; } = 0;

    /// <summary>対象倉庫CD（必須、null 不可）</summary>
    [Required, MaxLength(10)]
    public string TargetWarehouseCd { get; set; } = string.Empty;

    /// <summary>対象ロケーションCD前方一致（任意、null = 倉庫全体）</summary>
    [MaxLength(30)] public string? TargetLocationPrefix { get; set; }

    /// <summary>対象製品CDフィルタ（任意）</summary>
    [MaxLength(20)] public string? TargetProductCd { get; set; }

    /// <summary>差異金額の閾値（上長承認要否判定用、未設定=承認常に必要）</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? ApprovalThresholdAmount { get; set; }

    /// <summary>承認者CD（最終承認時にセット）</summary>
    [MaxLength(20)] public string? ApproverCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    [NotMapped]
    public List<StockTakeDetail> Details { get; set; } = new();
}
