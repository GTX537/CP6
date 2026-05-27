using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// クロスドッキング指示（MSBBWM130）
/// </summary>
/// <remarks>
/// 仕様書 §23 入庫品をいったん棚に置かず、そのまま出荷ドックへ転送。
/// 業務 PK：XDockNo（XDYYYYMMDD-NNNN）
/// 状态：0=計画 → 1=実行済 → 9=取消
/// 実行時：IN + OUT を一対で発行（在庫の滞留時間≒0）
/// </remarks>
[Table("T_CrossDockOrder")]
public class CrossDockOrder : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string XDockNo { get; set; } = string.Empty;

    /// <summary>関連入庫予定NO（任意、参照モード）</summary>
    [MaxLength(20)] public string? InboundNo { get; set; }

    /// <summary>関連出荷指示NO（任意、参照モード）</summary>
    [MaxLength(20)] public string? OutboundNo { get; set; }

    /// <summary>製品CD</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>クロスドック数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>仕入先 / 客先</summary>
    [MaxLength(20)] public string? SupplierCd { get; set; }
    [MaxLength(20)] public string? CustomerCd { get; set; }

    /// <summary>到着ドック</summary>
    [MaxLength(30)] public string? FromDock { get; set; }
    /// <summary>出荷ドック</summary>
    [MaxLength(30)] public string? ToDock { get; set; }

    /// <summary>倉庫CD（IN + OUT 同一倉庫）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>仮置きロケーション</summary>
    [Required, MaxLength(30)]
    public string TempLocationCd { get; set; } = string.Empty;

    /// <summary>ロットNO</summary>
    [MaxLength(30)] public string LotNo { get; set; } = string.Empty;

    /// <summary>ステータス：0=計画 1=実行済 9=取消</summary>
    public int Status { get; set; } = 0;

    /// <summary>実行 IN トランザクションNO</summary>
    [MaxLength(25)] public string? InTxnNo { get; set; }
    /// <summary>実行 OUT トランザクションNO</summary>
    [MaxLength(25)] public string? OutTxnNo { get; set; }

    public DateTime? ExecutedAt { get; set; }
    [MaxLength(20)] public string? OperatorCd { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}
