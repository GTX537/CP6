using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 在庫トランザクション（不可変ログ）
/// </summary>
/// <remarks>
/// 仕様書 §12.3。
/// すべての Stock 変動を INSERT only で記録。UPDATE/DELETE は禁止。
/// 業務 UK：TxnNo（TXNYYYYMMDD-NNNNN）。
/// 集計レポートはこのテーブルを唯一の真実源とする。
/// </remarks>
[Table("T_StockTransaction")]
public class StockTransaction : BaseBizEntity
{
    /// <summary>トランザクションNO（業務 UK）</summary>
    [Required, MaxLength(25)]
    public string TxnNo { get; set; } = string.Empty;

    /// <summary>種別（<see cref="WmsTxnType"/>：IN/OUT/MOVE/ADJ/RSV/UNRSV）</summary>
    [Required, MaxLength(10)]
    public string TxnType { get; set; } = string.Empty;

    /// <summary>発生日時</summary>
    public DateTime TxnDateTime { get; set; } = DateTime.Now;

    /// <summary>倉庫CD</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>ロケーションCD</summary>
    [Required, MaxLength(30)]
    public string LocationCd { get; set; } = string.Empty;

    /// <summary>製品CD</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>ロットNO</summary>
    [Required, MaxLength(30)]
    public string LotNo { get; set; } = string.Empty;

    /// <summary>数量（IN/RSV は正、OUT/UNRSV は正、ADJ は差分の符号付き）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>単位CD</summary>
    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>単価</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? UnitPrice { get; set; }

    /// <summary>関連伝票NO（入庫予定NO / 出庫指示NO / 棚卸NO 等）</summary>
    [MaxLength(25)] public string? RelatedNo { get; set; }

    /// <summary>関連伝票区分（INBOUND / OUTBOUND / STOCKTAKE 等）</summary>
    [MaxLength(20)] public string? RelatedType { get; set; }

    /// <summary>MOVE 種別の移動先ロケーションCD（MOVE-OUT 行のみ）</summary>
    [MaxLength(30)] public string? CounterLocationCd { get; set; }

    /// <summary>作業者CD</summary>
    [MaxLength(20)] public string? OperatorCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remark { get; set; }

    // ───── V1.1 拡張連携列（NULL 許容、後追い追加可） ─────

    /// <summary>受入検品NO（WM100 連携）</summary>
    [MaxLength(25)] public string? ReceiptInspectionNo { get; set; }

    /// <summary>キット組立指示NO（WM140 連携）</summary>
    [MaxLength(25)] public string? KitOrderNo { get; set; }

    /// <summary>RMA返品NO（WM150 連携）</summary>
    [MaxLength(25)] public string? RmaNo { get; set; }

    /// <summary>クロスドック実施フラグ（WM130 連携）</summary>
    public bool? CrossDockFlag { get; set; }

    /// <summary>所有者区分（SELF / CUSTOMER）</summary>
    [MaxLength(10)] public string? OwnerType { get; set; }

    /// <summary>所有者CD（VMI 客先CD）</summary>
    [MaxLength(20)] public string? OwnerCd { get; set; }

    /// <summary>原紙ロールNO（WM200 連携）</summary>
    [MaxLength(20)] public string? PaperRollNo { get; set; }

    /// <summary>原紙消費長（m、原紙特化）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? ConsumedLengthM { get; set; }
}
