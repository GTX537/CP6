using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 原紙ロール（MSBBWM200）
/// </summary>
/// <remarks>
/// 仕様書 §29 段ボール原紙のロール単位管理。
/// 業務 PK：RollNo（ROLLYYYYMMDD-NNNNN）
/// 1ロール = 1 独立在庫単位。"巾×流れ目×残米長×芯径" で識別。
/// 残米管理が肝：使用都度 RemainingLengthM を減算、閾値以下で廃棄候補化。
/// 状态：0=在庫 1=使用中 2=残米 3=廃棄
/// </remarks>
[Table("T_PaperRoll")]
public class PaperRoll : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string RollNo { get; set; } = string.Empty;

    /// <summary>紙質コード（K280 / K210 / SK 等）</summary>
    [Required, MaxLength(10)]
    public string PaperGrade { get; set; } = string.Empty;

    /// <summary>巾(mm)（905 / 1100 / 1310 ...）</summary>
    public int WidthMm { get; set; }

    /// <summary>連量(g/m²)</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal BasisWeight { get; set; }

    /// <summary>流れ目：T目 / Y目</summary>
    [Required, MaxLength(2)]
    public string GrainDirection { get; set; } = "T";

    /// <summary>入庫時米長(m)</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal OriginalLengthM { get; set; }

    /// <summary>残米長(m)</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal RemainingLengthM { get; set; }

    /// <summary>芯径(inch)（3 / 5）</summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal CoreDiameterInch { get; set; } = 3m;

    /// <summary>製造年月日</summary>
    public DateTime? MfgDate { get; set; }

    /// <summary>製紙会社のロットNO</summary>
    [MaxLength(30)] public string? MfgLotNo { get; set; }

    /// <summary>仕入先ロールNO</summary>
    [MaxLength(30)] public string? SupplierRollNo { get; set; }

    /// <summary>保管位置 ロケーションCD</summary>
    [Required, MaxLength(30)]
    public string LocationCd { get; set; } = string.Empty;

    /// <summary>倉庫CD</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>状态：0=在庫 1=使用中 2=残米 3=廃棄</summary>
    public int Status { get; set; } = 0;

    /// <summary>スリッター親ロールNO（巾割り元、null=元ロール）</summary>
    [MaxLength(25)] public string? ParentRollNo { get; set; }

    /// <summary>廃棄閾値（残米長 m）— これ以下で 2→3 廃棄候補化</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? DisposeThresholdM { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}
