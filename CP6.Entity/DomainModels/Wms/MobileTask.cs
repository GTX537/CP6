using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// モバイル作業指示（MSBBWM300）
/// </summary>
/// <remarks>
/// 仕様書 §37 RFハンディ・モバイルWMS。
/// 業務 PK：MobileTaskNo（MTKYYYYMMDD-NNNN）
/// 現場端末（ハンディ/タブレット）に配信する 1 画面 1 作業の指示単位。
/// TaskType により後続の在庫処理が変わる（MOVE は完了時に MOVE トランザクション一対を発行）。
/// </remarks>
[Table("T_MobileTask")]
public class MobileTask : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string MobileTaskNo { get; set; } = string.Empty;

    /// <summary>作業種別：RECEIVE/PUTAWAY/PICK/COUNT/MOVE/LABEL</summary>
    [Required, MaxLength(20)]
    public string TaskType { get; set; } = MobileTaskType.Pick;

    /// <summary>割当作業者CD（null = 未割当 = 全員プール）</summary>
    [MaxLength(20)] public string? AssignedTo { get; set; }

    /// <summary>優先度：1=至急 2=通常</summary>
    public int Priority { get; set; } = 2;

    /// <summary>ステータス：0=未着手 1=進行中 2=完了 9=取消</summary>
    public int Status { get; set; } = MobileTaskStatus.Pending;

    /// <summary>関連伝票NO（出庫/入庫/棚卸/補充 等）</summary>
    [MaxLength(25)] public string? RelatedNo { get; set; }

    /// <summary>関連伝票種別（OUTBOUND/INBOUND/STOCKTAKE/REPLENISH 等）</summary>
    [MaxLength(20)] public string? RelatedType { get; set; }

    [MaxLength(20)] public string? ProductCd { get; set; }
    [MaxLength(100)] public string? ProductName { get; set; }
    [MaxLength(30)] public string? LotNo { get; set; }

    [MaxLength(10)] public string? WarehouseCd { get; set; }

    /// <summary>作業元ロケーション（PICK/MOVE 源）</summary>
    [MaxLength(30)] public string? FromLocationCd { get; set; }
    /// <summary>作業先ロケーション（PUTAWAY/MOVE 先）</summary>
    [MaxLength(30)] public string? ToLocationCd { get; set; }

    /// <summary>指示数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>実績数量（スキャン累計 / カウント結果）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ScannedQty { get; set; }

    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>作業指示文（現場端末に大きく表示）</summary>
    [MaxLength(200)] public string? Instruction { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? DoneAt { get; set; }

    /// <summary>完了時に発行したトランザクションNO（MOVE 源 OUT）</summary>
    [MaxLength(25)] public string? OutTxnNo { get; set; }
    /// <summary>完了時に発行したトランザクションNO（MOVE 先 IN）</summary>
    [MaxLength(25)] public string? InTxnNo { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}
