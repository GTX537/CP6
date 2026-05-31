using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 受注ヘッダー（MSBBPA070 第1页 基本情報のヘッダー部）
/// </summary>
/// <remarks>
/// 仕様書 §11.1 T_Order
/// 業務 PK：Web受注NO（シーケンス自動採番）
/// 1 受注 → 多 OrderDetail → 多 OrderProcess / OrderMaterial
/// </remarks>
[Table("T_Order")]
public class Order : BaseBizEntity
{
    // ───── 業務主キー ─────
    /// <summary>Web受注NO（業務 PK、シーケンス自動採番）</summary>
    [Required, MaxLength(20)]
    public string WebOrderNo { get; set; } = string.Empty;

    // ───── 顧客・受注区分 ─────
    /// <summary>得意先コード</summary>
    [Required, MaxLength(20)]
    public string CustomerCd { get; set; } = string.Empty;

    /// <summary>受注区分（汎用マスタ：加工製品/シート/原紙/購買品/商品/版型/有償支給/経費品/輪切り）</summary>
    [Required, MaxLength(4)]
    public string OrderType { get; set; } = string.Empty;

    /// <summary>受注部門</summary>
    [MaxLength(10)] public string? OrderDepartment { get; set; }

    /// <summary>受注日</summary>
    public DateTime? OrderDate { get; set; }

    /// <summary>客先納期</summary>
    public DateTime? CustomerDeliveryDate { get; set; }

    /// <summary>数量（伝票合計）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? Quantity { get; set; }

    /// <summary>注文書NO</summary>
    [MaxLength(30)] public string? OrderSheetNo { get; set; }

    /// <summary>先方担当</summary>
    [MaxLength(50)] public string? CustomerContact { get; set; }

    /// <summary>宛名</summary>
    [MaxLength(100)] public string? Addressee { get; set; }

    /// <summary>運送会社</summary>
    [MaxLength(20)] public string? Carrier { get; set; }

    /// <summary>出荷日時（YYYY/MM/DD HH:MM 文字列）</summary>
    [MaxLength(16)] public string? ShipDateTime { get; set; }

    /// <summary>出荷条件</summary>
    [MaxLength(20)] public string? ShipCondition { get; set; }

    /// <summary>売価区分：1=個別単価 / 2=セット単価</summary>
    [MaxLength(1)] public string? SalesPriceDiv { get; set; }

    // ───── mcframe7 連携 ─────
    /// <summary>受注NO（mc 連携キー；新規=NULL or "*"）</summary>
    [MaxLength(20)] public string? McOrderNo { get; set; }

    /// <summary>ステータス：0=未転送 / 9=mcframe7 転送済</summary>
    public int Status { get; set; } = 0;

    /// <summary>mcframe7 転送FLG（FALSE=未転送）</summary>
    public bool McTransferFlg { get; set; } = false;

    // ───── 出荷実績（WMS出荷確定 → ERP回写 / Phase4）─────
    /// <summary>出荷ステータス：0=未出荷 / 5=一部出荷 / 9=出荷済</summary>
    public int ShipStatus { get; set; } = 0;

    /// <summary>実出荷日時（WMS出荷確定で記録。ユーザ入力の ShipDateTime とは別管理）</summary>
    public DateTime? ActualShipDate { get; set; }

    // ───── メモ ─────
    [MaxLength(100)] public string? Memo1 { get; set; }
    [MaxLength(100)] public string? Memo2 { get; set; }
    [MaxLength(100)] public string? Memo3 { get; set; }

    // ───── ナビゲーションプロパティ ─────
    public List<OrderDetail> Details { get; set; } = new();
}
