using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 見積加工工程 明细表 - MSBBPA010 §7.2 T_EstimateCalcProcess
/// 每一行对应第 2 页表格中一道工程
/// </summary>
[Table("T_EstimateCalcProcess")]
public class EstimateCalcProcess : BaseBizEntity
{
    /// <summary>所属見積計算書 NO（外键到 EstimateCalc.QtnCalcNo）</summary>
    [Required, MaxLength(20)]
    public string QtnCalcNo { get; set; } = string.Empty;

    /// <summary>行序号（同一 QtnCalcNo 内唯一）</summary>
    public int SeqNo { get; set; }

    [MaxLength(10)] public string? ProcessCd { get; set; }
    [MaxLength(100)] public string? ProcessName { get; set; }
    [MaxLength(10)] public string? TaskCd { get; set; }
    [MaxLength(100)] public string? TaskName { get; set; }
    [MaxLength(10)] public string? WgCd { get; set; }
    [MaxLength(50)] public string? MfgLocation { get; set; }

    // ───── 仕様 1-7 ─────
    [MaxLength(50)] public string? Spec1Label { get; set; }
    [MaxLength(100)] public string? Spec1Val { get; set; }
    [MaxLength(50)] public string? Spec2Label { get; set; }
    [MaxLength(100)] public string? Spec2Val { get; set; }
    [MaxLength(50)] public string? Spec3Label { get; set; }
    [MaxLength(100)] public string? Spec3Val { get; set; }
    [MaxLength(50)] public string? Spec4Label { get; set; }
    [MaxLength(100)] public string? Spec4Val { get; set; }
    [MaxLength(50)] public string? Spec5Label { get; set; }
    [MaxLength(100)] public string? Spec5Val { get; set; }
    [MaxLength(50)] public string? Spec6Label { get; set; }
    [MaxLength(100)] public string? Spec6Val { get; set; }
    [MaxLength(50)] public string? Spec7Label { get; set; }
    [MaxLength(100)] public string? Spec7Val { get; set; }

    [MaxLength(20)] public string? PlateNo { get; set; }
    [MaxLength(200)] public string? ProcNote1 { get; set; }
    [MaxLength(200)] public string? ProcNote2 { get; set; }
}
