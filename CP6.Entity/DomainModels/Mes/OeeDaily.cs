using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// OEE 日次集計（設備総合効率）
/// </summary>
/// <remarks>
/// OEE = 可用率 × 性能 × 品質
///   可用率(Availability) = (計画稼働時間 - 停止時間) / 計画稼働時間
///   性能(Performance)    = (良品数+不良数) / (能力 × 実稼働時間)
///   品質(Quality)        = 良品数 / (良品数+不良数)
/// 業務複合 PK：OeeDate + MachineCd
/// </remarks>
[Table("T_OeeDaily")]
public class OeeDaily : BaseBizEntity
{
    /// <summary>集計日（PK1）</summary>
    public DateTime OeeDate { get; set; }

    /// <summary>設備CD（PK2、FK → M_Machine.MachineCd）</summary>
    [Required, MaxLength(20)]
    public string MachineCd { get; set; } = string.Empty;

    /// <summary>計画稼働時間（分）</summary>
    public int PlannedRunMinutes { get; set; }

    /// <summary>実稼働時間（分）= 計画 - 停止</summary>
    public int ActualRunMinutes { get; set; }

    /// <summary>停止時間（分）</summary>
    public int DowntimeMinutes { get; set; }

    /// <summary>良品数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal GoodQty { get; set; }

    /// <summary>不良数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal DefectQty { get; set; }

    /// <summary>可用率(%) = ActualRunMinutes / PlannedRunMinutes * 100</summary>
    [Column(TypeName = "decimal(8,4)")]
    public decimal Availability { get; set; }

    /// <summary>性能(%) = (Good+Defect) / (Capacity × ActualRunMinutes/60) * 100</summary>
    [Column(TypeName = "decimal(8,4)")]
    public decimal Performance { get; set; }

    /// <summary>品質(%) = Good / (Good+Defect) * 100</summary>
    [Column(TypeName = "decimal(8,4)")]
    public decimal Quality { get; set; }

    /// <summary>OEE(%) = Availability × Performance × Quality / 10000</summary>
    [Column(TypeName = "decimal(8,4)")]
    public decimal Oee { get; set; }

    /// <summary>備考（再計算メモ等）</summary>
    [MaxLength(200)] public string? Remarks { get; set; }
}
