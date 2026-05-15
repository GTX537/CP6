using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 製造指図工程明細（MSBBME020 第2页）
/// </summary>
/// <remarks>
/// 仕様書 §9 No.2 T_WorkOrderProcess
/// 業務複合 PK：WORK_ORDER_NO + PROCESS_CD + TASK_CD
/// </remarks>
[Table("T_WorkOrderProcess")]
public class WorkOrderProcess : BaseBizEntity
{
    /// <summary>指図NO（FK → T_WorkOrder.WorkOrderNo）</summary>
    [Required, MaxLength(20)]
    public string WorkOrderNo { get; set; } = string.Empty;

    /// <summary>工程CD（PK2、PA050.ProductProcess.ProcessCd から展開）</summary>
    [Required, MaxLength(10)]
    public string ProcessCd { get; set; } = string.Empty;

    /// <summary>作業CD（PK3、PA050.ProductProcess.TaskCd から展開）</summary>
    [Required, MaxLength(10)]
    public string TaskCd { get; set; } = string.Empty;

    /// <summary>工程名</summary>
    [MaxLength(100)] public string? ProcessName { get; set; }

    /// <summary>並び順（実行順）</summary>
    public int SortOrder { get; set; }

    /// <summary>工程ステータス：0=未着手 1=着手中 2=完了 3=中断 9=取消</summary>
    public int ProcessStatus { get; set; } = 0;

    /// <summary>号機CD / 発注先CD</summary>
    [MaxLength(20)] public string? MachineCd { get; set; }

    /// <summary>WG(ワーキンググループ)CD</summary>
    [MaxLength(10)] public string? WgCd { get; set; }

    /// <summary>計画開始日時</summary>
    public DateTime? PlanStartTime { get; set; }

    /// <summary>計画完了日時</summary>
    public DateTime? PlanEndTime { get; set; }

    /// <summary>実績開始日時</summary>
    public DateTime? ActualStartTime { get; set; }

    /// <summary>実績完了日時</summary>
    public DateTime? ActualEndTime { get; set; }

    /// <summary>計画数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? PlanQty { get; set; }

    /// <summary>累計良品数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal GoodQty { get; set; } = 0;

    /// <summary>累計不良数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal DefectQty { get; set; } = 0;

    /// <summary>標準ロス率</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? StdLossRate { get; set; }

    /// <summary>リードタイム（日）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? LeadTime { get; set; }

    /// <summary>前工程CD（依存関係）</summary>
    [MaxLength(10)] public string? PrevProcessCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(200)] public string? Remarks { get; set; }
}
