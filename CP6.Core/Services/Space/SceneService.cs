using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Space;

/// <summary>
/// 场景差量保存 + D7 绑码服务实现（ch01 §G-1/I-1，v1.1 多租户规则）。
/// 构造注入 CP6Context + LocationGeometryService；租户隔离由全局过滤+盖章自动施加。
/// </summary>
public class SceneService : ISceneService
{
    private readonly CP6Context _db;
    private readonly LocationGeometryService _geo;
    private readonly ILocationPublishService _publish;

    public SceneService(CP6Context db, LocationGeometryService geo, ILocationPublishService publish)
    {
        _db  = db;
        _geo = geo;
        _publish = publish;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<Guid, Guid>> SaveSceneAsync(Guid floorId, SceneSaveDto dto, string? user)
    {
        // InMemory 安全事务守卫：真库开事务，InMemory 降级无事务
        IDbContextTransaction? tx = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync()
            : null;
        try
        {
            var changedRackIds = new HashSet<Guid>();
            var pathChangedRackIds = new HashSet<Guid>();   // H4：层级归属（ZoneId/AisleId）变更的货架

            // ── Zones ──────────────────────────────────────────────
            foreach (var zd in dto.Zones ?? new List<ZoneDto>())
            {
                var existing = zd.Id.HasValue
                    ? await _db.Space_Zones.FirstOrDefaultAsync(z => z.Id == zd.Id.Value)
                    : null;
                if (existing != null)
                {
                    existing.ZoneCode   = zd.ZoneCode;
                    existing.ZoneName   = zd.ZoneName;
                    existing.ZoneType   = zd.ZoneType;
                    existing.Polygon    = zd.Polygon;
                    existing.Color      = zd.Color;
                    existing.Enable     = zd.Enable;
                    existing.Modifier   = user;
                    existing.ModifyDate = DateTime.Now;
                }
                else
                {
                    _db.Space_Zones.Add(new Space_Zone
                    {
                        Id         = zd.Id ?? Guid.NewGuid(),
                        FloorId    = floorId,
                        ZoneCode   = zd.ZoneCode,
                        ZoneName   = zd.ZoneName,
                        ZoneType   = zd.ZoneType,
                        Polygon    = zd.Polygon,
                        Color      = zd.Color,
                        Enable     = zd.Enable,
                        Creator    = user,
                        CreateDate = DateTime.Now
                    });
                }
            }

            // ── Aisles ─────────────────────────────────────────────
            foreach (var ad in dto.Aisles ?? new List<AisleDto>())
            {
                var existing = ad.Id.HasValue
                    ? await _db.Space_Aisles.FirstOrDefaultAsync(a => a.Id == ad.Id.Value)
                    : null;
                if (existing != null)
                {
                    existing.AisleCode  = ad.AisleCode;
                    existing.Polygon    = ad.Polygon;
                    existing.Centerline = ad.Centerline;
                    existing.Modifier   = user;
                    existing.ModifyDate = DateTime.Now;
                }
                else
                {
                    _db.Space_Aisles.Add(new Space_Aisle
                    {
                        Id         = ad.Id ?? Guid.NewGuid(),
                        ZoneId     = ad.ZoneId,
                        AisleCode  = ad.AisleCode,
                        Polygon    = ad.Polygon,
                        Centerline = ad.Centerline,
                        Creator    = user,
                        CreateDate = DateTime.Now
                    });
                }
            }

            // ── Racks ──────────────────────────────────────────────
            foreach (var rd in dto.Racks ?? new List<RackDto>())
            {
                var existing = rd.Id.HasValue
                    ? await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == rd.Id.Value)
                    : null;
                if (existing != null)
                {
                    // 乐观锁：设置 OriginalValue，SaveChanges 会在 WHERE 附加 RowVersion
                    _db.Entry(existing).Property(x => x.RowVersion).OriginalValue = rd.RowVersion;

                    // H4（ch04 §7.2 路径B）：层级归属变更检测——纯几何不发布，但 path 归属变更须 re-publish
                    if (existing.ZoneId != rd.ZoneId || existing.AisleId != rd.AisleId)
                        pathChangedRackIds.Add(existing.Id);

                    // H2 缩格护栏（2026-07-06 拍板：Restrict）：越界库位——已发布阻断，草稿/停用连带删
                    if (rd.Cols < existing.Cols || rd.Levels < existing.Levels || rd.DepthCount < existing.DepthCount)
                    {
                        var outOfBounds = await _db.Space_Locations
                            .Where(l => l.RackId == existing.Id &&
                                        (l.Col > rd.Cols || l.Level > rd.Levels || l.Depth > rd.DepthCount))
                            .ToListAsync();
                        if (outOfBounds.Any(l => l.Status == 1))
                            throw new InvalidOperationException(
                                "E-SPACE-403: 缩格触及已发布库位，请先停用越界库位再缩格");
                        if (outOfBounds.Count > 0)
                            _db.Space_Locations.RemoveRange(outOfBounds);   // 幽灵位清理（H2）
                    }

                    // 位姿/尺寸变更检测
                    if (existing.X != rd.X || existing.Y != rd.Y || existing.Z != rd.Z ||
                        existing.RotationZ != rd.RotationZ || existing.Cols != rd.Cols ||
                        existing.Levels != rd.Levels || existing.DepthCount != rd.DepthCount ||
                        existing.CellW != rd.CellW || existing.CellH != rd.CellH || existing.CellD != rd.CellD)
                    {
                        changedRackIds.Add(existing.Id);
                    }

                    existing.ZoneId     = rd.ZoneId;
                    existing.AisleId    = rd.AisleId;
                    existing.TemplateId = rd.TemplateId;
                    existing.RackCode   = rd.RackCode;
                    existing.X          = rd.X;
                    existing.Y          = rd.Y;
                    existing.Z          = rd.Z;
                    existing.RotationZ  = rd.RotationZ;
                    existing.Cols       = rd.Cols;
                    existing.Levels     = rd.Levels;
                    existing.DepthCount = rd.DepthCount;
                    existing.CellW      = rd.CellW;
                    existing.CellH      = rd.CellH;
                    existing.CellD      = rd.CellD;
                    existing.Enable     = rd.Enable;
                    existing.Modifier   = user;
                    existing.ModifyDate = DateTime.Now;
                }
                else
                {
                    var newId = rd.Id ?? Guid.NewGuid();
                    _db.Space_Racks.Add(new Space_Rack
                    {
                        Id         = newId,
                        ZoneId     = rd.ZoneId,
                        AisleId    = rd.AisleId,
                        FloorId    = floorId,
                        TemplateId = rd.TemplateId,
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
                        Enable     = rd.Enable,
                        Creator    = user,
                        CreateDate = DateTime.Now
                    });
                    changedRackIds.Add(newId);  // 新货架→位置变化，库位需重算
                }
            }

            // ── Markers ────────────────────────────────────────────
            foreach (var md in dto.Markers ?? new List<MarkerDto>())
            {
                var existing = md.Id.HasValue
                    ? await _db.Space_Markers.FirstOrDefaultAsync(m => m.Id == md.Id.Value)
                    : null;
                if (existing != null)
                {
                    existing.X          = md.X;
                    existing.Y          = md.Y;
                    existing.Z          = md.Z;
                    existing.MarkerType = md.MarkerType;
                    existing.Text       = md.Text;
                    existing.RefRackId  = md.RefRackId;
                    existing.Modifier   = user;
                    existing.ModifyDate = DateTime.Now;
                }
                else
                {
                    _db.Space_Markers.Add(new Space_Marker
                    {
                        Id         = md.Id ?? Guid.NewGuid(),
                        FloorId    = floorId,
                        X          = md.X,
                        Y          = md.Y,
                        Z          = md.Z,
                        MarkerType = md.MarkerType,
                        Text       = md.Text,
                        RefRackId  = md.RefRackId,
                        Creator    = user,
                        CreateDate = DateTime.Now
                    });
                }
            }

            // ── Locations ──────────────────────────────────────────
            foreach (var ld in dto.Locations ?? new List<SceneLocationSaveDto>())
            {
                var existing = ld.Id.HasValue
                    ? await _db.Space_Locations.FirstOrDefaultAsync(l => l.Id == ld.Id.Value)
                    : null;
                if (existing != null)
                {
                    existing.RackId     = ld.RackId;
                    existing.Col        = ld.Col;
                    existing.Level      = ld.Level;
                    existing.Depth      = ld.Depth;
                    existing.Placed     = ld.Placed;
                    // H1 状态机护栏：Status/CodeOrigin 不接受场景保存覆盖——
                    // 状态只经 publish/deactivate 通道流转（ch04 §4），来源标签只在生码/采纳时落定
                    existing.Modifier   = user;
                    existing.ModifyDate = DateTime.Now;
                }
                else
                {
                    _db.Space_Locations.Add(new Space_Location
                    {
                        Id         = ld.Id ?? Guid.NewGuid(),
                        RackId     = ld.RackId,
                        FloorId    = floorId,
                        Col        = ld.Col,
                        Level      = ld.Level,
                        Depth      = ld.Depth,
                        Placed     = ld.Placed,
                        Status     = 0,   // H1：编辑器新建恒草稿（发布走 publish、采纳走 adopt/bind-codes）
                        CodeOrigin = 1,
                        Creator    = user,
                        CreateDate = DateTime.Now
                    });
                }
            }

            // ── Deletes ────────────────────────────────────────────
            foreach (var locId in dto.Deletes?.Locations ?? new List<Guid>())
            {
                var e = await _db.Space_Locations.FirstOrDefaultAsync(l => l.Id == locId);
                if (e == null) continue;
                // 已发布不可删（须先停用）；草稿/停用可删（2026-07-06 拍板）。
                // 注意：停用位删除后其码仍被 T_WmsBin 停用行占据 join 锚，同码新库位发布会被
                // 锚碰撞 REJECTED——锚清理机制记后续票，此为拍板时已知代价。
                if (e.Status == 1)
                    throw new InvalidOperationException("E-SPACE-408: 已发布库位不可删除，请先停用");
                _db.Space_Locations.Remove(e);
            }

            foreach (var rackId in dto.Deletes?.Racks ?? new List<Guid>())
            {
                // ch04 §7.1：有已发布库位 → Restrict；全草稿/停用 → 库位连带删（旧 E-003 全拦废止）
                if (await _db.Space_Locations.AnyAsync(l => l.RackId == rackId && l.Status == 1))
                    throw new InvalidOperationException("E-SPACE-403: 该货架下有已发布库位，请先停用");
                var children = await _db.Space_Locations.Where(l => l.RackId == rackId).ToListAsync();
                if (children.Count > 0) _db.Space_Locations.RemoveRange(children);
                var e = await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == rackId);
                if (e != null) _db.Space_Racks.Remove(e);
            }

            foreach (var aisleId in dto.Deletes?.Aisles ?? new List<Guid>())
            {
                // SetNull：保留货架但解除巷道关联
                var racks = await _db.Space_Racks.Where(r => r.AisleId == aisleId).ToListAsync();
                foreach (var r in racks) r.AisleId = null;
                var e = await _db.Space_Aisles.FirstOrDefaultAsync(a => a.Id == aisleId);
                if (e != null) _db.Space_Aisles.Remove(e);
            }

            foreach (var zoneId in dto.Deletes?.Zones ?? new List<Guid>())
            {
                var e = await _db.Space_Zones.FirstOrDefaultAsync(z => z.Id == zoneId);
                if (e != null) _db.Space_Zones.Remove(e);
            }

            foreach (var markerId in dto.Deletes?.Markers ?? new List<Guid>())
            {
                var e = await _db.Space_Markers.FirstOrDefaultAsync(m => m.Id == markerId);
                if (e != null) _db.Space_Markers.Remove(e);
            }

            // ── 保存（乐观并发冲突→E-SPACE-009）─────────────────────
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException("E-SPACE-009");
            }

            // ── 位姿变更货架→重算库位绝对坐标 ─────────────────────────
            foreach (var rid in changedRackIds)
                await _geo.RecalcRackLocationsAsync(rid);

            // ── H4：改挂货架下的已发布库位 re-publish（在本事务内，RepublishAsync 嵌套守卫自动加入）──
            if (pathChangedRackIds.Count > 0)
            {
                var republishIds = await _db.Space_Locations
                    .Where(l => l.RackId != null && pathChangedRackIds.Contains(l.RackId.Value) && l.Status == 1)
                    .Select(l => l.Id)
                    .ToListAsync();
                if (republishIds.Count > 0)
                    await _publish.RepublishAsync(republishIds, user);
            }

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

        return new Dictionary<Guid, Guid>();
    }

    /// <inheritdoc/>
    public async Task BindCodesAsync(Guid rackId, IEnumerable<(Guid locId, int col, int level, int depth)> pairs, string? user)
    {
        var rack = await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == rackId)
                   ?? throw new InvalidOperationException("E-SPACE-002");

        foreach (var (locId, col, level, depth) in pairs)
        {
            var loc = await _db.Space_Locations.FirstOrDefaultAsync(l => l.Id == locId)
                      ?? throw new InvalidOperationException("E-SPACE-004");

            if (loc.Status != 1 || loc.Placed || loc.CodeOrigin != 2)
                throw new InvalidOperationException("E-SPACE-004");

            var (absX, absY, absZ) = LocationGeometryService.ComputeAbs(rack, col, level, depth);

            loc.RackId   = rackId;
            loc.FloorId  = rack.FloorId;
            loc.Col      = col;
            loc.Level    = level;
            loc.Depth    = depth;
            loc.AbsX     = absX;
            loc.AbsY     = absY;
            loc.AbsZ     = absZ;
            loc.SizeW    = rack.CellW;
            loc.SizeH    = rack.CellH;
            loc.SizeD    = rack.CellD;
            loc.Placed   = true;
            // LocationCode / Id / Status / Version 不动
            loc.Modifier   = user;
            loc.ModifyDate = DateTime.Now;
        }

        await _db.SaveChangesAsync();
    }
}
