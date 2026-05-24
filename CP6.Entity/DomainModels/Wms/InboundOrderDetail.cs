using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 入庫予定明細
/// </summary>
/// <remarks>
/// 業務 UK：(InboundNo, LineNo)
/// ExpectedQty は固定、ReceivedQty は WM040 実績登録の都度 累計加算（事前計算しておく）。
/// </remarks>
[Table("T_InboundOrderDetail")]
public class InboundOrderDetail : BaseBizEntity
{
    /// <summary>入庫予定NO（FK → T_InboundOrder.InboundNo）</summary>
    [Required, MaxLength(20)]
    public string InboundNo { get; set; } = string.Empty;

    /// <summary>行番号（1始まり）</summary>
    public int LineNo { get; set; }

    /// <summary>製品CD</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>製品名（スナップショット）</summary>
    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>ロットNO（受入時に決定する場合は空可）</summary>
    [MaxLength(30)] public string? LotNo { get; set; }

    /// <summary>予定数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ExpectedQty { get; set; }

    /// <summary>累計受入数量（実績登録の都度更新）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ReceivedQty { get; set; } = 0m;

    /// <summary>単位CD</summary>
    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>予定ロケーションCD</summary>
    [MaxLength(30)] public string? ExpectedLocationCd { get; set; }

    /// <summary>単価</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? UnitPrice { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
