using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 入庫実績明細
/// </summary>
/// <remarks>
/// 業務 UK：(ReceiptNo, LineNo)
/// 確定後に StockTxnNo に発行された TXN NO を格納（追溯用）。
/// </remarks>
[Table("T_InboundReceiptDetail")]
public class InboundReceiptDetail : BaseBizEntity
{
    /// <summary>入庫実績NO（FK → T_InboundReceipt.ReceiptNo）</summary>
    [Required, MaxLength(20)]
    public string ReceiptNo { get; set; } = string.Empty;

    /// <summary>行番号</summary>
    public int LineNo { get; set; }

    /// <summary>参照する入庫予定明細の行番号（直入時 null）</summary>
    public int? RefOrderLineNo { get; set; }

    /// <summary>製品CD</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>製品名（スナップショット）</summary>
    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>ロットNO（必須、空文字は no-lot）</summary>
    [Required, MaxLength(30)]
    public string LotNo { get; set; } = string.Empty;

    /// <summary>受入数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ReceivedQty { get; set; }

    /// <summary>単位CD</summary>
    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>配置先ロケーションCD（必須）</summary>
    [Required, MaxLength(30)]
    public string LocationCd { get; set; } = string.Empty;

    /// <summary>単価</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? UnitPrice { get; set; }

    /// <summary>賞味期限（製品マスタ HasExpiryDate=true 時必須）</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>原紙ロールNO（紙器特化、将来 WM200 連携）</summary>
    [MaxLength(20)] public string? PaperRollNo { get; set; }

    /// <summary>発行された在庫トランザクションNO（確定後にセット）</summary>
    [MaxLength(25)] public string? StockTxnNo { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
