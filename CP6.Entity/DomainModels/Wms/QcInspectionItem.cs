using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 入荷検品明細
/// </summary>
/// <remarks>
/// 業務 UK：(InspectionNo, LineNo)
/// AQL サンプリングではなく、簡易検品（製品単位の数量・受入/不良判定）として実装。
/// 拡張余地として CheckItemsJson に AQL 詳細を格納可能。
/// </remarks>
[Table("T_QcInspectionItem")]
public class QcInspectionItem : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string InspectionNo { get; set; } = string.Empty;

    public int LineNo { get; set; }

    /// <summary>製品CD</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>製品名（スナップショット）</summary>
    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>予定数量（入庫予定からコピー）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ExpectedQty { get; set; }

    /// <summary>到着数量（外見カウント）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ReceivedQty { get; set; }

    /// <summary>合格数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal AcceptedQty { get; set; } = 0m;

    /// <summary>不良数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal RejectedQty { get; set; } = 0m;

    /// <summary>保留数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal PendingQty { get; set; } = 0m;

    /// <summary>不良理由CD</summary>
    [MaxLength(20)] public string? DefectReasonCd { get; set; }

    /// <summary>検査項目 JSON（AQL 詳細などの拡張用、任意）</summary>
    [MaxLength(4000)] public string? CheckItemsJson { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
