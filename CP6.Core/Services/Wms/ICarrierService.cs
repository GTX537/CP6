using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 配送業者連携サービス（MSBBWM320）
/// </summary>
/// <remarks>
/// 仕様書 §41。実際の業者 API（ヤマト/佐川）連携は将来追加。
/// 当面は CreateShipment + AddEvent 手動投入でモック動作。
/// </remarks>
public interface ICarrierService
{
    Task<List<CarrierShipment>> SearchAsync(CarrierSearchQuery q);
    Task<CarrierShipment?> GetAsync(string shipmentNo);

    /// <summary>シップメント作成（運単NO 自動生成可）</summary>
    Task<string> CreateShipmentAsync(CarrierShipmentDto dto, string? userName);

    /// <summary>イベント追加：JSON 履歴に append + 状态遷移</summary>
    Task AddEventAsync(string shipmentNo, CarrierEventDto evt, string? userName);

    /// <summary>集荷済：0→1</summary>
    Task MarkPickedUpAsync(string shipmentNo, string? userName);

    /// <summary>配送中：1→2</summary>
    Task MarkInTransitAsync(string shipmentNo, string? userName);

    /// <summary>配達完了：2→3</summary>
    Task MarkDeliveredAsync(string shipmentNo, string? userName);

    /// <summary>失敗：→9</summary>
    Task MarkFailedAsync(string shipmentNo, string reason, string? userName);
}

public class CarrierSearchQuery
{
    public string? ShipmentNo { get; set; }
    public string? PackageNo { get; set; }
    public string? TrackingNo { get; set; }
    public string? CarrierCd { get; set; }
    public string? CustomerCd { get; set; }
    public int? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class CarrierShipmentDto
{
    public string PackageNo { get; set; } = string.Empty;
    public string CarrierCd { get; set; } = "YAMATO";
    public string? TrackingNo { get; set; }  // null の場合は自動生成
    public string? ServiceType { get; set; }
    public string? CustomerCd { get; set; }
    public string? ShipToAddress { get; set; }
    public string? ShipToName { get; set; }
    public string? ShipToTel { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? CarrierFee { get; set; }
    public string? Remarks { get; set; }
}

public class CarrierEventDto
{
    public DateTime Ts { get; set; } = DateTime.Now;
    public string? Location { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
}
