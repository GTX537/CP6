using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class IotService : IIotService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "SEN";

    public IotService(CP6Context db, IWmsSequenceService seq) : this(db, seq, null, null) { }

    // ═════════ センサマスタ ═════════

    private async Task<List<IotSensor>> SearchSensorsCoreAsync(string? warehouseCd, string? sensorType)
    {
        var q = _db.IotSensors.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(sensorType))  q = q.Where(x => x.SensorType == sensorType);
        return await q.OrderBy(x => x.WarehouseCd).ThenBy(x => x.LocationCd).ToListAsync();
    }

    private async Task<IotSensor?> GetSensorCoreAsync(string sensorId)
        => await _db.IotSensors.AsNoTracking().FirstOrDefaultAsync(x => x.SensorId == sensorId && !x.IsDeleted);

    private async Task<string> CreateSensorCoreAsync(IotSensorDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.SensorType) || string.IsNullOrWhiteSpace(dto.WarehouseCd))
            throw new InvalidOperationException("SensorType / WarehouseCd required");
        var id = await _seq.NextAsync(Prefix);
        _db.IotSensors.Add(new IotSensor
        {
            SensorId = id, SensorType = dto.SensorType, SensorName = dto.SensorName,
            WarehouseCd = dto.WarehouseCd, LocationCd = dto.LocationCd,
            Unit = dto.Unit ?? DefaultUnit(dto.SensorType),
            MinThreshold = dto.MinThreshold, MaxThreshold = dto.MaxThreshold,
            IsEnabled = dto.IsEnabled, Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task UpdateSensorCoreAsync(string sensorId, IotSensorDto dto, string? userName)
    {
        var s = await GetSensorTracked(sensorId);
        s.SensorType = dto.SensorType; s.SensorName = dto.SensorName;
        s.WarehouseCd = dto.WarehouseCd; s.LocationCd = dto.LocationCd;
        s.Unit = dto.Unit ?? s.Unit;
        s.MinThreshold = dto.MinThreshold; s.MaxThreshold = dto.MaxThreshold;
        s.IsEnabled = dto.IsEnabled; s.Remarks = dto.Remarks;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task DeleteSensorCoreAsync(string sensorId, string? userName)
    {
        var s = await GetSensorTracked(sensorId);
        s.IsDeleted = true;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ═════════ 計測値 ═════════

    private async Task<List<IotSensorReading>> GetReadingsCoreAsync(string sensorId, DateTime? from, DateTime? to, int limit = 200)
    {
        var q = _db.IotSensorReadings.AsNoTracking().Where(x => !x.IsDeleted && x.SensorId == sensorId);
        if (from.HasValue) q = q.Where(x => x.ReadAt >= from.Value);
        if (to.HasValue)   q = q.Where(x => x.ReadAt <= to.Value);
        return await q.OrderByDescending(x => x.ReadAt).Take(Math.Min(limit, 5000)).ToListAsync();
    }

    private async Task PostReadingCoreAsync(string sensorId, decimal value, DateTime? readAt, string? userName)
    {
        var sensor = await GetSensorTracked(sensorId);
        var at = readAt ?? DateTime.Now;
        var isAlert = (sensor.MinThreshold.HasValue && value < sensor.MinThreshold.Value)
                   || (sensor.MaxThreshold.HasValue && value > sensor.MaxThreshold.Value);
        string? alertMsg = isAlert
            ? $"閾値違反: {value} (範囲 {sensor.MinThreshold?.ToString() ?? "-"} 〜 {sensor.MaxThreshold?.ToString() ?? "-"})"
            : null;

        _db.IotSensorReadings.Add(new IotSensorReading
        {
            SensorId = sensorId, ReadAt = at, Value = value,
            IsAlert = isAlert, AlertMessage = alertMsg, Creator = userName,
        });
        await RefreshLatestRecordedAsync(sensor, value, at);
        sensor.Modifier = userName; sensor.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    private async Task<int> SimulateCoreAsync(int countPerSensor = 1, string? userName = null)
    {
        if (countPerSensor is < 1 or > 100)
            throw new OrderPriceCorrectionException("IOT_SIMULATION_LIMIT", 400);
        var sensors = await _db.IotSensors.Where(x => !x.IsDeleted && x.IsEnabled).Take(101).ToListAsync();
        if (sensors.Count > 100) throw new OrderPriceCorrectionException("IOT_SIMULATION_LIMIT", 400);
        // Resolve the whole requested generator set before staging any observations.
        var plans = sensors.Select(sensor => (Sensor: sensor, Generate: ResolveSimulator(sensor.SensorType))).ToArray();
        var rnd = new Random(); var added = 0; var batchAt = DateTime.Now;
        foreach (var plan in plans)
        {
            var sensor = plan.Sensor;
            for (var i = 0; i < countPerSensor; i++)
            {
                var value = plan.Generate(rnd); var at = batchAt.AddSeconds(-i * 5);
                var isAlert = sensor.MinThreshold.HasValue && value < sensor.MinThreshold.Value
                    || sensor.MaxThreshold.HasValue && value > sensor.MaxThreshold.Value;
                _db.IotSensorReadings.Add(new IotSensorReading
                {
                    SensorId = sensor.SensorId, ReadAt = at, Value = value, IsAlert = isAlert,
                    AlertMessage = isAlert ? $"\u95be\u5024\u9055\u53cd: {value}" : null, Creator = userName ?? "sim"
                });
                added++;
                if (i == 0) await RefreshLatestRecordedAsync(sensor, value, at);
            }
            sensor.Modifier = userName; sensor.ModifyDate = batchAt;
        }
        await _db.SaveChangesAsync();
        return added;
    }

    private async Task<List<IotAlertDto>> CurrentAlertsCoreAsync()
    {
        // 各センサの最新読みで IsAlert=true のもの
        var sensors = await _db.IotSensors.AsNoTracking().Where(x => !x.IsDeleted).ToListAsync();
        var result = new List<IotAlertDto>();
        foreach (var s in sensors)
        {
            var latest = await _db.IotSensorReadings.AsNoTracking()
                .Where(x => x.SensorId == s.SensorId && !x.IsDeleted)
                .OrderByDescending(x => x.ReadAt).FirstOrDefaultAsync();
            if (latest != null && latest.IsAlert)
            {
                result.Add(new IotAlertDto
                {
                    SensorId = s.SensorId, SensorName = s.SensorName, SensorType = s.SensorType,
                    WarehouseCd = s.WarehouseCd, LocationCd = s.LocationCd,
                    LastValue = latest.Value, LastReadAt = latest.ReadAt,
                    AlertMessage = latest.AlertMessage,
                });
            }
        }
        return result.OrderByDescending(x => x.LastReadAt).ToList();
    }

    // ─── helpers ───

    private async Task<IotSensor> GetSensorTracked(string sensorId)
        => await _db.IotSensors.FirstOrDefaultAsync(x => x.SensorId == sensorId && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");

    private static string DefaultUnit(string sensorType) => sensorType switch
    {
        IotSensorType.Temperature => "℃",
        IotSensorType.Humidity    => "%",
        IotSensorType.Shock       => "G",
        IotSensorType.Shelf       => "ON-OFF",
        _ => string.Empty,
    };

    private async Task RefreshLatestRecordedAsync(IotSensor sensor, decimal value, DateTime at)
    {
        // Existing heads may already be stale. History, rather than that projection, owns the comparison.
        // Strictly later rows win; equal-time legacy inputs retain their existing arrival behavior.
        var later = await _db.IotSensorReadings.AsNoTracking()
            .Where(r => !r.IsDeleted && r.SensorId == sensor.SensorId && r.ReadAt > at)
            .OrderByDescending(r => r.ReadAt).FirstOrDefaultAsync();
        sensor.LastValue = later?.Value ?? value;
        sensor.LastReadAt = later?.ReadAt ?? at;
    }

    private static Func<Random, decimal> ResolveSimulator(string sensorType) => sensorType switch
    {
        IotSensorType.Temperature => rnd => Math.Round((decimal)(15 + rnd.NextDouble() * 18), 1),
        IotSensorType.Humidity => rnd => Math.Round((decimal)(30 + rnd.NextDouble() * 45), 1),
        IotSensorType.Shock => rnd => rnd.Next(0, 100) > 95 ? 5m + (decimal)rnd.NextDouble() * 3m : Math.Round((decimal)(rnd.NextDouble() * 2), 2),
        IotSensorType.Shelf => rnd => rnd.Next(0, 100) > 50 ? 1m : 0m,
        _ => throw new OrderPriceCorrectionException("IOT_SIMULATOR_UNAVAILABLE", 503)
    };
}
