using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 設備停止記録
/// </summary>
/// <remarks>
/// 業務 PK：DowntimeNo（DTyyyyMMdd-NNNN）
/// OEE 可用率計算の元データ
/// </remarks>
[Table("T_MachineDowntime")]
public class MachineDowntime : BaseBizEntity
{
    /// <summary>停止記録NO（業務 PK、DTyyyyMMdd-NNNN）</summary>
    [Required, MaxLength(20)]
    public string DowntimeNo { get; set; } = string.Empty;

    /// <summary>設備CD（FK → M_Machine.MachineCd）</summary>
    [Required, MaxLength(20)]
    public string MachineCd { get; set; } = string.Empty;

    /// <summary>関連 指図NO（任意。停止が指図実行中に発生した場合）</summary>
    [MaxLength(20)] public string? WorkOrderNo { get; set; }

    /// <summary>停止開始日時</summary>
    public DateTime StartTime { get; set; }

    /// <summary>停止終了日時（null=継続中）</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>停止時間（分、自動計算）</summary>
    public int? DowntimeMinutes { get; set; }

    /// <summary>停止区分：1=計画停止(段取/清掃/メンテ) 2=故障 3=材料待ち 4=作業者不在 9=その他</summary>
    public int DowntimeType { get; set; } = 9;

    /// <summary>停止理由CD（汎用マスタ）</summary>
    [MaxLength(10)] public string? ReasonCd { get; set; }

    /// <summary>停止内容詳細</summary>
    [MaxLength(500)] public string? Description { get; set; }

    /// <summary>担当者CD</summary>
    [MaxLength(20)] public string? OperatorCd { get; set; }

    /// <summary>復旧担当者CD</summary>
    [MaxLength(20)] public string? RecoveryOperatorCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
