using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>
/// 库位发布、停用、采纳服务实现（ch04）。
///
/// v1.1 多租户约定：
///   · 构造注入 ITenantContext 仅用于 LocationPublishBatch.TenantId DTO 字段（不被 EF 盖章）。
///   · 查询不写 .Where(TenantId)——全局过滤自动按当前租户隔离。
///   · 创建实体不写 TenantId——SaveChanges 盖章自动补当前租户。
/// </summary>
public class LocationPublishService : ILocationPublishService
{
    private readonly CP6Context _db;
    private readonly ITenantContext _t;
    private readonly ICodeEngineService _code;
    private readonly ISpaceBridgeHook _hook;
    private readonly IWmsStockQuery _stock;

    public LocationPublishService(
        CP6Context db,
        ITenantContext t,
        ICodeEngineService code,
        ISpaceBridgeHook hook,
        IWmsStockQuery stock)
    {
        _db = db;
        _t = t;
        _code = code;
        _hook = hook;
        _stock = stock;
    }

    /// <inheritdoc/>
    public async Task<int> PublishFloorAsync(Guid floorId, Guid? zoneId, string? user)
    {
        // 1. 闸门（ch03 §9.2）
        var pre = await _code.PrecheckAsync(floorId);
        if (pre.EmptyCodeCount > 0 || pre.DuplicateGroups.Count > 0 || pre.PrecheckErrors.Count > 0)
            throw new InvalidOperationException("E-SPACE-307: 楼层存在空码、重码或其他预检错误，无法发布");

        // 2. 取 Status=0 且编码就绪的库位
        var locs = await _db.Space_Locations
            .Where(l => l.FloorId == floorId && l.Status == 0 && l.LocationCode != null)
            .ToListAsync();

        if (locs.Count == 0) return 0;

        // 3. 批号（D-E）
        var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
        var batchNo = $"LPUB-{DateTime.Today:yyyyMMdd}-{seq:D4}";

        // 4. 翻状态 + 升版 + 组载荷
        var batch = new LocationPublishBatch
        {
            BatchNo = batchNo,
            TenantId = _t.CurrentTenantId,  // DTO 字段，不被 EF 盖章，必须显式赋值
            PublishedBy = user
        };
        foreach (var l in locs)
        {
            l.Status = 1;
            l.Version += 1;
            l.Modifier = user;
            l.ModifyDate = DateTime.Now;
            batch.Items.Add(await BuildItemAsync(l, "UPSERT"));
        }
        await _db.SaveChangesAsync();

        // 5. 发事件
        await _hook.OnLocationPublishedAsync(batch, Guid.NewGuid());
        return locs.Count;
    }

    /// <inheritdoc/>
    public async Task DeactivateAsync(Guid locationId, string? user)
    {
        var l = await _db.Space_Locations.FirstOrDefaultAsync(x => x.Id == locationId)
                ?? throw new InvalidOperationException("E-SPACE-004: 库位不存在");
        if (l.Status != 1)
            throw new InvalidOperationException("E-SPACE-004: 库位未处于已发布状态");

        // D6 前置校验：查 WMS 库存
        var qty = await _stock.GetStockQtyAsync(l.LocationCode ?? "");
        if (qty > 0)
            throw new InvalidOperationException("E-SPACE-401: 库位仍有库存，无法停用");

        l.Status = 2;
        l.Version += 1;
        l.Modifier = user;
        l.ModifyDate = DateTime.Now;

        var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
        var batch = new LocationPublishBatch
        {
            BatchNo = $"LPUB-{DateTime.Today:yyyyMMdd}-{seq:D4}",
            TenantId = _t.CurrentTenantId,
            PublishedBy = user
        };
        batch.Items.Add(await BuildItemAsync(l, "DEACTIVATE"));
        await _db.SaveChangesAsync();

        var r = await _hook.OnLocationPublishedAsync(batch, Guid.NewGuid());
        if (!r.Success)
        {
            // WMS 拒绝 → 回滚状态
            l.Status = 1;
            l.Version += 1;
            await _db.SaveChangesAsync();
            throw new InvalidOperationException("W-SPACE-404: WMS 拒绝停用，已回滚");
        }
    }

    /// <inheritdoc/>
    public async Task<(int imported, List<string> skipped)> AdoptAsync(
        IEnumerable<(string code, Dictionary<string, object?>? attrs)> items, string? user)
    {
        var existing = await _db.Space_Locations
            .Where(l => l.LocationCode != null)
            .Select(l => l.LocationCode!)
            .ToListAsync();
        var set = existing.ToHashSet(StringComparer.Ordinal);

        int n = 0;
        var skipped = new List<string>();
        foreach (var (code, attrs) in items)
        {
            if (set.Contains(code))
            {
                skipped.Add(code);
                continue;
            }
            _db.Space_Locations.Add(new Space_Location
            {
                Id = Guid.NewGuid(),
                LocationCode = code,
                CodeOrigin = 2,
                Status = 1,
                Placed = false,
                RackId = null,
                Creator = user,
                CreateDate = DateTime.Now
            });
            set.Add(code);
            n++;
        }
        await _db.SaveChangesAsync();
        // 不发 LocationPublished（码本就来自 WMS）
        return (n, skipped);
    }

    private async Task<LocationPublishItem> BuildItemAsync(Space_Location l, string op)
    {
        var path = new LocationPath
        {
            Col = l.Col ?? 0,
            Level = l.Level ?? 0,
            Depth = l.Depth ?? 0
        };

        if (l.RackId != null)
        {
            var rack = await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == l.RackId);
            if (rack != null)
            {
                path.RackCode = rack.RackCode;
                if (rack.AisleId != null)
                {
                    var aisle = await _db.Space_Aisles.FirstOrDefaultAsync(a => a.Id == rack.AisleId);
                    path.AisleCode = aisle?.AisleCode;
                }
                var zone = await _db.Space_Zones.FirstOrDefaultAsync(z => z.Id == rack.ZoneId);
                if (zone != null)
                {
                    path.ZoneCode = zone.ZoneCode;
                    var floor = await _db.Space_Floors.FirstOrDefaultAsync(f => f.Id == zone.FloorId);
                    if (floor != null)
                    {
                        path.FloorLevel = floor.Level;
                        var site = await _db.Space_Sites.FirstOrDefaultAsync(s => s.Id == floor.SiteId);
                        path.SiteCode = site?.SiteCode;
                    }
                }
            }
        }

        // attrs: 仅 size，★绝不含 AbsX/Y/Z 几何坐标
        var attrs = new Dictionary<string, object?>();
        if (l.SizeW.HasValue) attrs["sizeW"] = l.SizeW;
        if (l.SizeH.HasValue) attrs["sizeH"] = l.SizeH;
        if (l.SizeD.HasValue) attrs["sizeD"] = l.SizeD;

        return new LocationPublishItem
        {
            Op = op,
            LocationId = l.Id,
            LocationCode = l.LocationCode ?? "",
            CodeOrigin = l.CodeOrigin,
            Version = l.Version,
            WarehouseCd = await ResolveWarehouseCdAsync(l),
            Path = path,
            Attrs = attrs
        };
    }

    /// <summary>
    /// SiteCode↔WarehouseCd 映射（ch04 §3.4）：Site.WarehouseCd 显式配置优先，空则默认 = SiteCode。
    /// 走 FloorId → Site 链（比 Rack 链短，且停用未落位库位也可能有 FloorId）；无楼层归属返回 null。
    /// </summary>
    private async Task<string?> ResolveWarehouseCdAsync(Space_Location l)
    {
        if (l.FloorId == null) return null;
        var floor = await _db.Space_Floors.FirstOrDefaultAsync(f => f.Id == l.FloorId);
        if (floor == null) return null;
        var site = await _db.Space_Sites.FirstOrDefaultAsync(s => s.Id == floor.SiteId);
        if (site == null) return null;
        return string.IsNullOrEmpty(site.WarehouseCd) ? site.SiteCode : site.WarehouseCd;
    }
}
