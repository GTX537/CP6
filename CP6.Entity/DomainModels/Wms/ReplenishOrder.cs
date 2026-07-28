using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 補充指示（MSBBWM120）
/// </summary>
/// <remarks>
/// 仕様書 §22 ピッキング棚（前棚）← 保管棚（後棚）の補充。
/// 業務 PK：ReplenishNo（RPLYYYYMMDD-NNNN）
/// 状态：0=未発行 1=MOVE完了済 2=v2タスク発行済 9=取消
/// 発行時：源在庫・先容量を予約。実在庫は v2 MOVE 完了時だけ移動する。
///
/// 第一版：日次バッチで GenerateAsync → MinQty 割れ製品を抽出 → 補充指示一括生成
/// </remarks>
[Table("T_ReplenishOrder")]
public class ReplenishOrder : BaseBizEntity, IAuditable
{
    [Required, MaxLength(20)]
    public string ReplenishNo { get; set; } = string.Empty;

    /// <summary>優先度：1=至急 2=通常</summary>
    public int Priority { get; set; } = 2;

    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    [MaxLength(100)] public string? ProductName { get; set; }

    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>補充元（保管棚）</summary>
    [Required, MaxLength(30)]
    public string FromLocationCd { get; set; } = string.Empty;

    /// <summary>補充先（ピッキング棚）</summary>
    [Required, MaxLength(30)]
    public string ToLocationCd { get; set; } = string.Empty;

    /// <summary>ロットNO</summary>
    [MaxLength(30)] public string LotNo { get; set; } = string.Empty;

    /// <summary>補充数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>生成トリガ：BATCH=日次自動 / MANUAL=手動 / ALERT=リアルタイム</summary>
    [MaxLength(20)] public string TriggerType { get; set; } = "MANUAL";

    /// <summary>ステータス：0=未発行 1=MOVE完了済 2=v2タスク発行済 9=取消</summary>
    public int Status { get; set; } = 0;

    /// <summary>実行 MOVE トランザクションNO（OUT 側）</summary>
    [MaxLength(25)] public string? OutTxnNo { get; set; }
    /// <summary>実行 MOVE トランザクションNO（IN 側）</summary>
    [MaxLength(25)] public string? InTxnNo { get; set; }

    public DateTime? ExecutedAt { get; set; }
    [MaxLength(20)] public string? OperatorCd { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}
