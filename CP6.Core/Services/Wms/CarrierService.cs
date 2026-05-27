using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class CarrierService : ICarrierService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string Prefix = "SHIP";

    public CarrierService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<CarrierShipment>> SearchAsync(CarrierSearchQuery q)
    {
        var query = _db.CarrierShipments.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.ShipmentNo)) query = query.Where(x => x.ShipmentNo.Contains(q.ShipmentNo));
        if (!string.IsNullOrWhiteSpace(q.PackageNo))  query = query.Where(x => x.PackageNo == q.PackageNo);
        if (!string.IsNullOrWhiteSpace(q.TrackingNo)) query = query.Where(x => x.TrackingNo == q.TrackingNo);
        if (!string.IsNullOrWhiteSpace(q.CarrierCd))  query = query.Where(x => x.CarrierCd == q.CarrierCd);
        if (!string.IsNullOrWhiteSpace(q.CustomerCd)) query = query.Where(x => x.CustomerCd == q.CustomerCd);
        if (q.Status.HasValue)                        query = query.Where(x => x.Status == q.Status.Value);
        return await query.OrderByDescending(x => x.CreateDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<CarrierShipment?> GetAsync(string shipmentNo)
        => await _db.CarrierShipments.AsNoTracking().FirstOrDefaultAsync(x => x.ShipmentNo == shipmentNo && !x.IsDeleted);

    public async Task<string> CreateShipmentAsync(CarrierShipmentDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.PackageNo))
            throw new InvalidOperationException("PackageNo required");
        if (string.IsNullOrWhiteSpace(dto.CarrierCd))
            throw new InvalidOperationException("CarrierCd required");

        var no = await _seq.NextAsync(Prefix);
        var trackingNo = !string.IsNullOrWhiteSpace(dto.TrackingNo)
            ? dto.TrackingNo
            : GenerateTrackingNo(dto.CarrierCd);

        _db.CarrierShipments.Add(new CarrierShipment
        {
            ShipmentNo = no,
            PackageNo = dto.PackageNo,
            CarrierCd = dto.CarrierCd,
            TrackingNo = trackingNo,
            ServiceType = dto.ServiceType,
            Status = CarrierShipmentStatus.Created,
            CustomerCd = dto.CustomerCd,
            ShipToAddress = dto.ShipToAddress, ShipToName = dto.ShipToName, ShipToTel = dto.ShipToTel,
            WeightKg = dto.WeightKg, CarrierFee = dto.CarrierFee,
            EventsJson = "[]",
            Remarks = dto.Remarks, Creator = userName,
        });
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task AddEventAsync(string shipmentNo, CarrierEventDto evt, string? userName)
    {
        var s = await GetTracked(shipmentNo);
        var list = string.IsNullOrWhiteSpace(s.EventsJson)
            ? new List<CarrierEventDto>()
            : JsonSerializer.Deserialize<List<CarrierEventDto>>(s.EventsJson) ?? new List<CarrierEventDto>();
        list.Add(evt);
        s.EventsJson = JsonSerializer.Serialize(list);
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task MarkPickedUpAsync(string shipmentNo, string? userName)
    {
        var s = await GetTracked(shipmentNo);
        if (s.Status != CarrierShipmentStatus.Created)
            throw new InvalidOperationException("WM-MSG-043: 作成済のみ集荷可");
        s.Status = CarrierShipmentStatus.PickedUp;
        s.PickedUpAt = DateTime.Now;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await AppendEvent(s, "PickedUp", "集荷完了");
        await _db.SaveChangesAsync();
    }

    public async Task MarkInTransitAsync(string shipmentNo, string? userName)
    {
        var s = await GetTracked(shipmentNo);
        if (s.Status != CarrierShipmentStatus.PickedUp)
            throw new InvalidOperationException("WM-MSG-043: 集荷済のみ配送移行可");
        s.Status = CarrierShipmentStatus.InTransit;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await AppendEvent(s, "InTransit", "配送中");
        await _db.SaveChangesAsync();
    }

    public async Task MarkDeliveredAsync(string shipmentNo, string? userName)
    {
        var s = await GetTracked(shipmentNo);
        if (s.Status != CarrierShipmentStatus.InTransit && s.Status != CarrierShipmentStatus.PickedUp)
            throw new InvalidOperationException("WM-MSG-043: 配送中/集荷済のみ完了可");
        s.Status = CarrierShipmentStatus.Delivered;
        s.DeliveredAt = DateTime.Now;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await AppendEvent(s, "Delivered", "配達完了");
        await _db.SaveChangesAsync();
    }

    public async Task MarkFailedAsync(string shipmentNo, string reason, string? userName)
    {
        var s = await GetTracked(shipmentNo);
        if (s.Status == CarrierShipmentStatus.Delivered)
            throw new InvalidOperationException("WM-MSG-043: 配達完了済は変更不可");
        s.Status = CarrierShipmentStatus.Failed;
        s.Modifier = userName; s.ModifyDate = DateTime.Now;
        await AppendEvent(s, "Failed", reason ?? "配達失敗");
        await _db.SaveChangesAsync();
    }

    private static string GenerateTrackingNo(string carrierCd)
    {
        var ymd = DateTime.Today.ToString("yyyyMMdd");
        var rnd = new Random().Next(100000, 1000000);
        return $"{carrierCd}-{ymd}-{rnd}";
    }

    private async Task AppendEvent(CarrierShipment s, string status, string msg)
    {
        var list = string.IsNullOrWhiteSpace(s.EventsJson)
            ? new List<CarrierEventDto>()
            : JsonSerializer.Deserialize<List<CarrierEventDto>>(s.EventsJson) ?? new List<CarrierEventDto>();
        list.Add(new CarrierEventDto { Ts = DateTime.Now, Status = status, Message = msg });
        s.EventsJson = JsonSerializer.Serialize(list);
        await Task.CompletedTask;
    }

    private async Task<CarrierShipment> GetTracked(string shipmentNo)
        => await _db.CarrierShipments.FirstOrDefaultAsync(x => x.ShipmentNo == shipmentNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
}
