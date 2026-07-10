using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 棚卸明細
/// </summary>
/// <remarks>
/// 業務 UK：(StockTakeNo, LineNo)
/// 計画フェーズで Stock のスナップショットを取り、BookQty を固定。
/// カウント入力で CountedQty を記録、DiffQty = CountedQty - BookQty を計算。
/// 承認後、ADJ トランザクション発行で在庫を実盤数に上書き。
/// </remarks>
[Table("T_StockTakeDetail")]
public class StockTakeDetail : BaseBizEntity, IAuditable
{
    /// <summary>棚卸NO（FK → T_StockTake.StockTakeNo）</summary>
    [Required, MaxLength(20)]
    public string StockTakeNo { get; set; } = string.Empty;

    /// <summary>行番号</summary>
    public int LineNo { get; set; }

    /// <summary>対象 Stock ID（スナップショット時の Guid）</summary>
    public Guid StockId { get; set; }

    /// <summary>倉庫CD（スナップショット）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>ロケーションCD（スナップショット）</summary>
    [Required, MaxLength(30)]
    public string LocationCd { get; set; } = string.Empty;

    /// <summary>製品CD（スナップショット）</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>ロットNO（スナップショット）</summary>
    [Required, MaxLength(30)]
    public string LotNo { get; set; } = string.Empty;

    /// <summary>帳簿在庫数（計画時の Stock.PhysicalQty、固定）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal BookQty { get; set; } = 0m;

    /// <summary>実棚数（カウント入力値）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? CountedQty { get; set; }

    /// <summary>差異数量（CountedQty - BookQty、カウント入力時に自動計算）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? DiffQty { get; set; }

    /// <summary>差異金額（差異数量 × 単価）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? DiffAmount { get; set; }

    /// <summary>単価（スナップショット）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? UnitPrice { get; set; }

    /// <summary>差異理由CD</summary>
    [MaxLength(20)] public string? DiffReasonCd { get; set; }

    /// <summary>差異理由テキスト</summary>
    [MaxLength(500)] public string? DiffReasonText { get; set; }

    /// <summary>承認ステータス：0=未承認 1=自動承認（差異0） 2=承認済 9=却下</summary>
    public int ApprovalStatus { get; set; } = 0;

    /// <summary>承認/反映の ADJ トランザクションNO（承認後にセット）</summary>
    [MaxLength(25)] public string? AdjustTxnNo { get; set; }

    /// <summary>カウント担当者CD</summary>
    [MaxLength(20)] public string? CountedByCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
