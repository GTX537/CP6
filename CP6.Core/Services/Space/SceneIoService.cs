using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>
/// 场景导入导出服务实现（ch01 §G-3，v1.1 多租户规则）。
/// 构造只注入 CP6Context；租户由 SaveChanges StampTenant 自动盖章。
/// </summary>
public class SceneIoService : ISceneIoService
{
    private readonly CP6Context _db;

    public SceneIoService(CP6Context db) => _db = db;

    /// <inheritdoc/>
    public async Task<SceneExportDto> ExportAsync(Guid floorId)
    {
        var floor = await _db.Space_Floors.FirstOrDefaultAsync(f => f.Id == floorId)
                    ?? throw new BizException("E-SPACE-001");

        var zones = await _db.Space_Zones
            .Where(z => z.FloorId == floorId)
            .Select(z => new ZoneExportDto
            {
                Id = z.Id, ZoneCode = z.ZoneCode, ZoneName = z.ZoneName,
                ZoneType = z.ZoneType, Polygon = z.Polygon, Color = z.Color
            }).ToListAsync();

        var zoneIds = zones.Select(z => z.Id).ToList();
        var aisles = await _db.Space_Aisles
            .Where(a => zoneIds.Contains(a.ZoneId))
            .Select(a => new AisleExportDto
            {
                Id = a.Id, ZoneId = a.ZoneId, AisleCode = a.AisleCode,
                Polygon = a.Polygon, Centerline = a.Centerline
            }).ToListAsync();

        var racks = await _db.Space_Racks
            .Where(r => r.FloorId == floorId)
            .Select(r => new RackExportDto
            {
                Id = r.Id, ZoneId = r.ZoneId, AisleId = r.AisleId,
                RackCode = r.RackCode, X = r.X, Y = r.Y, Z = r.Z, RotationZ = r.RotationZ,
                Cols = r.Cols, Levels = r.Levels, DepthCount = r.DepthCount,
                CellW = r.CellW, CellH = r.CellH, CellD = r.CellD
            }).ToListAsync();

        return new SceneExportDto
        {
            Source = SpaceDataSourceDto.Runtime(),
            Meta = new SceneExportMeta
            {
                FloorCode = floor.FloorCode, FloorName = floor.FloorName,
                Level = floor.Level, Height = floor.Height,
                UnderlayImage = floor.UnderlayImage, UnderlayScale = floor.UnderlayScale,
                UnderlayOffsetX = floor.UnderlayOffsetX, UnderlayOffsetY = floor.UnderlayOffsetY,
                OriginX = floor.OriginX, OriginY = floor.OriginY
            },
            Zones  = zones,
            Aisles = aisles,
            Racks  = racks
        };
    }

    /// <inheritdoc/>
    public async Task<Guid> ImportAsync(Guid siteId, SceneExportDto dto, string? user)
    {
        var meta = dto.Meta;

        // ── 新楼层 ──────────────────────────────────────────────────
        var newFloor = new Space_Floor
        {
            Id              = Guid.NewGuid(),
            SiteId          = siteId,
            Level           = meta.Level,
            FloorCode       = meta.FloorCode,
            FloorName       = meta.FloorName,
            Height          = meta.Height,
            UnderlayImage   = meta.UnderlayImage,
            UnderlayScale   = meta.UnderlayScale,
            UnderlayOffsetX = meta.UnderlayOffsetX,
            UnderlayOffsetY = meta.UnderlayOffsetY,
            OriginX         = meta.OriginX,
            OriginY         = meta.OriginY,
            Creator         = user,
            CreateDate      = DateTime.Now
        };
        _db.Space_Floors.Add(newFloor);

        // ── Zones GUID 映射 ──────────────────────────────────────────
        var zoneMap = new Dictionary<Guid, Guid>();
        foreach (var zd in dto.Zones)
        {
            var newId = Guid.NewGuid();
            zoneMap[zd.Id] = newId;
            _db.Space_Zones.Add(new Space_Zone
            {
                Id         = newId,
                FloorId    = newFloor.Id,
                ZoneCode   = zd.ZoneCode,
                ZoneName   = zd.ZoneName,
                ZoneType   = zd.ZoneType,
                Polygon    = zd.Polygon,
                Color      = zd.Color,
                Creator    = user,
                CreateDate = DateTime.Now
            });
        }

        // ── Aisles GUID 映射 ─────────────────────────────────────────
        var aisleMap = new Dictionary<Guid, Guid>();
        foreach (var ad in dto.Aisles)
        {
            var newId = Guid.NewGuid();
            aisleMap[ad.Id] = newId;
            _db.Space_Aisles.Add(new Space_Aisle
            {
                Id         = newId,
                ZoneId     = zoneMap.GetValueOrDefault(ad.ZoneId, ad.ZoneId),
                AisleCode  = ad.AisleCode,
                Polygon    = ad.Polygon,
                Centerline = ad.Centerline,
                Creator    = user,
                CreateDate = DateTime.Now
            });
        }

        // ── Racks GUID 映射 ──────────────────────────────────────────
        var rackMap = new Dictionary<Guid, Space_Rack>();
        foreach (var rd in dto.Racks)
        {
            var newId   = Guid.NewGuid();
            var newRack = new Space_Rack
            {
                Id         = newId,
                ZoneId     = zoneMap.GetValueOrDefault(rd.ZoneId, rd.ZoneId),
                AisleId    = rd.AisleId.HasValue ? aisleMap.GetValueOrDefault(rd.AisleId.Value, rd.AisleId.Value) : null,
                FloorId    = newFloor.Id,
                RackCode   = rd.RackCode,
                X          = rd.X,
                Y          = rd.Y,
                Z          = rd.Z,
                RotationZ  = rd.RotationZ,
                Cols       = rd.Cols,
                Levels     = rd.Levels,
                DepthCount = rd.DepthCount,
                CellW      = rd.CellW,
                CellH      = rd.CellH,
                CellD      = rd.CellD,
                Enable     = true,
                Creator    = user,
                CreateDate = DateTime.Now
            };
            _db.Space_Racks.Add(newRack);
            rackMap[rd.Id] = newRack;
        }

        await _db.SaveChangesAsync();

        // ── 库位全枚举重建（按货架参数 col×level×depth）────────────────
        var locations = new List<Space_Location>();
        foreach (var (oldRackId, rack) in rackMap)
        {
            for (int col = 1; col <= rack.Cols; col++)
            for (int level = 1; level <= rack.Levels; level++)
            for (int depth = 1; depth <= rack.DepthCount; depth++)
            {
                var (absX, absY, absZ) = LocationGeometryService.ComputeAbs(rack, col, level, depth);
                locations.Add(new Space_Location
                {
                    Id           = Guid.NewGuid(),
                    RackId       = rack.Id,
                    FloorId      = newFloor.Id,
                    Col          = col,
                    Level        = level,
                    Depth        = depth,
                    AbsX         = absX,
                    AbsY         = absY,
                    AbsZ         = absZ,
                    SizeW        = rack.CellW,
                    SizeH        = rack.CellH,
                    SizeD        = rack.CellD,
                    Placed       = true,
                    Status       = 0,
                    CodeOrigin   = 1,
                    LocationCode = null,
                    Creator      = user,
                    CreateDate   = DateTime.Now
                });
            }
        }

        _db.Space_Locations.AddRange(locations);
        await _db.SaveChangesAsync();

        return newFloor.Id;
    }
}
