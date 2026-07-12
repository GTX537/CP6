using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 製造実績（MSBBME040）
/// </summary>
/// <remarks>
/// 仕様書 §9 No.4 T_ProductionResult
/// 業務 PK：RESULT_NO（PRYYYYMMDD-NNNN）
/// 1指図1工程の各報告（開始/中断/再開/完了/数量報告）を時系列で蓄積
/// </remarks>
[Table("T_ProductionResult")]
public class ProductionResult : BaseBizEntity, IAuditable
{
    /// <summary>実績NO（業務 PK、PRYYYYMMDD-NNNN）</summary>
    [Required, MaxLength(20)]
    public string ResultNo { get; set; } = string.Empty;

    /// <summary>指図NO（FK → T_WorkOrder.WorkOrderNo）</summary>
    [Required, MaxLength(20)]
    public string WorkOrderNo { get; set; } = string.Empty;

    /// <summary>工程CD</summary>
    [Required, MaxLength(10)]
    public string ProcessCd { get; set; } = string.Empty;

    /// <summary>作業CD</summary>
    [MaxLength(10)] public string? TaskCd { get; set; }

    /// <summary>実績種別：1=開始 2=中断 3=中断解除 4=完了 5=数量報告</summary>
    public int ResultType { get; set; } = 4;

    /// <summary>作業者CD</summary>
    [Required, MaxLength(20)]
    public string OperatorCd { get; set; } = string.Empty;

    /// <summary>作業者名</summary>
    [MaxLength(100)] public string? OperatorName { get; set; }

    /// <summary>実績開始日時</summary>
    public DateTime? ActualStartTime { get; set; }

    /// <summary>実績完了日時</summary>
    public DateTime? ActualEndTime { get; set; }

    /// <summary>良品数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal GoodQty { get; set; } = 0;

    /// <summary>不良数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal DefectQty { get; set; } = 0;

    /// <summary>実績ロス率(%) = defectQty / totalQty * 100</summary>
    [Column(TypeName = "decimal(8,4)")]
    public decimal? ActualLossRate { get; set; }

    /// <summary>不良理由CD（M_DefectCategory.CategoryCd）</summary>
    [MaxLength(10)] public string? DefectReasonCd { get; set; }

    /// <summary>中断理由CD（汎用マスタ）</summary>
    [MaxLength(10)] public string? SuspendReasonCd { get; set; }

    /// <summary>号機CD</summary>
    [MaxLength(20)] public string? MachineCd { get; set; }

    /// <summary>実績備考</summary>
    [MaxLength(200)] public string? ResultNote { get; set; }

    // ───── A2 显式工时覆盖（spec §3.5）─────
    /// <summary>本次报工人工工时（h，可选；填则本行人工工时用此值）。</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? LaborHour { get; set; }
    /// <summary>本次报工机时（h，可选；填则本行机时用此值）。</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? MachineHour { get; set; }
}
