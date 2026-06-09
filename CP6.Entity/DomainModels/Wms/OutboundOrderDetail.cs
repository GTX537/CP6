using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 出庫指示明細
/// </summary>
/// <remarks>
/// 業務 UK：(OutboundNo, LineNo)
/// 引当（Allocate）：FIFO + 期限優先で LotNo + LocationCd が確定し AllocatedQty を加算。
/// 出庫確定（Ship）：ShippedQty を加算、StockTxnNo に OUT トランザクション NO 記録。
///
/// 注：第一版では 1 明細 = 1 ロット・1 ロケーション の引当に限定。
///     必要量が単一ロットで賄えない場合は明細を分割するか、Phase WM-3.5 で
///     OutboundAllocation 子表を導入する。
/// </remarks>
[Table("T_OutboundOrderDetail")]
public class OutboundOrderDetail : BaseBizEntity
{
    /// <summary>出庫指示NO（FK → T_OutboundOrder.OutboundNo）</summary>
    [Required, MaxLength(20)]
    public string OutboundNo { get; set; } = string.Empty;

    /// <summary>行番号</summary>
    public int LineNo { get; set; }

    /// <summary>製品CD</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>製品名（スナップショット）</summary>
    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>必要数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal RequiredQty { get; set; }

    /// <summary>引当済数量（Allocate 後にセット）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal AllocatedQty { get; set; } = 0m;

    /// <summary>出庫済数量（Ship 後にセット）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ShippedQty { get; set; } = 0m;

    /// <summary>引当ロットNO（引当時に Stock からコピー）</summary>
    [MaxLength(30)] public string? LotNo { get; set; }

    /// <summary>引当ロケーションCD（引当時に Stock からコピー）</summary>
    [MaxLength(30)] public string? LocationCd { get; set; }

    /// <summary>
    /// 実引当倉庫CD（多倉庫ルーティング Gap 4.2 / T14）。
    /// ヘッダの WarehouseCd と異なる倉庫から引き当てた場合に実倉庫を記録。
    /// 出庫(Ship)・取消(Cancel)はこの値を優先（null は旧データ＝ヘッダ倉庫にフォールバック）。
    /// </summary>
    [MaxLength(10)] public string? WarehouseCd { get; set; }

    /// <summary>単位CD</summary>
    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>単価</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? UnitPrice { get; set; }

    /// <summary>引当 RSV トランザクションNO（追溯用）</summary>
    [MaxLength(25)] public string? AllocateTxnNo { get; set; }

    /// <summary>出庫 OUT トランザクションNO</summary>
    [MaxLength(25)] public string? ShipTxnNo { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
