using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// WMS 連携・モバイル・IoT 単体テスト（MSBBWM310/320/330）
///
/// テスト観点：
/// WCS:
///   1. CreateAsync + 状态遷移 0→1→2→3
///   2. 失敗ルート 1/2→9 (ErrorMessage 保持)
///   3. 不正状态遷移は拒否
/// Carrier:
///   4. CreateShipment + TrackingNo 自動生成
///   5. AddEvent + EventsJson 履歴蓄積
///   6. PickedUp→InTransit→Delivered 状态遷移
/// IoT:
///   7. CreateSensor + PostReading 閾値判定 isAlert
///   8. SimulateAsync で複数センサ × N 件生成
///   9. CurrentAlerts 最新警報抽出
/// </summary>
public class WmsConnectivityServiceTests
{
    private static (CP6.Core.EFDbContext.CP6Context db, WmsSequenceService seq) Create()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        return (db, seq);
    }

    // ═════════ WCS ═════════

    [Fact]
    public async Task Wcs_CreateAndStateTransition()
    {
        var (db, seq) = Create();
        var svc = new WcsService(db, seq);
        var no = await svc.CreateAsync(new WcsTaskDto
        {
            TaskType = "MOVE",
            FromWarehouseCd = "W01", FromLocationCd = "L01",
            ToWarehouseCd = "W01", ToLocationCd = "L02",
            ProductCd = "P1", Qty = 10,
        }, "u");

        await svc.DispatchAsync(no, "AGV01", "u");
        var t = await db.WcsTasks.SingleAsync();
        Assert.Equal(WcsTaskStatus.Dispatched, t.Status);
        Assert.Equal("AGV01", t.DeviceCd);
        Assert.NotNull(t.DispatchedAt);

        await svc.StartAsync(no, "u");
        t = await db.WcsTasks.SingleAsync();
        Assert.Equal(WcsTaskStatus.Executing, t.Status);

        await svc.CompleteAsync(no, "u");
        t = await db.WcsTasks.SingleAsync();
        Assert.Equal(WcsTaskStatus.Completed, t.Status);
        Assert.NotNull(t.CompletedAt);
    }

    [Fact]
    public async Task Wcs_FailureRoute_ShouldSaveError()
    {
        var (db, seq) = Create();
        var svc = new WcsService(db, seq);
        var no = await svc.CreateAsync(new WcsTaskDto { TaskType = "MOVE" }, "u");
        await svc.DispatchAsync(no, "AGV01", "u");
        await svc.StartAsync(no, "u");
        await svc.FailAsync(no, "ターゲット棚 障害物", "u");

        var t = await db.WcsTasks.SingleAsync();
        Assert.Equal(WcsTaskStatus.Failed, t.Status);
        Assert.Equal("ターゲット棚 障害物", t.ErrorMessage);
    }

    [Fact]
    public async Task Wcs_InvalidTransition_ShouldThrow()
    {
        var (db, seq) = Create();
        var svc = new WcsService(db, seq);
        var no = await svc.CreateAsync(new WcsTaskDto { TaskType = "MOVE" }, "u");
        // Created 直接 Start (without Dispatch) → 拒否
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartAsync(no, "u"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompleteAsync(no, "u"));
    }

    // ═════════ Carrier ═════════

    [Fact]
    public async Task Carrier_CreateShipment_AutoTracking()
    {
        var (db, seq) = Create();
        var svc = new CarrierService(db, seq);
        var no = await svc.CreateShipmentAsync(new CarrierShipmentDto
        {
            PackageNo = "PKG001", CarrierCd = "YAMATO", CustomerCd = "C001",
            ShipToAddress = "東京都...", WeightKg = 1.5m,
        }, "u");

        var s = await db.CarrierShipments.SingleAsync();
        Assert.Equal(CarrierShipmentStatus.Created, s.Status);
        Assert.StartsWith("YAMATO-", s.TrackingNo);
        Assert.Equal("[]", s.EventsJson);
    }

    [Fact]
    public async Task Carrier_AddEvent_AccumulatesJson()
    {
        var (db, seq) = Create();
        var svc = new CarrierService(db, seq);
        var no = await svc.CreateShipmentAsync(new CarrierShipmentDto { PackageNo = "P1", CarrierCd = "YAMATO" }, "u");

        await svc.AddEventAsync(no, new CarrierEventDto { Location = "Tokyo", Status = "ScanIn", Message = "Arrived at branch" }, "u");
        await svc.AddEventAsync(no, new CarrierEventDto { Location = "Yokohama", Status = "InTransit", Message = "Transit" }, "u");
        var s = await db.CarrierShipments.SingleAsync();
        Assert.Contains("ScanIn", s.EventsJson!);
        Assert.Contains("Transit", s.EventsJson!);
        // 2 件記録されている
        var events = System.Text.Json.JsonSerializer.Deserialize<List<CarrierEventDto>>(s.EventsJson!);
        Assert.Equal(2, events!.Count);
    }

    [Fact]
    public async Task Carrier_StateTransition_PickedUp_To_Delivered()
    {
        var (db, seq) = Create();
        var svc = new CarrierService(db, seq);
        var no = await svc.CreateShipmentAsync(new CarrierShipmentDto { PackageNo = "P1", CarrierCd = "YAMATO" }, "u");

        await svc.MarkPickedUpAsync(no, "u");
        Assert.Equal(CarrierShipmentStatus.PickedUp, (await db.CarrierShipments.SingleAsync()).Status);

        await svc.MarkInTransitAsync(no, "u");
        Assert.Equal(CarrierShipmentStatus.InTransit, (await db.CarrierShipments.SingleAsync()).Status);

        await svc.MarkDeliveredAsync(no, "u");
        var s = await db.CarrierShipments.SingleAsync();
        Assert.Equal(CarrierShipmentStatus.Delivered, s.Status);
        Assert.NotNull(s.DeliveredAt);
        // 3 つの状态遷移で event は 3 件
        Assert.Contains("PickedUp",  s.EventsJson!);
        Assert.Contains("InTransit", s.EventsJson!);
        Assert.Contains("Delivered", s.EventsJson!);
    }

    // ═════════ IoT ═════════

    [Fact]
    public async Task Iot_PostReading_DetectsAlert()
    {
        var (db, seq) = Create();
        var svc = new IotService(db, seq);
        var sid = await svc.CreateSensorAsync(new IotSensorDto
        {
            SensorType = "TEMP", WarehouseCd = "W01",
            MinThreshold = 2m, MaxThreshold = 8m,  // 冷蔵
        }, "u");

        // 正常値
        await svc.PostReadingAsync(sid, 5m, null, "u");
        var ok = await db.IotSensorReadings.SingleAsync(r => r.SensorId == sid);
        Assert.False(ok.IsAlert);

        // 警報値（上限超）
        await svc.PostReadingAsync(sid, 15m, null, "u");
        var alert = await db.IotSensorReadings.Where(r => r.SensorId == sid).OrderByDescending(r => r.ReadAt).FirstAsync();
        Assert.True(alert.IsAlert);
        Assert.Contains("閾値違反", alert.AlertMessage!);
    }

    [Fact]
    public async Task Iot_Simulate_GeneratesMultipleReadings()
    {
        var (db, seq) = Create();
        var svc = new IotService(db, seq);
        await svc.CreateSensorAsync(new IotSensorDto { SensorType = "TEMP",  WarehouseCd = "W01", MinThreshold = 2, MaxThreshold = 8 }, "u");
        await svc.CreateSensorAsync(new IotSensorDto { SensorType = "HUMID", WarehouseCd = "W01", MinThreshold = 30, MaxThreshold = 70 }, "u");

        var n = await svc.SimulateAsync(countPerSensor: 5);
        Assert.Equal(10, n);  // 2 sensors × 5 readings
        Assert.Equal(10, await db.IotSensorReadings.CountAsync());
    }

    [Fact]
    public async Task Iot_CurrentAlerts_ReflectsLatestState()
    {
        var (db, seq) = Create();
        var svc = new IotService(db, seq);
        var s1 = await svc.CreateSensorAsync(new IotSensorDto { SensorType = "TEMP", WarehouseCd = "W01", MinThreshold = 2, MaxThreshold = 8 }, "u");
        var s2 = await svc.CreateSensorAsync(new IotSensorDto { SensorType = "TEMP", WarehouseCd = "W01", MinThreshold = 2, MaxThreshold = 8 }, "u");

        // s1: 警報状态
        await svc.PostReadingAsync(s1, 99m, null, "u");
        // s2: 警報出てから正常に戻る → 最新は非警報
        await svc.PostReadingAsync(s2, 99m, null, "u");
        await svc.PostReadingAsync(s2, 5m, null, "u");

        var alerts = await svc.CurrentAlertsAsync();
        Assert.Single(alerts);
        Assert.Equal(s1, alerts[0].SensorId);
    }
}
