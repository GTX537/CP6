using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 配送業者連携 シップメント（MSBBWM320）
/// </summary>
/// <remarks>
/// 仕様書 §41 ヤマト・佐川・日本郵便等の API 連携で生成された配送オーダ。
/// 業務 PK：ShipmentNo（SHIP-YYYYMMDD-NNNN）
/// 状态遷移：0=Created → 1=PickedUp → 2=InTransit → 3=Delivered (or 9=Failed)
/// </remarks>
[Table("T_CarrierShipment")]
public class CarrierShipment : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string ShipmentNo { get; set; } = string.Empty;

    /// <summary>関連 梱包NO（T_ShippingPackage.PackageNo）</summary>
    [Required, MaxLength(20)]
    public string PackageNo { get; set; } = string.Empty;

    /// <summary>配送業者CD（YAMATO/SAGAWA/JP/SELF/OTHER）</summary>
    [Required, MaxLength(20)]
    public string CarrierCd { get; set; } = "YAMATO";

    /// <summary>送り状追跡番号</summary>
    [Required, MaxLength(50)]
    public string TrackingNo { get; set; } = string.Empty;

    /// <summary>サービス種別（NEXT_DAY/STANDARD/COOL 等）</summary>
    [MaxLength(20)] public string? ServiceType { get; set; }

    /// <summary>状态：0=Created 1=PickedUp 2=InTransit 3=Delivered 9=Failed</summary>
    public int Status { get; set; } = 0;

    /// <summary>顧客CD</summary>
    [MaxLength(20)] public string? CustomerCd { get; set; }

    /// <summary>送り先住所</summary>
    [MaxLength(200)] public string? ShipToAddress { get; set; }

    /// <summary>送り先氏名</summary>
    [MaxLength(60)] public string? ShipToName { get; set; }

    /// <summary>送り先 TEL</summary>
    [MaxLength(30)] public string? ShipToTel { get; set; }

    /// <summary>重量(kg)</summary>
    [Column(TypeName = "decimal(10,3)")]
    public decimal? WeightKg { get; set; }

    /// <summary>料金</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? CarrierFee { get; set; }

    /// <summary>引取日時</summary>
    public DateTime? PickedUpAt { get; set; }

    /// <summary>配送完了日時</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>配送イベント履歴（JSON 形式：[{ts, location, status, msg}, ...]）</summary>
    public string? EventsJson { get; set; }

    /// <summary>API 連携 リクエスト ID（業者システム側 ID）</summary>
    [MaxLength(50)] public string? ApiRefId { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}
