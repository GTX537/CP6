using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 設備マスタ（号機）
/// </summary>
/// <remarks>
/// 業務 PK：MachineCd
/// 1 設備 → N 停止記録(T_MachineDowntime) / N OEE 日次(T_OeeDaily)
/// </remarks>
[Table("M_Machine")]
public class Machine : BaseBizEntity
{
    /// <summary>設備CD（業務 PK）</summary>
    [Required, MaxLength(20)]
    public string MachineCd { get; set; } = string.Empty;

    /// <summary>設備名</summary>
    [Required, MaxLength(100)]
    public string MachineName { get; set; } = string.Empty;

    /// <summary>設備種別（汎用マスタ：印刷機/トムソン/グルア/貼合機 等）</summary>
    [MaxLength(20)] public string? MachineType { get; set; }

    /// <summary>主担当工程CD</summary>
    [MaxLength(10)] public string? ProcessCd { get; set; }

    /// <summary>所属WG</summary>
    [MaxLength(10)] public string? WgCd { get; set; }

    /// <summary>所属拠点CD</summary>
    [MaxLength(10)] public string? BaseCd { get; set; }

    /// <summary>状態：0=停止 1=稼働中 2=故障 3=メンテナンス 4=切替中(段取)</summary>
    public int Status { get; set; } = 0;

    /// <summary>計画稼働時間（分/日、OEE 可用率計算の分母）</summary>
    public int PlannedRunMinutesPerDay { get; set; } = 480; // 8h 既定

    /// <summary>理論サイクルタイム（秒/個、OEE 性能計算用）</summary>
    [Column(TypeName = "decimal(10,4)")]
    public decimal? StandardCycleSec { get; set; }

    /// <summary>設備能力（個/時間）</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? CapacityPerHour { get; set; }

    /// <summary>導入日</summary>
    public DateTime? InstallDate { get; set; }

    /// <summary>最終メンテナンス日</summary>
    public DateTime? LastMaintenanceDate { get; set; }

    /// <summary>次回メンテナンス予定日</summary>
    public DateTime? NextMaintenanceDate { get; set; }

    /// <summary>メーカ</summary>
    [MaxLength(50)] public string? Manufacturer { get; set; }

    /// <summary>型番</summary>
    [MaxLength(50)] public string? Model { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    /// <summary>有効FLG</summary>
    public bool ActiveFlg { get; set; } = true;
}
