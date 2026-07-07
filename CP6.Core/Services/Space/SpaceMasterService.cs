using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Space;

/// <summary>
/// Space 主数据服务实现（ch00 §9，v1.1 多租户规则）。
///
/// v1.1 约定（全文遵守）：
///   · 构造注入 CP6Context + LocationGeometryService + ILocationPublishService（删除放行路径/改挂 re-publish 用），不注入任何租户上下文。
///   · 查询不写 .Where(x => x.TenantId == ...)——CP6Context.OnModelCreating 已对所有
///     BaseTenantEntity 子类反射注册全局查询过滤，自动 WHERE TenantId = CurrentTenantId。
///   · 创建实体不写 TenantId = ...——SaveChanges 写入盖章自动补当前租户。
///   · 仅保留 Creator = user / CreateDate = DateTime.Now（日期用 DateTime.Now，仓约定）。
/// </summary>
public class SpaceMasterService : ISpaceMasterService
{
    private readonly CP6Context _db;
    private readonly LocationGeometryService _geo;
    private readonly ILocationPublishService _publish;

    public SpaceMasterService(CP6Context db, LocationGeometryService geo, ILocationPublishService publish)
    {
        _db  = db;
        _geo = geo;
        _publish = publish;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Site
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Guid> CreateSiteAsync(SiteDto d, string? user)
    {
        if (await _db.Space_Sites.AnyAsync(x => x.SiteCode == d.SiteCode))
            throw new InvalidOperationException("E-SPACE-001");
        var e = new Space_Site
        {
            Id         = Guid.NewGuid(),
            SiteCode   = d.SiteCode,
            SiteName   = d.SiteName,
            Address    = d.Address,
            Lng        = d.Lng,
            Lat        = d.Lat,
            Enable     = d.Enable,
            Creator    = user,
            CreateDate = DateTime.Now
        };
        _db.Space_Sites.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpdateSiteAsync(Guid id, SiteDto d, string? user)
    {
        var e = await _db.Space_Sites.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("E-SPACE-001");
        e.SiteCode   = d.SiteCode;
        e.SiteName   = d.SiteName;
        e.Address    = d.Address;
        e.Lng        = d.Lng;
        e.Lat        = d.Lat;
        e.Enable     = d.Enable;
        e.Modifier   = user;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<SiteDto>> ListSitesAsync() =>
        await _db.Space_Sites.Select(x => new SiteDto
        {
            Id = x.Id, SiteCode = x.SiteCode, SiteName = x.SiteName,
            Address = x.Address, Lng = x.Lng, Lat = x.Lat, Enable = x.Enable
        }).ToListAsync();

    public async Task DeleteSiteAsync(Guid id)
    {
        if (await _db.Space_Floors.AnyAsync(x => x.SiteId == id))
            throw new InvalidOperationException("E-SPACE-007");
        var e = await _db.Space_Sites.FirstOrDefaultAsync(x => x.Id == id);
        if (e != null) { _db.Space_Sites.Remove(e); await _db.SaveChangesAsync(); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Floor
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Guid> CreateFloorAsync(FloorDto d, string? user)
    {
        if (await _db.Space_Floors.AnyAsync(x => x.SiteId == d.SiteId && x.FloorCode == d.FloorCode))
            throw new InvalidOperationException("E-SPACE-001");
        var e = new Space_Floor
        {
            Id              = Guid.NewGuid(),
            SiteId          = d.SiteId,
            Level           = d.Level,
            FloorCode       = d.FloorCode,
            FloorName       = d.FloorName,
            Height          = d.Height,
            UnderlayImage   = d.UnderlayImage,
            UnderlayScale   = d.UnderlayScale,
            UnderlayOffsetX = d.UnderlayOffsetX,
            UnderlayOffsetY = d.UnderlayOffsetY,
            OriginX         = d.OriginX,
            OriginY         = d.OriginY,
            Creator         = user,
            CreateDate      = DateTime.Now
        };
        _db.Space_Floors.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpdateFloorAsync(Guid id, FloorDto d, string? user)
    {
        var e = await _db.Space_Floors.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("E-SPACE-001");
        e.Level           = d.Level;
        e.FloorCode       = d.FloorCode;
        e.FloorName       = d.FloorName;
        e.Height          = d.Height;
        e.UnderlayImage   = d.UnderlayImage;
        e.UnderlayScale   = d.UnderlayScale;
        e.UnderlayOffsetX = d.UnderlayOffsetX;
        e.UnderlayOffsetY = d.UnderlayOffsetY;
        e.OriginX         = d.OriginX;
        e.OriginY         = d.OriginY;
        e.Modifier        = user;
        e.ModifyDate      = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<FloorDto>> ListFloorsAsync(Guid siteId) =>
        await _db.Space_Floors.Where(x => x.SiteId == siteId).Select(x => new FloorDto
        {
            Id = x.Id, SiteId = x.SiteId, Level = x.Level, FloorCode = x.FloorCode,
            FloorName = x.FloorName, Height = x.Height, UnderlayImage = x.UnderlayImage,
            UnderlayScale = x.UnderlayScale, UnderlayOffsetX = x.UnderlayOffsetX,
            UnderlayOffsetY = x.UnderlayOffsetY, OriginX = x.OriginX, OriginY = x.OriginY
        }).ToListAsync();

    public async Task DeleteFloorAsync(Guid id)
    {
        if (await _db.Space_Zones.AnyAsync(x => x.FloorId == id) ||
            await _db.Space_Markers.AnyAsync(x => x.FloorId == id))
            throw new InvalidOperationException("E-SPACE-007");
        var e = await _db.Space_Floors.FirstOrDefaultAsync(x => x.Id == id);
        if (e != null) { _db.Space_Floors.Remove(e); await _db.SaveChangesAsync(); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Zone
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Guid> CreateZoneAsync(ZoneDto d, string? user)
    {
        ValidatePolygon(d.Polygon);  // E-SPACE-006
        if (await _db.Space_Zones.AnyAsync(x => x.FloorId == d.FloorId && x.ZoneCode == d.ZoneCode))
            throw new InvalidOperationException("E-SPACE-001");
        var e = new Space_Zone
        {
            Id         = Guid.NewGuid(),
            FloorId    = d.FloorId,
            ZoneCode   = d.ZoneCode,
            ZoneName   = d.ZoneName,
            ZoneType   = d.ZoneType,
            Polygon    = d.Polygon,
            Color      = d.Color,
            Enable     = d.Enable,
            Creator    = user,
            CreateDate = DateTime.Now
        };
        _db.Space_Zones.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpdateZoneAsync(Guid id, ZoneDto d, string? user)
    {
        ValidatePolygon(d.Polygon);
        var e = await _db.Space_Zones.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("E-SPACE-001");
        e.ZoneCode   = d.ZoneCode;
        e.ZoneName   = d.ZoneName;
        e.ZoneType   = d.ZoneType;
        e.Polygon    = d.Polygon;
        e.Color      = d.Color;
        e.Enable     = d.Enable;
        e.Modifier   = user;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<ZoneDto>> ListZonesAsync(Guid floorId) =>
        await _db.Space_Zones.Where(x => x.FloorId == floorId).Select(x => new ZoneDto
        {
            Id = x.Id, FloorId = x.FloorId, ZoneCode = x.ZoneCode, ZoneName = x.ZoneName,
            ZoneType = x.ZoneType, Polygon = x.Polygon, Color = x.Color, Enable = x.Enable
        }).ToListAsync();

    public async Task DeleteZoneAsync(Guid id)
    {
        if (await _db.Space_Aisles.AnyAsync(x => x.ZoneId == id) ||
            await _db.Space_Racks.AnyAsync(x => x.ZoneId == id))
            throw new InvalidOperationException("E-SPACE-007");
        var e = await _db.Space_Zones.FirstOrDefaultAsync(x => x.Id == id);
        if (e != null) { _db.Space_Zones.Remove(e); await _db.SaveChangesAsync(); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Aisle
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Guid> CreateAisleAsync(AisleDto d, string? user)
    {
        if (await _db.Space_Aisles.AnyAsync(x => x.ZoneId == d.ZoneId && x.AisleCode == d.AisleCode))
            throw new InvalidOperationException("E-SPACE-001");
        var e = new Space_Aisle
        {
            Id         = Guid.NewGuid(),
            ZoneId     = d.ZoneId,
            AisleCode  = d.AisleCode,
            Polygon    = d.Polygon,
            Centerline = d.Centerline,
            Creator    = user,
            CreateDate = DateTime.Now
        };
        _db.Space_Aisles.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpdateAisleAsync(Guid id, AisleDto d, string? user)
    {
        var e = await _db.Space_Aisles.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("E-SPACE-001");
        e.AisleCode  = d.AisleCode;
        e.Polygon    = d.Polygon;
        e.Centerline = d.Centerline;
        e.Modifier   = user;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<AisleDto>> ListAislesAsync(Guid zoneId) =>
        await _db.Space_Aisles.Where(x => x.ZoneId == zoneId).Select(x => new AisleDto
        {
            Id = x.Id, ZoneId = x.ZoneId, AisleCode = x.AisleCode,
            Polygon = x.Polygon, Centerline = x.Centerline
        }).ToListAsync();

    public async Task DeleteAisleAsync(Guid id, string? mode = null, Guid? targetAisleId = null, string? user = null)
    {
        var aisle = await _db.Space_Aisles.FirstOrDefaultAsync(x => x.Id == id);
        if (aisle == null) return;

        // I2：mode 白名单（在取 published 之前）——未知 mode 直接拒绝，不静默降级默认删除
        if (mode is not (null or "deactivate" or "rehome"))
            throw new InvalidOperationException("E-SPACE-002: 未知 mode（可用 deactivate|rehome）");

        var racks = await _db.Space_Racks.Where(r => r.AisleId == id).ToListAsync();
        var rackIds = racks.Select(r => r.Id).ToList();
        var published = await _db.Space_Locations
            .Where(l => l.RackId != null && rackIds.Contains(l.RackId.Value) && l.Status == 1)
            .Select(l => l.Id)
            .ToListAsync();

        // 路径B（ch04 §7.2）：改挂目标巷道（null=脱巷道也是 path 变更）→ re-publish → 删。
        // I2：rehome 无论 published 数量都走本分支——「搬走」语义对全草稿同样成立（草稿只改挂不 re-publish）。
        if (mode == "rehome")
        {
            if (targetAisleId != null)
            {
                var target = await _db.Space_Aisles.FirstOrDefaultAsync(a => a.Id == targetAisleId.Value)
                             ?? throw new InvalidOperationException("E-SPACE-407: 目标巷道不存在");
                if (racks.Any(r => r.ZoneId != target.ZoneId))
                    throw new InvalidOperationException("E-SPACE-407: 目标巷道与货架不在同一库区");
            }
            // I1：改挂 + re-publish + 删源 三段提交包一层事务（RepublishAsync 嵌套守卫自动加入，非原子重试黑洞闭合）
            IDbContextTransaction? tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync()
                : null;
            try
            {
                foreach (var r in racks) r.AisleId = targetAisleId;
                await _db.SaveChangesAsync();                    // 先落改挂，BuildItemAsync 才拼得出新 path
                if (published.Count > 0)
                    await _publish.RepublishAsync(published, user);
                _db.Space_Aisles.Remove(aisle);                  // 货架已不挂本巷道，直接删
                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync();
                throw;
            }
            finally
            {
                tx?.Dispose();
            }
            return;
        }

        if (published.Count > 0)
        {
            switch (mode)
            {
                case "deactivate":
                    // 路径A（ch04 §7.2）：逐个走停用同步 RPC。不包事务——停用是同步决策模型，
                    // 部分完成时已停用的保持停用（安全方向），重试幂等收敛。
                    foreach (var locId in published)
                        await _publish.DeactivateAsync(locId, user);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"E-SPACE-402: 该巷道下有 {published.Count} 个已发布库位，不能直接删除（可用 mode=deactivate|rehome）");
            }
        }

        // 默认路径（无已发布 / deactivate 后落到这里）——SetNull 保留货架。
        // 注：deactivate 后库位已 Status=2，其 WMS bin 已带最终态，path 陈旧无消费方，不再 re-publish。
        foreach (var r in racks) r.AisleId = null;
        _db.Space_Aisles.Remove(aisle);
        await _db.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Rack
    // ══════════════════════════════════════════════════════════════════════

    public async Task<Guid> CreateRackAsync(RackDto d, string? user)
    {
        if (d.ZoneId == Guid.Empty)
            throw new InvalidOperationException("E-SPACE-002");  // 货架必须归库区
        if (d.Cols < 1 || d.Levels < 1 || d.DepthCount < 1 || d.CellW <= 0 || d.CellH <= 0 || d.CellD <= 0)
            throw new InvalidOperationException("E-SPACE-002");  // 尺寸不变量
        if (await _db.Space_Racks.AnyAsync(x => x.ZoneId == d.ZoneId && x.RackCode == d.RackCode))
            throw new InvalidOperationException("E-SPACE-001");
        // 冗余回填 FloorId（从 Zone 查）
        var floorId = await _db.Space_Zones
            .Where(z => z.Id == d.ZoneId)
            .Select(z => z.FloorId)
            .FirstOrDefaultAsync();
        var e = new Space_Rack
        {
            Id         = Guid.NewGuid(),
            ZoneId     = d.ZoneId,
            AisleId    = d.AisleId,
            FloorId    = floorId,
            TemplateId = d.TemplateId,
            RackCode   = d.RackCode,
            X          = d.X,
            Y          = d.Y,
            Z          = d.Z,
            RotationZ  = d.RotationZ,
            Cols       = d.Cols,
            Levels     = d.Levels,
            DepthCount = d.DepthCount,
            CellW      = d.CellW,
            CellH      = d.CellH,
            CellD      = d.CellD,
            Enable     = d.Enable,
            Creator    = user,
            CreateDate = DateTime.Now
        };
        _db.Space_Racks.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpdateRackAsync(Guid id, RackDto d, string? user)
    {
        var e = await _db.Space_Racks.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("E-SPACE-002");
        // 乐观并发（真库生效；InMemory 测试跳过冲突）
        _db.Entry(e).Property(x => x.RowVersion).OriginalValue = d.RowVersion;
        e.X          = d.X;
        e.Y          = d.Y;
        e.Z          = d.Z;
        e.RotationZ  = d.RotationZ;
        e.Cols       = d.Cols;
        e.Levels     = d.Levels;
        e.DepthCount = d.DepthCount;
        e.CellW      = d.CellW;
        e.CellH      = d.CellH;
        e.CellD      = d.CellD;
        e.AisleId    = d.AisleId;
        e.Modifier   = user;
        e.ModifyDate = DateTime.Now;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("E-SPACE-009"); }
        // ★ 位姿/尺寸变更后重算其下所有已放置库位的绝对坐标缓存
        await _geo.RecalcRackLocationsAsync(id);
    }

    public async Task<List<RackDto>> ListRacksAsync(Guid zoneId) =>
        await _db.Space_Racks.Where(x => x.ZoneId == zoneId).Select(x => new RackDto
        {
            Id = x.Id, ZoneId = x.ZoneId, AisleId = x.AisleId, TemplateId = x.TemplateId,
            RackCode = x.RackCode, X = x.X, Y = x.Y, Z = x.Z, RotationZ = x.RotationZ,
            Cols = x.Cols, Levels = x.Levels, DepthCount = x.DepthCount,
            CellW = x.CellW, CellH = x.CellH, CellD = x.CellD, Enable = x.Enable,
            RowVersion = x.RowVersion
        }).ToListAsync();

    public async Task DeleteRackAsync(Guid id, string? mode = null, Guid? targetRackId = null, string? user = null)
    {
        var rack = await _db.Space_Racks.FirstOrDefaultAsync(x => x.Id == id);
        if (rack == null) return;

        // I2：mode 白名单（在取 published 之前）——未知 mode 直接拒绝，不静默降级默认删除
        if (mode is not (null or "deactivate" or "rehome"))
            throw new InvalidOperationException("E-SPACE-002: 未知 mode（可用 deactivate|rehome）");

        var published = await _db.Space_Locations
            .Where(l => l.RackId == id && l.Status == 1)
            .Select(l => l.Id)
            .ToListAsync();

        // 路径B（同规格换架）：C1——目标必须同库区（同 zone ⇒ 同 floor 同 site，
        // WarehouseCd 锚绝不漂移；比照巷道 rehome E-407 先例）；目标网格 ≥ 源、且无自有库位（否则格口冲突）。
        // I2：rehome 无论 published 数量都走本分支——「搬走」语义对全草稿同样成立（草稿只改挂不 re-publish）。
        if (mode == "rehome")
        {
            if (targetRackId == null)
                throw new InvalidOperationException("E-SPACE-002: mode=rehome 需要 targetRackId");
            var target = await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == targetRackId.Value)
                         ?? throw new InvalidOperationException("E-SPACE-002: 目标货架不存在");
            if (target.ZoneId != rack.ZoneId)
                throw new InvalidOperationException("E-SPACE-002: 目标货架与源货架不在同一库区，无法改挂");
            if (target.Cols < rack.Cols || target.Levels < rack.Levels || target.DepthCount < rack.DepthCount)
                throw new InvalidOperationException("E-SPACE-002: 目标货架网格小于源货架，无法改挂");
            if (await _db.Space_Locations.AnyAsync(l => l.RackId == target.Id))
                throw new InvalidOperationException("E-SPACE-002: 目标货架已有库位，无法改挂");

            // I1：改挂 + re-publish + 删源 三段提交包一层事务（RepublishAsync 嵌套守卫自动加入，非原子重试黑洞闭合）
            IDbContextTransaction? tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync()
                : null;
            try
            {
                var movable = await _db.Space_Locations.Where(l => l.RackId == id).ToListAsync();
                foreach (var l in movable)
                {
                    l.RackId     = target.Id;
                    l.FloorId    = target.FloorId;
                    l.Modifier   = user;
                    l.ModifyDate = DateTime.Now;
                }
                await _db.SaveChangesAsync();                         // 先落改挂
                await _geo.RecalcRackLocationsAsync(target.Id);       // 几何回填（纯几何不发布，D4）
                if (published.Count > 0)
                    await _publish.RepublishAsync(published, user);   // path.RackCode 变 → re-publish（§7.2 B）
                _db.Space_Racks.Remove(rack);
                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync();
                throw;
            }
            finally
            {
                tx?.Dispose();
            }
            return;
        }

        if (published.Count > 0)
        {
            switch (mode)
            {
                case "deactivate":
                    // 路径A（ch04 §7.2）：逐个停用（同步 RPC 决策模型，不包事务，部分完成重试幂等）
                    // → 落到下方级联删（停用位可删，2026-07-06 拍板②——与巷道 SetNull 落点不同）。
                    foreach (var locId in published)
                        await _publish.DeactivateAsync(locId, user);
                    break;

                default:
                    throw new InvalidOperationException(
                        "E-SPACE-403: 该货架下有已发布库位，请先停用（或 mode=deactivate|rehome）");
            }
        }

        // 无已发布（或 deactivate 后）→ 库位级联删 + 删货架（停用位可删，2026-07-06 拍板；
        // 其码仍占 T_WmsBin 锚，同码再发布会被 REJECTED——锚清理记后续票）。
        var children = await _db.Space_Locations.Where(l => l.RackId == id).ToListAsync();
        if (children.Count > 0) _db.Space_Locations.RemoveRange(children);
        _db.Space_Racks.Remove(rack);
        await _db.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════════════════════
    // 场景聚合 / 待绑定 / 库位列表
    // ══════════════════════════════════════════════════════════════════════

    public async Task<SceneDto> GetSceneAsync(Guid floorId)
    {
        var scene = new SceneDto { FloorId = floorId };

        // 楼层对象（编辑器底图标定 + 局部系原点需要；viewer 也可用，不需亦无害）
        scene.Floor = await _db.Space_Floors
            .Where(f => f.Id == floorId)
            .Select(f => new FloorDto
            {
                Id = f.Id, SiteId = f.SiteId, Level = f.Level, FloorCode = f.FloorCode,
                FloorName = f.FloorName, Height = f.Height, UnderlayImage = f.UnderlayImage,
                UnderlayScale = f.UnderlayScale, UnderlayOffsetX = f.UnderlayOffsetX,
                UnderlayOffsetY = f.UnderlayOffsetY, OriginX = f.OriginX, OriginY = f.OriginY
            })
            .FirstOrDefaultAsync();

        scene.Zones = await _db.Space_Zones
            .Where(z => z.FloorId == floorId)
            .Select(z => new ZoneDto
            {
                Id = z.Id, FloorId = z.FloorId, ZoneCode = z.ZoneCode, ZoneName = z.ZoneName,
                ZoneType = z.ZoneType, Polygon = z.Polygon, Color = z.Color, Enable = z.Enable
            }).ToListAsync();

        var zoneIds = scene.Zones.Select(z => z.Id!.Value).ToList();
        scene.Aisles = await _db.Space_Aisles
            .Where(a => zoneIds.Contains(a.ZoneId))
            .Select(a => new AisleDto
            {
                Id = a.Id, ZoneId = a.ZoneId, AisleCode = a.AisleCode,
                Polygon = a.Polygon, Centerline = a.Centerline
            }).ToListAsync();

        scene.Racks = await _db.Space_Racks
            .Where(r => r.FloorId == floorId)
            .Select(r => new RackDto
            {
                Id = r.Id, ZoneId = r.ZoneId, AisleId = r.AisleId, TemplateId = r.TemplateId,
                RackCode = r.RackCode, X = r.X, Y = r.Y, Z = r.Z, RotationZ = r.RotationZ,
                Cols = r.Cols, Levels = r.Levels, DepthCount = r.DepthCount,
                CellW = r.CellW, CellH = r.CellH, CellD = r.CellD, Enable = r.Enable,
                RowVersion = r.RowVersion   // 编辑器乐观保存需回传
            }).ToListAsync();

        // 仅含 Placed=true 的库位（未放置的走 GetUnplacedAsync）
        scene.Locations = await _db.Space_Locations
            .Where(l => l.FloorId == floorId && l.Placed)
            .Select(l => new SceneLocationDto
            {
                Id           = l.Id,
                RackId       = l.RackId!.Value,
                LocationCode = l.LocationCode,
                Col          = l.Col   ?? 0,
                Level        = l.Level ?? 0,
                Depth        = l.Depth ?? 0,
                AbsX         = l.AbsX  ?? 0,
                AbsY         = l.AbsY  ?? 0,
                AbsZ         = l.AbsZ  ?? 0,
                SizeW        = l.SizeW ?? 0,
                SizeH        = l.SizeH ?? 0,
                SizeD        = l.SizeD ?? 0,
                Status       = l.Status
            }).ToListAsync();

        scene.Markers = await _db.Space_Markers
            .Where(m => m.FloorId == floorId)
            .Select(m => new MarkerDto
            {
                Id = m.Id, FloorId = m.FloorId, X = m.X, Y = m.Y, Z = m.Z,
                MarkerType = m.MarkerType, Text = m.Text, RefRackId = m.RefRackId
            }).ToListAsync();

        return scene;
    }

    public async Task<List<SceneLocationDto>> GetUnplacedAsync(Guid floorId)
    {
        // 采纳态待绑定：Status=1 ∧ Placed=false（FloorId 可为空，按租户全量返回）
        return await _db.Space_Locations
            .Where(l => l.Status == 1 && !l.Placed)
            .Select(l => new SceneLocationDto
            {
                Id = l.Id, LocationCode = l.LocationCode, Status = l.Status
            }).ToListAsync();
    }

    public async Task<List<SceneLocationDto>> ListLocationsAsync(Guid rackId) =>
        await _db.Space_Locations
            .Where(l => l.RackId == rackId)
            .Select(l => new SceneLocationDto
            {
                Id           = l.Id,
                RackId       = l.RackId!.Value,
                LocationCode = l.LocationCode,
                Col          = l.Col   ?? 0,
                Level        = l.Level ?? 0,
                Depth        = l.Depth ?? 0,
                AbsX         = l.AbsX  ?? 0,
                AbsY         = l.AbsY  ?? 0,
                AbsZ         = l.AbsZ  ?? 0,
                SizeW        = l.SizeW ?? 0,
                SizeH        = l.SizeH ?? 0,
                SizeD        = l.SizeD ?? 0,
                Status       = l.Status
            }).ToListAsync();

    // ══════════════════════════════════════════════════════════════════════
    // 内部工具
    // ══════════════════════════════════════════════════════════════════════

    private static void ValidatePolygon(string json)
    {
        var pts = JsonSerializer.Deserialize<List<List<int>>>(json) ?? new();
        if (pts.Count < 3) throw new InvalidOperationException("E-SPACE-006");
    }
}
