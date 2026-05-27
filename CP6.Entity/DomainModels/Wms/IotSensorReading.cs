using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// IoT センサ マスタ（MSBBWM330）
/// </summary>
/// <remarks>
/// 仕様書 §42 倉庫内の温湿度・衝撃・棚センサ マスタ。
/// 業務 PK：SensorId（SEN-XXXX）
/// </remarks>
[Table("T_IotSensor")]
public class IotSensor : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string SensorId { get; set; } = string.Empty;

    /// <summary>センサ種別：TEMP/HUMID/SHOCK/SHELF</summary>
    [Required, MaxLength(10)]
    public string SensorType { get; set; } = "TEMP";

    /// <summary>センサ名称</summary>
    [MaxLength(100)] public string? SensorName { get; set; }

    /// <summary>設置 倉庫CD</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>設置 ロケCD</summary>
    [MaxLength(30)] public string? LocationCd { get; set; }

    /// <summary>単位（℃/%/G/ON-OFF）</summary>
    [MaxLength(10)] public string? Unit { get; set; }

    /// <summary>下限閾値</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? MinThreshold { get; set; }

    /// <summary>上限閾値</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? MaxThreshold { get; set; }

    /// <summary>有効フラグ</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>最終受信値</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? LastValue { get; set; }

    /// <summary>最終受信日時</summary>
    public DateTime? LastReadAt { get; set; }

    [MaxLength(500)] public string? Remarks { get; set; }
}

/// <summary>
/// IoT センサ 計測値（時系列ログ）
/// </summary>
[Table("T_IotSensorReading")]
public class IotSensorReading : BaseBizEntity
{
    [Required, MaxLength(25)]
    public string SensorId { get; set; } = string.Empty;

    public DateTime ReadAt { get; set; } = DateTime.Now;

    [Column(TypeName = "decimal(18,4)")]
    public decimal Value { get; set; }

    /// <summary>閾値超過警報フラグ</summary>
    public bool IsAlert { get; set; } = false;

    [MaxLength(200)] public string? AlertMessage { get; set; }
}
