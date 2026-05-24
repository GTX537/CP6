using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 出庫指示ヘッダ（MSBBWM050 + WM070 共有）
/// </summary>
/// <remarks>
/// 仕様書 §6 出庫指示。OutboundType で材料出庫(1)・出荷(2)・社内振替(3)・その他(9) を区分。
/// 業務 PK：OutboundNo（OUTYYYYMMDD-NNNN）。1ヘッダ → N明細。
/// ステータス：0=下書き 1=確定済 2=引当済 3=ピッキング中 4=出庫完了 9=取消
/// </remarks>
[Table("T_OutboundOrder")]
public class OutboundOrder : BaseBizEntity
{
    /// <summary>出庫指示NO（業務 PK）</summary>
    [Required, MaxLength(20)]
    public string OutboundNo { get; set; } = string.Empty;

    /// <summary>出庫区分：1=材料出庫 2=出荷 3=社内振替 9=その他</summary>
    public int OutboundType { get; set; } = 1;

    /// <summary>関連 MES 製造指図NO（材料出庫時）</summary>
    [MaxLength(20)] public string? WorkOrderNo { get; set; }

    /// <summary>関連受注NO（出荷時：T_Order.WebOrderNo）</summary>
    [MaxLength(20)] public string? WebOrderNo { get; set; }

    /// <summary>得意先CD（出荷時）</summary>
    [MaxLength(20)] public string? CustomerCd { get; set; }

    /// <summary>得意先名（スナップショット）</summary>
    [MaxLength(100)] public string? CustomerName { get; set; }

    /// <summary>出庫倉庫CD</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>計画日（出庫予定日）</summary>
    public DateTime PlannedDate { get; set; } = DateTime.Today;

    /// <summary>ステータス：0~9</summary>
    public int Status { get; set; } = 0;

    /// <summary>優先度：1=通常 2=急ぎ 3=特急</summary>
    public int Priority { get; set; } = 1;

    /// <summary>配送先住所（出荷時）</summary>
    [MaxLength(500)] public string? ShipToAddress { get; set; }

    /// <summary>配送業者CD</summary>
    [MaxLength(20)] public string? CarrierCd { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }

    [NotMapped]
    public List<OutboundOrderDetail> Details { get; set; } = new();
}
