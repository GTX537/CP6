using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    private readonly IWmsBinDeactivator _deactivator;
    private readonly ISpaceNotifier _notifier;
    private readonly ISpaceExecutionContextAccessor _execution;
    private readonly ISpaceExecutionContextManager _executionManager;

    public LocationPublishService(
        CP6Context db,
        ITenantContext t,
        ICodeEngineService code,
        ISpaceBridgeHook hook,
        IWmsStockQuery stock,
        IWmsBinDeactivator deactivator,
        ISpaceNotifier notifier,
        ISpaceExecutionContextAccessor execution,
        ISpaceExecutionContextManager executionManager)
    {
        _db = db;
        _t = t;
        _code = code;
        _hook = hook;
        _stock = stock;
        _deactivator = deactivator;
        _notifier = notifier;
        _execution = execution;
        _executionManager = executionManager;
    }

    /// <inheritdoc/>
    public async Task<int> PublishFloorAsync(Guid floorId, Guid? zoneId, string? user)
    {
        // InMemory 安全事务守卫（惯例见 SceneService）：真库开事务，InMemory 降级无事务。
        // 事务范围＝闸门→翻状态→WMS 消费(T_WmsBin 写入)→事件落库，全部同库原子提交，
        // 修复"翻了状态但事件静默丢失"的窗口（同一 CP6Context 实例，hook 内 SaveChanges 同事务）。
        IDbContextTransaction? tx = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync()
            : null;
        IDisposable? executionScope = null;
        try
        {
            // 1. 闸门（ch03 §9.2；zoneId 给定时按库区收窄，H5）
            var pre = await _code.PrecheckAsync(floorId, zoneId);
            if (pre.EmptyCodeCount > 0 || pre.DuplicateGroups.Count > 0 || pre.PrecheckErrors.Count > 0)
                throw new BizException("E-SPACE-307");

            // 2. 取 Status=0 且编码就绪的库位（zoneId 给定时经 Rack.ZoneId 收窄）
            var locQuery = _db.Space_Locations
                .Where(l => l.FloorId == floorId && l.Status == 0 && l.LocationCode != null);
            if (zoneId != null)
            {
                var rackIds = await _db.Space_Racks.Where(r => r.ZoneId == zoneId).Select(r => r.Id).ToListAsync();
                locQuery = locQuery.Where(l => l.RackId != null && rackIds.Contains(l.RackId.Value));
            }
            var locs = await locQuery.ToListAsync();

            if (locs.Count == 0) return 0;

            // 只有真正进入发布流程时才建立一次发布意图；必须早于任何本地状态翻转和 Adapter 调用。
            var publishExecution = BeginPublishExecution();
            var context = publishExecution.Context;
            executionScope = publishExecution.Scope;

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
            var lk = await LoadLookupAsync(locs, default);   // 波5：五表各一次预载，替代循环内逐库位连查
            foreach (var l in locs)
            {
                l.Status = 1;
                l.Version += 1;
                l.Modifier = user;
                l.ModifyDate = DateTime.Now;
                batch.Items.Add(BuildItem(l, "UPSERT", lk));
            }
            await _db.SaveChangesAsync();

            // 5. 发事件（hook 内部吞消费异常→Failed 事件落库，由 Worker 重试；不影响本事务提交）
            await _hook.OnLocationPublishedAsync(batch, context.CorrelationId);

            if (tx != null) await tx.CommitAsync();

            // 6. SignalR プッシュ（★事務 Commit 後：確定済みイベントのみ通知、推送不進事務。
            //    実装は例外を投げない契約 ── 万一落ちても業務は既に確定済み、return を妨げない）
            await _notifier.NotifyLocationPublishedAsync(batchNo, locs.Count, "SUCCESS");
            return locs.Count;
        }
        finally
        {
            try
            {
                if (tx != null) await tx.DisposeAsync();   // 未 Commit 即 Dispose = 回滚
            }
            finally
            {
                executionScope?.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    public async Task DeactivateAsync(Guid locationId, string? user)
    {
        var l = await _db.Space_Locations.FirstOrDefaultAsync(x => x.Id == locationId)
                ?? throw new BizException("E-SPACE-004");
        if (l.Status != 1)
            throw new BizException("E-SPACE-004");

        // 停用意图必须在任何库存数据源或 WMS Adapter 调用前建立。
        var publishExecution = BeginPublishExecution();
        try
        {
            var context = publishExecution.Context;

            // ① 前置校验（用户体验，连 RPC 都不发；ch04 §6.1①；H7 带仓维度防多仓同码误拦）
            // 波5：单库位也走统一预载，前置校验与后续 BuildItem 共用同一 lookup（FloorId/RackId 期间不变）。
            var lk = await LoadLookupAsync(new[] { l }, default);
            var warehouseCd = ResolveWarehouseCd(l, lk);
            if (_stock.DataSourceKind == SpaceDataSourceKind.Unavailable)
                throw new BizException(SpaceDataSourceErrors.Unavailable, 503);

            var qty = await _stock.GetStockQtyAsync(l.LocationCode ?? "", warehouseCd);
            if (qty > 0)
                throw new BizException("E-SPACE-401");

            // ② 同步 RPC：WMS 按实时库存权威判定（TOCTOU 防护；ch04 §6.1② v1.1）
            var newVersion = l.Version + 1;
            var resp = await _deactivator.DeactivateAsync(new WmsDeactivateRequest
            {
                LocationId = l.Id,
                LocationCode = l.LocationCode ?? "",
                WarehouseCd = warehouseCd,
                Version = newVersion,
                User = user
            });

            // ③ 据同步返回决定本地 Status——被拒不前进，无翻转回滚（ch04 §6.3）
            if (!resp.Success)
                throw new BizException("W-SPACE-404");

            l.Status = 2;
            l.Version = newVersion;
            l.Modifier = user;
            l.ModifyDate = DateTime.Now;

            var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
            var batch = new LocationPublishBatch
            {
                BatchNo = $"LPUB-{DateTime.Today:yyyyMMdd}-{seq:D4}",
                TenantId = _t.CurrentTenantId,
                PublishedBy = user
            };
            batch.Items.Add(BuildItem(l, "DEACTIVATE", lk));
            await _db.SaveChangesAsync();

            // ④ 异步事件兜底（对账/审计/漂移纠正，不参与本地 Status 决策；ch04 §6.1④）
            await _hook.OnLocationPublishedAsync(batch, context.CorrelationId);

            // ⑤ SignalR プッシュ（兜底事件 hook 後：本地 Status 已 SaveChanges 確定。
            //    実装は例外を投げない契約 ── 推送失敗絕不坏业务）
            await _notifier.NotifyLocationPublishedAsync(batch.BatchNo, 1, "SUCCESS");
        }
        finally
        {
            publishExecution.Scope?.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<int> RepublishAsync(IReadOnlyCollection<Guid> locationIds, string? user)
    {
        if (locationIds.Count == 0) return 0;

        // 嵌套事务守卫：本方法会被 SceneService 场景保存事务（H4 改挂）或删除放行路径包裹调用；
        // 已有环境事务时直接加入（同连接嵌套 BeginTransaction 会抛），无事务时自开（惯例守卫）。
        var ownsTx = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null;
        IDbContextTransaction? tx = ownsTx ? await _db.Database.BeginTransactionAsync() : null;
        IDisposable? executionScope = null;
        try
        {
            var locs = await _db.Space_Locations
                .Where(l => locationIds.Contains(l.Id) && l.Status == 1 && l.LocationCode != null)
                .ToListAsync();
            if (locs.Count == 0) return 0;

            // 空集合或无命中不制造虚假发布意图；命中后、状态升版前建立上下文。
            var publishExecution = BeginPublishExecution();
            var context = publishExecution.Context;
            executionScope = publishExecution.Scope;

            var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
            var batch = new LocationPublishBatch
            {
                BatchNo = $"LPUB-{DateTime.Today:yyyyMMdd}-{seq:D4}",
                TenantId = _t.CurrentTenantId,
                PublishedBy = user
            };
            var lk = await LoadLookupAsync(locs, default);   // 波5：五表各一次预载，替代循环内逐库位连查
            foreach (var l in locs)
            {
                l.Version += 1;                 // 码冻结不变，只升版刷新 path（§7.2 B）
                l.Modifier = user;
                l.ModifyDate = DateTime.Now;
                batch.Items.Add(BuildItem(l, "UPSERT", lk));
            }
            await _db.SaveChangesAsync();
            await _hook.OnLocationPublishedAsync(batch, context.CorrelationId);

            if (tx != null) await tx.CommitAsync();
            return locs.Count;
        }
        finally
        {
            try
            {
                if (tx != null) await tx.DisposeAsync();
            }
            finally
            {
                executionScope?.Dispose();
            }
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

    /// <summary>
    /// 为一次实际发布意图建立执行身份。首个意图补充根作用域，便于 HTTP 结果审计读取最终标识；
    /// 同一请求中的后续意图使用派生作用域，保持 Correlation/Actor 等身份不变，同时生成独立
    /// PublishAttemptId/JobId，并在调用结束后恢复首个根身份。
    /// </summary>
    private (ISpaceExecutionContext Context, IDisposable? Scope) BeginPublishExecution()
    {
        var current = _execution.RequireCurrent();
        if (current.JobId is null && current.PublishAttemptId is null)
        {
            _executionManager.Enrich(publishAttemptId: Guid.NewGuid());
            return (_execution.RequireCurrent(), null);
        }

        var derived = new SpaceExecutionContext(
            current.CorrelationId,
            current.TraceId,
            current.TenantId,
            current.ActorType,
            current.ActorId,
            current.ActorName,
            current.OrganizationContextId,
            JobId: null,
            RunId: current.RunId,
            PublishAttemptId: null);
        var scope = _executionManager.PushDerived(derived);
        try
        {
            _executionManager.Enrich(publishAttemptId: Guid.NewGuid());
            return (_execution.RequireCurrent(), scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 发布链路径解析预载字典（波5 批量化）。五级挂载（Rack→Aisle→Zone→Floor→Site）+
    /// WarehouseCd 回退（Site.WarehouseCd ?? SiteCode）所需的全部父级实体，按 Id 预索引。
    /// 由 <see cref="LoadLookupAsync"/> 五张表各一次查询填充，供纯内存的 <see cref="BuildItem"/> 消费。
    /// </summary>
    private sealed class PublishLookup
    {
        public Dictionary<Guid, Space_Rack> Racks { get; init; } = new();
        public Dictionary<Guid, Space_Aisle> Aisles { get; init; } = new();
        public Dictionary<Guid, Space_Zone> Zones { get; init; } = new();
        public Dictionary<Guid, Space_Floor> Floors { get; init; } = new();
        public Dictionary<Guid, Space_Site> Sites { get; init; } = new();
    }

    /// <summary>
    /// 发布链路径预载（波5 批量化）：按 locs 的 RackId / FloorId 集合，五张表各**一次**
    /// <c>Where(ids.Contains)</c> 载入成字典——替代旧 BuildItemAsync 事务内逐库位 5 连查 +
    /// ResolveWarehouseCdAsync 2 查（旧实现每库位约 7 次往返、总量随 N 线性放大）。
    /// 依赖链决定加载顺序：Rack →（Aisle/Zone by rack）→ Floor →（Site by floor）。
    /// 楼层集合 = 货架链 zone.FloorId（供 Path.FloorLevel/SiteCode）∪ 库位冗余 l.FloorId
    /// （供 WarehouseCd 回退——两条链共用同一 Floors/Sites 字典，各自 TryGetValue 语义不变）。
    /// 无论 N 多少，查询恒为常数 5 次；全空集合直接跳过对应查询。
    /// </summary>
    private async Task<PublishLookup> LoadLookupAsync(IReadOnlyCollection<Space_Location> locs, CancellationToken ct)
    {
        var rackIds = locs.Where(l => l.RackId != null).Select(l => l.RackId!.Value).Distinct().ToList();
        var racks = rackIds.Count == 0
            ? new List<Space_Rack>()
            : await _db.Space_Racks.Where(r => rackIds.Contains(r.Id)).ToListAsync(ct);

        var aisleIds = racks.Where(r => r.AisleId != null).Select(r => r.AisleId!.Value).Distinct().ToList();
        var aisles = aisleIds.Count == 0
            ? new List<Space_Aisle>()
            : await _db.Space_Aisles.Where(a => aisleIds.Contains(a.Id)).ToListAsync(ct);

        var zoneIds = racks.Select(r => r.ZoneId).Distinct().ToList();
        var zones = zoneIds.Count == 0
            ? new List<Space_Zone>()
            : await _db.Space_Zones.Where(z => zoneIds.Contains(z.Id)).ToListAsync(ct);

        // Path 链走 zone.FloorId；WarehouseCd 链走 l.FloorId（冗余列）——并集一次载全，两链各自查同一字典。
        var floorIds = zones.Select(z => z.FloorId)
            .Concat(locs.Where(l => l.FloorId != null).Select(l => l.FloorId!.Value))
            .Distinct().ToList();
        var floors = floorIds.Count == 0
            ? new List<Space_Floor>()
            : await _db.Space_Floors.Where(f => floorIds.Contains(f.Id)).ToListAsync(ct);

        var siteIds = floors.Select(f => f.SiteId).Distinct().ToList();
        var sites = siteIds.Count == 0
            ? new List<Space_Site>()
            : await _db.Space_Sites.Where(s => siteIds.Contains(s.Id)).ToListAsync(ct);

        return new PublishLookup
        {
            Racks = racks.ToDictionary(r => r.Id),
            Aisles = aisles.ToDictionary(a => a.Id),
            Zones = zones.ToDictionary(z => z.Id),
            Floors = floors.ToDictionary(f => f.Id),
            Sites = sites.ToDictionary(s => s.Id)
        };
    }

    /// <summary>
    /// 纯内存构建单条发布载荷（波5 批量化后的 BuildItemAsync）。逐字段等价旧实现：
    /// PathJson 五级路径、缺挂（rack/aisle/zone/floor/site 任一 null）分支、WarehouseCd 回退全保持，
    /// 唯一区别是父级从 <paramref name="lk"/> 预载字典 TryGetValue 取，不再事务内逐条 FirstOrDefaultAsync。
    /// </summary>
    private static LocationPublishItem BuildItem(Space_Location l, string op, PublishLookup lk)
    {
        var path = new LocationPath
        {
            Col = l.Col ?? 0,
            Level = l.Level ?? 0,
            Depth = l.Depth ?? 0
        };

        if (l.RackId != null && lk.Racks.TryGetValue(l.RackId.Value, out var rack))
        {
            path.RackCode = rack.RackCode;
            if (rack.AisleId != null && lk.Aisles.TryGetValue(rack.AisleId.Value, out var aisle))
                path.AisleCode = aisle.AisleCode;   // aisle 缺失 → AisleCode 保持 null（等价 aisle?.AisleCode）
            if (lk.Zones.TryGetValue(rack.ZoneId, out var zone))
            {
                path.ZoneCode = zone.ZoneCode;
                if (lk.Floors.TryGetValue(zone.FloorId, out var floor))
                {
                    path.FloorLevel = floor.Level;
                    if (lk.Sites.TryGetValue(floor.SiteId, out var site))
                        path.SiteCode = site.SiteCode;   // site 缺失 → SiteCode 保持 null（等价 site?.SiteCode）
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
            WarehouseCd = ResolveWarehouseCd(l, lk),
            Path = path,
            Attrs = attrs
        };
    }

    /// <summary>
    /// SiteCode↔WarehouseCd 映射（ch04 §3.4）：Site.WarehouseCd 显式配置优先，空则默认 = SiteCode。
    /// 走 FloorId → Site 链（比 Rack 链短，且停用未落位库位也可能有 FloorId）；无楼层归属返回 null。
    /// 波5 批量化后从 <paramref name="lk"/> 预载字典取父级（旧实现逐库位 2 查），逐字段行为不变。
    ///
    /// 长度守卫（终审 #1）：WmsBin.WarehouseCd / Space_Site.WarehouseCd 均为 nvarchar(10)，而
    /// Space_Site.SiteCode 是 MaxLength(50)。默认回退 WarehouseCd=SiteCode 时若 SiteCode 超 10 字符，
    /// 消费端真库 SaveChanges 会截断/抛异常 → 毒化共享 CP6Context → 状态已翻/无 bin/无事件的三无孤儿。
    /// 本方法在 BuildItem（发布/re-publish）/ DeactivateAsync（停用）内被调用。发布路径虽在循环里先写了
    /// 内存态 l.Status = 1，但抛异常发生在 _db.SaveChangesAsync() 之前——内存翻转从不落库，且外层
    /// 发布事务（tx）未 Commit 即 Dispose 回滚。因此天然 fail-fast：库位 Status 不持久化、无 bin、无事件、无孤儿。
    /// 显式配置的 Space_Site.WarehouseCd 本身受 MaxLength(10) 列约束护住，超长只可能来自 SiteCode 默认回退。
    /// </summary>
    private static string? ResolveWarehouseCd(Space_Location l, PublishLookup lk)
    {
        if (l.FloorId == null) return null;
        if (!lk.Floors.TryGetValue(l.FloorId.Value, out var floor)) return null;
        if (!lk.Sites.TryGetValue(floor.SiteId, out var site)) return null;
        var warehouseCd = string.IsNullOrEmpty(site.WarehouseCd) ? site.SiteCode : site.WarehouseCd;
        if (warehouseCd.Length > 10)
            throw new BizException("E-SPACE-405");
        return warehouseCd;
    }
}
