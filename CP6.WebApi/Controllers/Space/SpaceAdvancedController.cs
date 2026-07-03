using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Route("api/space")]
[Authorize]
public class SpaceAdvancedController : ControllerBase
{
    private readonly IWmsPickTaskQuery _pick;
    private readonly IWmsWorkloadQuery _workload;
    private readonly IWmsDeviceQuery _device;
    private readonly CP6Context _db;

    public SpaceAdvancedController(IWmsPickTaskQuery pick, IWmsWorkloadQuery workload,
        IWmsDeviceQuery device, CP6Context db)
    { _pick = pick; _workload = workload; _device = device; _db = db; }

    private IActionResult Ok2(object? data = null, string msg = "OK") => Ok(new { code = 0, message = msg, data });

    /// <summary>拣货路径：有序拣货点（补 AbsXYZ）+ 本层 Aisle 中心线（前端建图规划动画）。</summary>
    [HttpGet("floor/{floorId:guid}/pick-path")]
    public async Task<IActionResult> PickPath(Guid floorId, [FromQuery] string taskNo, CancellationToken ct)
    {
        var path = await _pick.GetPickPathAsync(taskNo ?? "", ct);
        var codes = path.Items.Select(i => i.LocationCode).Distinct().ToList();

        var coordByCode = (await _db.Space_Locations
            .Where(l => l.FloorId == floorId && l.Placed && l.LocationCode != null && codes.Contains(l.LocationCode!))
            .Select(l => new { l.LocationCode, l.AbsX, l.AbsY, l.AbsZ })
            .ToListAsync(ct))
            .GroupBy(c => c.LocationCode!)
            .ToDictionary(g => g.Key, g => g.First());

        var stops = path.Items.Select(i =>
        {
            coordByCode.TryGetValue(i.LocationCode, out var c);
            return new
            {
                seq = i.Seq, locationCode = i.LocationCode, qty = i.Qty, materialNo = i.MaterialNo,
                absX = c?.AbsX, absY = c?.AbsY, absZ = c?.AbsZ,
            };
        }).ToList();

        var aisles = await (
            from a in _db.Space_Aisles
            join z in _db.Space_Zones on a.ZoneId equals z.Id
            where z.FloorId == floorId
            select new { aisleCode = a.AisleCode, centerline = a.Centerline }).ToListAsync(ct);

        return Ok2(new { taskNo = path.TaskNo, stops, aisles });
    }

    /// <summary>作业热图：时间窗内各库位作业频次（默认今日）。</summary>
    [HttpGet("floor/{floorId:guid}/workload")]
    public async Task<IActionResult> Workload(Guid floorId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var f = from ?? DateTime.Today;
        var t = to ?? DateTime.Today.AddDays(1);
        var items = await _workload.GetWorkloadAsync(floorId, f, t, ct);
        return Ok2(new { items, from = f, to = t });
    }

    /// <summary>全站楼层按 Level 升序自底向上累加层高赋 Z（mm）。最低 Level Z=0。 </summary>
    public static Dictionary<Guid, int> ComputeFloorZ(List<(Guid FloorId, int Level, int Height)> floors)
    {
        var ordered = floors.OrderBy(f => f.Level).ToList();
        var z = new Dictionary<Guid, int>();
        int acc = 0;
        foreach (var f in ordered)
        {
            z[f.FloorId] = acc;
            acc += f.Height;
        }
        return z;
    }

    /// <summary>站点级跨层拣货路径：stops(带 floorId)+全站楼层(含 Z)+涉及层 aisles+站点连接体。</summary>
    [HttpGet("site/{siteId:guid}/pick-path")]
    public async Task<IActionResult> SitePickPath(Guid siteId, [FromQuery] string taskNo, CancellationToken ct)
    {
        var path = await _pick.GetPickPathAsync(taskNo ?? "", ct);
        var codes = path.Items.Select(i => i.LocationCode).Distinct().ToList();

        var siteFloors = await _db.Space_Floors.Where(f => f.SiteId == siteId)
            .Select(f => new { f.Id, f.Level, f.Height, f.FloorCode }).ToListAsync(ct);
        var zMap = ComputeFloorZ(siteFloors.Select(f => (f.Id, f.Level, f.Height)).ToList());

        var siteFloorIds = siteFloors.Select(f => f.Id).ToList();
        var locs = (await _db.Space_Locations
            .Where(l => l.FloorId.HasValue && siteFloorIds.Contains(l.FloorId!.Value) && l.Placed && l.LocationCode != null && codes.Contains(l.LocationCode!))
            .Select(l => new { l.LocationCode, l.FloorId, l.AbsX, l.AbsY, l.AbsZ }).ToListAsync(ct))
            .GroupBy(l => l.LocationCode!).ToDictionary(g => g.Key, g => g.First());

        var stops = path.Items.Select(i =>
        {
            locs.TryGetValue(i.LocationCode, out var c);
            return new
            {
                seq = i.Seq, locationCode = i.LocationCode, qty = i.Qty, materialNo = i.MaterialNo,
                floorId = c?.FloorId, absX = c?.AbsX, absY = c?.AbsY, absZ = c?.AbsZ,
            };
        }).ToList();

        var involved = stops.Where(s => s.floorId != null).Select(s => s.floorId.GetValueOrDefault()).Distinct().ToList();
        var aisles = await (
            from a in _db.Space_Aisles
            join zn in _db.Space_Zones on a.ZoneId equals zn.Id
            where involved.Contains(zn.FloorId)
            select new { floorId = zn.FloorId, aisleCode = a.AisleCode, centerline = a.Centerline }).ToListAsync(ct);

        var conns = await _db.Space_Connectors.Where(c => c.SiteId == siteId).ToListAsync(ct);
        var connIds = conns.Select(c => c.Id).ToList();
        var connStops = await _db.Space_ConnectorStops.Where(s => connIds.Contains(s.ConnectorId)).ToListAsync(ct);
        var connectors = conns.Select(c => new
        {
            connectorCode = c.ConnectorCode, type = c.ConnectorType,
            waitSec = c.WaitSec, travelSecPerFloor = c.TravelSecPerFloor,
            stops = connStops.Where(s => s.ConnectorId == c.Id).Select(s => new { floorId = s.FloorId, x = s.X, y = s.Y }).ToList()
        }).ToList();

        var floors = siteFloors.Select(f => new { floorId = f.Id, floorCode = f.FloorCode, level = f.Level, height = f.Height, z = zMap[f.Id] }).ToList();
        return Ok2(new { taskNo = path.TaskNo, floors, stops, aisles, connectors });
    }

    /// <summary>设备示意（v1 占位；有 LocationCode 的设备补 AbsXYZ）。</summary>
    [HttpGet("floor/{floorId:guid}/devices")]
    public async Task<IActionResult> Devices(Guid floorId, CancellationToken ct)
    {
        var devices = await _device.GetDevicesAsync(floorId, ct);
        var codes = devices.Where(d => d.LocationCode != null).Select(d => d.LocationCode!).Distinct().ToList();

        var coordByCode = (await _db.Space_Locations
            .Where(l => l.FloorId == floorId && l.Placed && l.LocationCode != null && codes.Contains(l.LocationCode!))
            .Select(l => new { l.LocationCode, l.AbsX, l.AbsY, l.AbsZ })
            .ToListAsync(ct))
            .GroupBy(c => c.LocationCode!)
            .ToDictionary(g => g.Key, g => g.First());

        var result = devices.Select(d =>
        {
            int? x = null, y = null, z = null;
            if (d.LocationCode != null && coordByCode.TryGetValue(d.LocationCode, out var c)) { x = c.AbsX; y = c.AbsY; z = c.AbsZ; }
            return new { deviceId = d.DeviceId, type = d.Type, status = d.Status, locationCode = d.LocationCode, absX = x, absY = y, absZ = z };
        }).ToList();

        return Ok2(result);
    }
}
