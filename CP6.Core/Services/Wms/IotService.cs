using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class IotService : IIotService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "SEN";

    public IotService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    // ═════════ センサマスタ ═════════

    public async Task<List<IotSensor>> SearchSensorsAsync(string? warehouseCd, string? sensorType)
    {
        var q = _db.IotSensors.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(sensorType))  q = q.Where(x => x.SensorType == sensorType);
        return await q.OrderBy(x => x.WarehouseCd).ThenBy(x => x.LocationCd).ToListAsync();
    }

    public async Task<IotSensor?> GetSensorAsync(string sensorId)
        => await _db.IotSensors.AsNoTracking().FirstOrDefaultAsync(x => x.SensorId == sensorId && !x.IsDeleted);

    public async Task<string> CreateSensorAsync(IotSensorDto dto, string? userName)
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

    public async Task UpdateSensorAsync(string sensorId, IotSensorDto dto, string? userName)
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

    public async Task DeleteSensorAsync(string sensorId, string? userName)
    {
        var s = await GetSensorTracked(sensorId);
        s.IsDeleted = true;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ═════════ 計測値 ═════════

    public async Task<List<IotSensorReading>> GetReadingsAsync(string sensorId, DateTime? from, DateTime? to, int limit = 200)
    {
        var q = _db.IotSensorReadings.AsNoTracking().Where(x => !x.IsDeleted && x.SensorId == sensorId);
        if (from.HasValue) q = q.Where(x => x.ReadAt >= from.Value);
        if (to.HasValue)   q = q.Where(x => x.ReadAt <= to.Value);
        return await q.OrderByDescending(x => x.ReadAt).Take(Math.Min(limit, 5000)).ToListAsync();
    }

    public async Task PostReadingAsync(string sensorId, decimal value, DateTime? readAt, string? userName)
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
        sensor.LastValue = value;
        sensor.LastReadAt = at;
        sensor.Modifier = userName; sensor.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<int> SimulateAsync(int countPerSensor = 1, string? userName = null)
    {
        var sensors = await _db.IotSensors.Where(x => !x.IsDeleted && x.IsEnabled).ToListAsync();
        if (sensors.Count == 0) return 0;
        var rnd = new Random();
        var added = 0;
        foreach (var s in sensors)
        {
            for (int i = 0; i < countPerSensor; i++)
            {
                var v = SimValue(s, rnd);
                var isAlert = (s.MinThreshold.HasValue && v < s.MinThreshold.Value)
                           || (s.MaxThreshold.HasValue && v > s.MaxThreshold.Value);
                _db.IotSensorReadings.Add(new IotSensorReading
                {
                    SensorId = s.SensorId,
                    ReadAt = DateTime.Now.AddSeconds(-i * 5),
                    Value = v, IsAlert = isAlert,
                    AlertMessage = isAlert ? $"閾値違反: {v}" : null,
                    Creator = userName ?? "sim",
                });
                added++;
                s.LastValue = v;
                s.LastReadAt = DateTime.Now;
            }
        }
        await _db.SaveChangesAsync();
        return added;
    }

    public async Task<List<IotAlertDto>> CurrentAlertsAsync()
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

    private static decimal SimValue(IotSensor s, Random rnd) => s.SensorType switch
    {
        // 温度：通常 15-30°C、たまに範囲外
        IotSensorType.Temperature => Math.Round((decimal)(15 + rnd.NextDouble() * 18), 1),
        // 湿度：通常 30-70%
        IotSensorType.Humidity    => Math.Round((decimal)(30 + rnd.NextDouble() * 45), 1),
        // 衝撃：通常 0-2G、まれに大
        IotSensorType.Shock       => rnd.Next(0, 100) > 95 ? 5m + (decimal)rnd.NextDouble() * 3m : Math.Round((decimal)(rnd.NextDouble() * 2), 2),
        IotSensorType.Shelf       => rnd.Next(0, 100) > 50 ? 1m : 0m,
        _ => 0m,
    };
}
