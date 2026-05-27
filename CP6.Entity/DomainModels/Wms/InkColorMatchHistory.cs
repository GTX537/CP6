using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// インキ色合せ履歴（§32.4）
/// </summary>
/// <remarks>
/// 客先 × 色番号 で過去配合履歴を参照可能にする。
/// 営業/技術部門が同色再現時に再利用。
/// </remarks>
[Table("T_InkColorMatchHistory")]
public class InkColorMatchHistory : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string MatchNo { get; set; } = string.Empty;

    /// <summary>客先CD</summary>
    [Required, MaxLength(20)]
    public string CustomerCd { get; set; } = string.Empty;

    /// <summary>客先名（スナップショット）</summary>
    [MaxLength(100)] public string? CustomerName { get; set; }

    /// <summary>目標色番号</summary>
    [Required, MaxLength(30)]
    public string ColorCode { get; set; } = string.Empty;

    /// <summary>配合 JSON（色 + 比率）例：{"DIC-100":0.5,"DIC-200":0.3,...}</summary>
    [MaxLength(2000)] public string? FormulaJson { get; set; }

    /// <summary>消費量(kg or L)</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ConsumedQty { get; set; }

    /// <summary>合せ日時</summary>
    public DateTime MatchedAt { get; set; } = DateTime.Now;

    /// <summary>担当者CD</summary>
    [MaxLength(20)] public string? OperatorCd { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}
