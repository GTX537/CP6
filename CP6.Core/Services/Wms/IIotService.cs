using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// IoT センサ サービス（MSBBWM330）
/// </summary>
/// <remarks>
/// 仕様書 §42。実機 MQTT/HTTP push 連携は将来追加。
/// 当面は SimulateAsync で疑似データ生成、閾値超で IsAlert=true 自動セット。
/// </remarks>
public interface IIotService
{
    // ─── センサマスタ ───
    Task<List<IotSensor>> SearchSensorsAsync(string? warehouseCd, string? sensorType);
    Task<IotSensor?> GetSensorAsync(string sensorId);
    Task<string> CreateSensorAsync(IotSensorDto dto, string? userName);
    Task UpdateSensorAsync(string sensorId, IotSensorDto dto, string? userName);
    Task DeleteSensorAsync(string sensorId, string? userName);

    // ─── 計測値（時系列） ───
    Task<List<IotSensorReading>> GetReadingsAsync(string sensorId, DateTime? from, DateTime? to, int limit = 200);

    /// <summary>計測値を 1 件投入（実機 push をエミュレート、閾値判定自動）</summary>
    Task PostReadingAsync(string sensorId, decimal value, DateTime? readAt, string? userName);

    /// <summary>疑似データ生成（センサ全件 × N 回 ランダム値）</summary>
    Task<int> SimulateAsync(int countPerSensor = 1, string? userName = null);

    /// <summary>現在のアラート一覧（最新 1 件が IsAlert=true のセンサ）</summary>
    Task<List<IotAlertDto>> CurrentAlertsAsync();
}

public class IotSensorDto
{
    public string SensorType { get; set; } = "TEMP";
    public string? SensorName { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string? LocationCd { get; set; }
    public string? Unit { get; set; }
    public decimal? MinThreshold { get; set; }
    public decimal? MaxThreshold { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Remarks { get; set; }
}

public class IotAlertDto
{
    public string SensorId { get; set; } = string.Empty;
    public string? SensorName { get; set; }
    public string SensorType { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public string? LocationCd { get; set; }
    public decimal LastValue { get; set; }
    public DateTime LastReadAt { get; set; }
    public string? AlertMessage { get; set; }
}
