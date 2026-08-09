using CP6.Core.EFDbContext;
using CP6.WebApi.Localization;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CP6.Tests;

/// <summary>
/// LocationPublishService 测试（ch04 D-3/D-4/D-5）。[InMemory 仅测逻辑]
/// </summary>
public class LocationPublishServiceTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static LocationPublishService MakePublishSvc(
        CP6Context db,
        IWmsStockQuery? stock = null,
        ISpaceBridgeHook? hook = null,
        IWmsBinDeactivator? deact = null,
        ISpaceNotifier? notifier = null,
        SpaceExecutionContextAccessor? execution = null)
    {
        var t = new TenantContext();
        var code = new CodeEngineService(db);
        execution ??= NewExecution();
        hook ??= new SpaceBridgeHook(
            db,
            NullLogger<SpaceBridgeHook>.Instance,
            new NoOpWmsLocationConsumer(),
            execution,
            execution);
        stock ??= new FixedStockQuery(0);
        deact ??= new CP6.Core.Services.Wms.WmsBinDeactivator(db);
        notifier ??= new NoOpSpaceNotifier();
        return new LocationPublishService(
            db,
            t,
            code,
            hook,
            stock,
            deact,
            notifier,
            execution,
            execution);
    }

    private static SpaceExecutionContextAccessor NewExecution(Guid? correlationId = null)
    {
        var execution = new SpaceExecutionContextAccessor();
        execution.Push(SpaceExecutionContext.ForUser(
            TenantContext.DefaultTenant,
            "test-user",
            "Test User",
            correlationId ?? Guid.NewGuid(),
            Guid.NewGuid().ToString("N")));
        return execution;
    }

    /// <summary>发布/停用后 SignalR プッシュが呼ばれたか記録する桩（実装契約通り例外を投げない）。</summary>
    private sealed class RecordingSpaceNotifier : ISpaceNotifier
    {
        public int Calls;
        public string? LastBatchNo;
        public int LastCount;
        public string? LastStatus;
        public Task NotifyLocationPublishedAsync(string batchNo, int count, string status)
        {
            Calls++;
            LastBatchNo = batchNo;
            LastCount = count;
            LastStatus = status;
            return Task.CompletedTask;
        }
    }

    // ── D-3: 整层发布 ──────────────────────────────────────────────────────

    private static string ValidSegmentsJson() => JsonSerializer.Serialize(new[]
    {
        new CodeSegmentDef { Key = "zone", Source = "zone-code", Sep = "-" },
        new CodeSegmentDef { Key = "col",  Source = "col",       Sep = "" }
    });

    private static Guid SeedPublishableFloor(CP6Context db)
    {
        var floorId = Guid.NewGuid();
        var site = new Space_Site
        {
            Id = Guid.NewGuid(),
            SiteCode = "S1",
            SiteName = "Site 1"
        };
        var floor = new Space_Floor
        {
            Id = floorId,
            SiteId = site.Id,
            Level = 1,
            FloorCode = "F1",
            FloorName = "Floor 1"
        };
        var zone = new Space_Zone
        {
            Id = Guid.NewGuid(),
            FloorId = floorId,
            ZoneCode = "Z1",
            ZoneName = "Zone 1"
        };
        var rack = new Space_Rack
        {
            Id = Guid.NewGuid(),
            ZoneId = zone.Id,
            FloorId = floorId,
            RackCode = "R1",
            Cols = 1,
            Levels = 1,
            CellW = 1000,
            CellH = 1000,
            CellD = 1000
        };
        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(),
            RuleName = "default",
            ScopeType = 0,
            IsDefault = true,
            Segments = ValidSegmentsJson()
        });
        db.AddRange(site, floor, zone, rack);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(),
            FloorId = floorId,
            RackId = rack.Id,
            Placed = true,
            Status = 0,
            CodeOrigin = 1,
            LocationCode = "Z1-1",
            Col = 1,
            Level = 1,
            Depth = 1
        });
        db.SaveChanges();
        return floorId;
    }

    [Fact]
    public async Task Publish_reuses_execution_correlation_and_persists_attempt_and_job()
    {
        using var db = Db();
        var correlationId = Guid.NewGuid();
        var execution = NewExecution(correlationId);
        var floorId = SeedPublishableFloor(db);
        var service = MakePublishSvc(db, execution: execution);

        await service.PublishFloorAsync(floorId, null, "alice");

        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal(correlationId, evt.CorrelationId);
        Assert.NotNull(evt.PublishAttemptId);
        Assert.NotNull(evt.JobId);
        Assert.Equal(evt.PublishAttemptId, execution.Current!.PublishAttemptId);
        Assert.Equal(evt.JobId, execution.Current.JobId);
    }

    [Fact]
    public async Task Publish_without_matching_locations_does_not_create_attempt()
    {
        using var db = Db();
        var execution = NewExecution();
        var service = MakePublishSvc(db, execution: execution);

        var count = await service.PublishFloorAsync(Guid.NewGuid(), null, "alice");

        Assert.Equal(0, count);
        Assert.Null(execution.Current!.PublishAttemptId);
        Assert.Null(execution.Current.JobId);
        Assert.Empty(db.IntegrationEvents);
    }

    [Fact]
    public async Task Publish_requires_context_before_state_change()
    {
        using var db = Db();
        var floorId = SeedPublishableFloor(db);
        var execution = new SpaceExecutionContextAccessor();
        var service = MakePublishSvc(db, execution: execution);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PublishFloorAsync(floorId, null, "alice"));

        Assert.Equal("SPACE_EXECUTION_CONTEXT_REQUIRED", error.Message);
        Assert.Equal(0, (await db.Space_Locations.SingleAsync()).Status);
        Assert.Empty(db.IntegrationEvents);
    }

    [Fact]
    public async Task Publish_GatePassed_FlipsStatusAndEmitsEvent()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        // Seed hierarchy to satisfy PrecheckAsync + BuildItemAsync
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "S1", SiteName = "S1" };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var rack = new Space_Rack { Id = rackId, ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        // Seed valid code rule (ScopeType=0 tenant default) so PrecheckAsync passes
        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true,
            Segments = ValidSegmentsJson()
        });
        db.Space_Sites.Add(site);
        db.Space_Floors.Add(floor);
        db.Space_Zones.Add(zone);
        db.Space_Racks.Add(rack);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId,
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        var svc = MakePublishSvc(db);
        var n = await svc.PublishFloorAsync(floorId, zoneId: null, user: "u");
        Assert.Equal(1, n);

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status);
        Assert.Equal(1, loc.Version);

        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal("SPACE", evt.SourceModule);
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var firstOp = payload.GetProperty("Items")[0].GetProperty("Op").GetString();
        Assert.Equal("UPSERT", firstOp);
    }

    [Fact]
    public async Task Publish_GatePassed_NotifiesSignalR()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "S1", SiteName = "S1" };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var rack = new Space_Rack { Id = rackId, ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true,
            Segments = ValidSegmentsJson()
        });
        db.Space_Sites.Add(site);
        db.Space_Floors.Add(floor);
        db.Space_Zones.Add(zone);
        db.Space_Racks.Add(rack);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId,
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        var rec = new RecordingSpaceNotifier();
        var svc = MakePublishSvc(db, notifier: rec);
        var n = await svc.PublishFloorAsync(floorId, zoneId: null, user: "u");

        Assert.Equal(1, n);
        Assert.Equal(1, rec.Calls);
        Assert.False(string.IsNullOrEmpty(rec.LastBatchNo));
        Assert.StartsWith("LPUB-", rec.LastBatchNo);
        Assert.Equal(1, rec.LastCount);
        Assert.Equal("SUCCESS", rec.LastStatus);
    }

    [Fact]
    public async Task Publish_EmptyCode_Throws_E307_NoEvent()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        // Zone without a code rule → PrecheckAsync returns PrecheckErrors, triggering E-307
        // Alternatively, add a location with null code → EmptyCodeCount > 0 → E-307
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        db.Space_Zones.Add(zone);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = Guid.NewGuid(),
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = null
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => MakePublishSvc(db).PublishFloorAsync(floorId, null, "u"));
        Assert.Equal("E-SPACE-307", ex.Code);
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Publish_NoLocations_Returns0()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        var n = await MakePublishSvc(db).PublishFloorAsync(floorId, null, "u");
        Assert.Equal(0, n);
    }

    // ── v1.1 §3.4: SiteCode↔WarehouseCd 映射 ─────────────────────────────

    private static (Guid floorId, Guid rackId) SeedHierarchy(CP6Context db, string? siteWarehouseCd)
    {
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "WH1", SiteName = "S1", WarehouseCd = siteWarehouseCd };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var rack = new Space_Rack { Id = rackId, ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true,
            Segments = ValidSegmentsJson()
        });
        db.Space_Sites.Add(site);
        db.Space_Floors.Add(floor);
        db.Space_Zones.Add(zone);
        db.Space_Racks.Add(rack);
        return (floorId, rackId);
    }

    private static IReadOnlyList<Guid> SeedPublishedLocations(
        CP6Context db,
        int count)
    {
        var ids = Enumerable.Range(1, count)
            .Select(_ => Guid.NewGuid())
            .ToList();
        db.Space_Locations.AddRange(ids.Select((id, index) =>
            new Space_Location
            {
                Id = id,
                FloorId = Guid.NewGuid(),
                RackId = Guid.NewGuid(),
                Placed = true,
                Status = 1,
                CodeOrigin = 1,
                LocationCode = $"CTX-OUTCOME-{index + 1:D2}",
                Version = 1
            }));
        db.SaveChanges();
        return ids;
    }

    [Fact]
    public async Task Publish_SiteWithoutMapping_ItemWarehouseCd_DefaultsToSiteCode()
    {
        using var db = Db();
        var (floorId, rackId) = SeedHierarchy(db, siteWarehouseCd: null);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId,
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).PublishFloorAsync(floorId, null, "u");

        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        // 默认规则：WarehouseCd = SiteCode（ch04 §3.4）
        Assert.Equal("WH1", payload.GetProperty("Items")[0].GetProperty("WarehouseCd").GetString());
    }

    [Fact]
    public async Task Publish_SiteWithMapping_ItemWarehouseCd_UsesMappedValue()
    {
        using var db = Db();
        var (floorId, rackId) = SeedHierarchy(db, siteWarehouseCd: "W9");
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId,
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).PublishFloorAsync(floorId, null, "u");

        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        Assert.Equal("W9", payload.GetProperty("Items")[0].GetProperty("WarehouseCd").GetString());
    }

    [Fact]
    public async Task Publish_SiteCodeOver10Chars_NoMapping_Throws_E405_NoOrphan()
    {
        // 终审 #1：SiteCode(11 字符) 默认回退 WarehouseCd=SiteCode 超 nvarchar(10) 列约束。
        // 长度守卫在 SaveChanges 前 fail-fast → 状态不持久化、无事件、无孤儿。
        using var db = Db();
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "SITE0123456", SiteName = "S1", WarehouseCd = null }; // 11 chars
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var rack = new Space_Rack { Id = rackId, ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_CodeRules.Add(new Space_CodeRule { Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true, Segments = ValidSegmentsJson() });
        db.AddRange(site, floor, zone, rack);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = floorId, RackId = rackId,
            Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => MakePublishSvc(db).PublishFloorAsync(floorId, null, "u"));
        Assert.Equal("E-SPACE-405", ex.Code);

        // AsNoTracking 读 InMemory 存储快照（SaveChanges 从未运行）→ Status 仍 0
        var loc = await db.Space_Locations.AsNoTracking().SingleAsync();
        Assert.Equal(0, loc.Status);
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Publish_MixedMounting_FullFiveLevelAndFloorOnly_PathAndWarehouseCd_Equivalent()
    {
        // 波5 批量化行为等价护栏：同层 2 库位一次发布——
        // ① 满五级挂载（Site→Floor→Zone→Aisle→Rack，AisleCode 非空，这是既有测试从未覆盖的巷道支路）
        // ② 只挂楼层（RackId=null，五级路径全缺省，WarehouseCd 仍走 l.FloorId→Site 回退）
        // 断言 PathJson 逐字段 + WarehouseCd 与旧逐条查询实现一致（LoadLookupAsync+BuildItem 纯内存构建后不许漂移）。
        using var db = Db();
        var floorId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "WH1", SiteName = "S1", WarehouseCd = null };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 7, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var aisle = new Space_Aisle { Id = Guid.NewGuid(), ZoneId = zone.Id, AisleCode = "AI1" };
        var rack = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zone.Id, AisleId = aisle.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_CodeRules.Add(new Space_CodeRule { Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true, Segments = ValidSegmentsJson() });
        db.AddRange(site, floor, zone, aisle, rack);

        var rackedId = Guid.NewGuid();
        var floorOnlyId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location { Id = rackedId, FloorId = floorId, RackId = rack.Id, Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "FULL-01", Col = 2, Level = 3, Depth = 4, SizeW = 100, SizeH = 200, SizeD = 300 });
        db.Space_Locations.Add(new Space_Location { Id = floorOnlyId, FloorId = floorId, RackId = null, Placed = false, Status = 0, CodeOrigin = 2, LocationCode = "FLOOR-01", Col = 5, Level = 6, Depth = 7 });
        await db.SaveChangesAsync();

        var n = await MakePublishSvc(db).PublishFloorAsync(floorId, null, "u");
        Assert.Equal(2, n);

        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var items = payload.GetProperty("Items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        // ① 满五级：路径每一级 + 巷道 + 坐标 + WarehouseCd 回退全对齐
        var full = items.Single(i => i.GetProperty("LocationId").GetGuid() == rackedId);
        Assert.Equal("WH1", full.GetProperty("WarehouseCd").GetString());
        var fp = full.GetProperty("Path");
        Assert.Equal("WH1", fp.GetProperty("SiteCode").GetString());
        Assert.Equal(7, fp.GetProperty("FloorLevel").GetInt32());
        Assert.Equal("Z1", fp.GetProperty("ZoneCode").GetString());
        Assert.Equal("AI1", fp.GetProperty("AisleCode").GetString());
        Assert.Equal("R1", fp.GetProperty("RackCode").GetString());
        Assert.Equal(2, fp.GetProperty("Col").GetInt32());
        Assert.Equal(3, fp.GetProperty("Level").GetInt32());
        Assert.Equal(4, fp.GetProperty("Depth").GetInt32());

        // ② 只挂楼层：RackId=null → 五级路径全缺省（SiteCode/ZoneCode/AisleCode/RackCode 为 null，FloorLevel=0）
        //    但 WarehouseCd 走 l.FloorId→Site 独立链，仍回退到 SiteCode "WH1"
        var floorOnly = items.Single(i => i.GetProperty("LocationId").GetGuid() == floorOnlyId);
        Assert.Equal("WH1", floorOnly.GetProperty("WarehouseCd").GetString());
        var op = floorOnly.GetProperty("Path");
        Assert.Equal(JsonValueKind.Null, op.GetProperty("SiteCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, op.GetProperty("ZoneCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, op.GetProperty("AisleCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, op.GetProperty("RackCode").ValueKind);
        Assert.Equal(0, op.GetProperty("FloorLevel").GetInt32());
        Assert.Equal(5, op.GetProperty("Col").GetInt32());
        Assert.Equal(6, op.GetProperty("Level").GetInt32());
        Assert.Equal(7, op.GetProperty("Depth").GetInt32());
    }

    [Fact]
    public async Task Publish_WithZoneId_OnlyPublishesThatZone_GateZoneScoped()
    {
        using var db = Db();
        var floorId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "WH1", SiteName = "S1" };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zoneA = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "ZA", ZoneName = "A" };
        var zoneB = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "ZB", ZoneName = "B" };
        var rackA = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zoneA.Id, FloorId = floorId, RackCode = "RA", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        var rackB = new Space_Rack { Id = Guid.NewGuid(), ZoneId = zoneB.Id, FloorId = floorId, RackCode = "RB", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_CodeRules.Add(new Space_CodeRule { Id = Guid.NewGuid(), RuleName = "default", ScopeType = 0, IsDefault = true, Segments = ValidSegmentsJson() });
        db.AddRange(site, floor, zoneA, zoneB, rackA, rackB);
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rackA.Id, Placed = true, Status = 0, CodeOrigin = 1, LocationCode = "ZA-01", Col = 1, Level = 1, Depth = 1 });
        // Zone B 留一个空码草稿——整层闸门会拦（E-307），库区闸门必须放行 Zone A
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), FloorId = floorId, RackId = rackB.Id, Placed = true, Status = 0, CodeOrigin = 1, LocationCode = null, Col = 1, Level = 1, Depth = 1 });
        await db.SaveChangesAsync();

        var n = await MakePublishSvc(db).PublishFloorAsync(floorId, zoneA.Id, "u");

        Assert.Equal(1, n);
        var a = await db.Space_Locations.SingleAsync(l => l.RackId == rackA.Id);
        var b = await db.Space_Locations.SingleAsync(l => l.RackId == rackB.Id);
        Assert.Equal(1, a.Status);
        Assert.Equal(0, b.Status);   // Zone B 未被波及
    }

    // ── D-4: 停用 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_StockZero_Success_EmitsDeactivateEvent()
    {
        using var db = Db();
        var execution = NewExecution();
        var floorId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var site = new Space_Site { Id = Guid.NewGuid(), SiteCode = "S1", SiteName = "S1" };
        var floor = new Space_Floor { Id = floorId, SiteId = site.Id, Level = 1, FloorCode = "F1", FloorName = "F1" };
        var zone = new Space_Zone { Id = Guid.NewGuid(), FloorId = floorId, ZoneCode = "Z1", ZoneName = "Z1" };
        var rack = new Space_Rack { Id = rackId, ZoneId = zone.Id, FloorId = floorId, RackCode = "R1", Cols = 1, Levels = 1, CellW = 1000, CellH = 1000, CellD = 1000 };
        db.Space_Sites.Add(site);
        db.Space_Floors.Add(floor);
        db.Space_Zones.Add(zone);
        db.Space_Racks.Add(rack);
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = rackId,
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db, execution: execution).DeactivateAsync(locId, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(2, loc.Status);
        Assert.Equal(2, loc.Version);

        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(evt.PayloadJson);
        var firstOp = payload.GetProperty("Items")[0].GetProperty("Op").GetString();
        Assert.Equal("DEACTIVATE", firstOp);
        Assert.Equal(execution.Current!.CorrelationId, evt.CorrelationId);
        Assert.Equal(execution.Current.PublishAttemptId, evt.PublishAttemptId);
        Assert.Equal(execution.Current.JobId, evt.JobId);
        Assert.NotNull(evt.PublishAttemptId);
        Assert.NotNull(evt.JobId);
    }

    [Fact]
    public async Task Deactivate_establishes_attempt_before_stock_and_deactivator_calls()
    {
        using var db = Db();
        var locationId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locationId,
            FloorId = Guid.NewGuid(),
            RackId = Guid.NewGuid(),
            Placed = true,
            Status = 1,
            CodeOrigin = 1,
            LocationCode = "CTX-01",
            Version = 1
        });
        await db.SaveChangesAsync();
        var execution = NewExecution();
        var stock = new ContextCheckingStockQuery(execution);
        var deactivator = new ContextCheckingDeactivator(execution);
        var service = MakePublishSvc(
            db,
            stock: stock,
            deact: deactivator,
            execution: execution);

        await service.DeactivateAsync(locationId, "alice");

        Assert.NotNull(execution.Current!.PublishAttemptId);
        Assert.Equal(execution.Current.PublishAttemptId, stock.SeenPublishAttemptId);
        Assert.Equal(execution.Current.PublishAttemptId, deactivator.SeenPublishAttemptId);
    }

    [Fact]
    public async Task Sequential_deactivations_use_distinct_child_attempts_and_restore_first_root_identity()
    {
        using var db = Db();
        var firstLocationId = Guid.NewGuid();
        var secondLocationId = Guid.NewGuid();
        db.Space_Locations.AddRange(
            new Space_Location
            {
                Id = firstLocationId,
                FloorId = Guid.NewGuid(),
                RackId = Guid.NewGuid(),
                Placed = true,
                Status = 1,
                CodeOrigin = 1,
                LocationCode = "CTX-BATCH-01",
                Version = 1
            },
            new Space_Location
            {
                Id = secondLocationId,
                FloorId = Guid.NewGuid(),
                RackId = Guid.NewGuid(),
                Placed = true,
                Status = 1,
                CodeOrigin = 1,
                LocationCode = "CTX-BATCH-02",
                Version = 1
            });
        await db.SaveChangesAsync();
        var execution = NewExecution();
        var stock = new ContextCheckingStockQuery(execution);
        var deactivator = new ContextCheckingDeactivator(execution);
        var service = MakePublishSvc(
            db,
            stock: stock,
            deact: deactivator,
            execution: execution);

        await service.DeactivateAsync(firstLocationId, "alice");
        var firstRootAttempt = execution.Current!.PublishAttemptId;
        var firstRootJob = execution.Current.JobId;

        await service.DeactivateAsync(secondLocationId, "alice");

        var events = await db.IntegrationEvents.ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, evt =>
        {
            Assert.Equal(execution.Current.CorrelationId, evt.CorrelationId);
            Assert.NotNull(evt.PublishAttemptId);
            Assert.NotNull(evt.JobId);
        });
        Assert.Equal(2, events.Select(evt => evt.PublishAttemptId).Distinct().Count());
        Assert.Equal(2, events.Select(evt => evt.JobId).Distinct().Count());
        Assert.Equal(2, stock.SeenPublishAttemptIds.Count);
        Assert.Equal(2, deactivator.SeenPublishAttemptIds.Count);
        Assert.Equal(stock.SeenPublishAttemptIds, deactivator.SeenPublishAttemptIds);
        Assert.True(events
            .Select(evt => evt.PublishAttemptId)
            .ToHashSet()
            .SetEquals(stock.SeenPublishAttemptIds));
        Assert.Equal(firstRootAttempt, execution.Current.PublishAttemptId);
        Assert.Equal(firstRootJob, execution.Current.JobId);
    }

    [Fact]
    public async Task Second_deactivation_failure_before_hook_becomes_latest_outcome_without_job()
    {
        using var db = Db();
        var locationIds = SeedPublishedLocations(db, 2);
        var execution = NewExecution();
        var stock = new ContextCheckingStockQuery(execution);
        var deactivator = new RejectSecondDeactivator(execution);
        var service = MakePublishSvc(
            db,
            stock: stock,
            deact: deactivator,
            execution: execution);

        await service.DeactivateAsync(locationIds[0], "alice");
        var firstAttempt = execution.Current!.PublishAttemptId;
        var firstJob = execution.Current.JobId;

        var error = await Assert.ThrowsAsync<BizException>(
            () => service.DeactivateAsync(locationIds[1], "alice"));

        Assert.Equal("W-SPACE-404", error.Code);
        Assert.Equal(firstAttempt, execution.Current.PublishAttemptId);
        Assert.Equal(firstJob, execution.Current.JobId);
        var outcome = execution.RequireOutcomeCurrent();
        Assert.Equal(
            deactivator.SeenPublishAttemptIds[1],
            outcome.PublishAttemptId);
        Assert.NotEqual(firstAttempt, outcome.PublishAttemptId);
        Assert.Null(outcome.JobId);
        Assert.Single(await db.IntegrationEvents.ToListAsync());
    }

    [Fact]
    public async Task Failed_final_audit_after_second_hook_uses_second_attempt_and_job()
    {
        var tenant = new TenantContext
        {
            CurrentTenantId = TenantContext.DefaultTenant
        };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new CP6Context(options, tenant);
        var locationIds = SeedPublishedLocations(db, 2);
        var execution = NewExecution();
        var notifier = new ThrowOnSecondNotifier();
        var service = MakePublishSvc(
            db,
            notifier: notifier,
            execution: execution);

        await service.DeactivateAsync(locationIds[0], "alice");
        var firstAttempt = execution.Current!.PublishAttemptId;
        var firstJob = execution.Current.JobId;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeactivateAsync(locationIds[1], "alice"));

        Assert.Equal(firstAttempt, execution.Current.PublishAttemptId);
        Assert.Equal(firstJob, execution.Current.JobId);
        var outcome = execution.RequireOutcomeCurrent();
        Assert.NotNull(outcome.PublishAttemptId);
        Assert.NotNull(outcome.JobId);
        Assert.NotEqual(firstAttempt, outcome.PublishAttemptId);
        Assert.NotEqual(firstJob, outcome.JobId);
        var events = await db.IntegrationEvents.ToListAsync();
        var secondEvent = Assert.Single(
            events,
            evt => evt.PublishAttemptId == outcome.PublishAttemptId);
        Assert.Equal(outcome.JobId, secondEvent.JobId);

        var writer = new SpaceAuditWriter(
            new SharedAuditFactory(options, tenant),
            execution,
            NullLogger<SpaceAuditWriter>.Instance);
        Assert.True(await writer.TryAppendAsync(new SpaceAuditEventInput(
            Action: "space.location.deactivate",
            ResourceType: "Location",
            ResourceId: locationIds[1].ToString(),
            Outcome: SpaceAuditOutcome.Failed,
            ReasonCode: "SPACE_ACTION_FAILED")));

        var audit = await db.SpaceAuditEvents.SingleAsync();
        Assert.Equal(outcome.PublishAttemptId, audit.PublishAttemptId);
        Assert.Equal(outcome.JobId, audit.JobId);
    }

    [Fact]
    public async Task Deactivate_missing_context_fails_before_stock_query()
    {
        using var db = Db();
        var locationId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locationId,
            FloorId = Guid.NewGuid(),
            RackId = Guid.NewGuid(),
            Placed = true,
            Status = 1,
            CodeOrigin = 1,
            LocationCode = "CTX-02",
            Version = 1
        });
        await db.SaveChangesAsync();
        var execution = new SpaceExecutionContextAccessor();
        var stock = new CountingStockQuery();
        var service = MakePublishSvc(
            db,
            stock: stock,
            execution: execution);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeactivateAsync(locationId, "alice"));

        Assert.Equal("SPACE_EXECUTION_CONTEXT_REQUIRED", error.Message);
        Assert.Equal(0, stock.Calls);
        Assert.Equal(1, (await db.Space_Locations.SingleAsync()).Status);
        Assert.Empty(db.IntegrationEvents);
    }

    [Fact]
    public async Task Deactivate_StockPositive_Throws_E401()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = Guid.NewGuid(), RackId = Guid.NewGuid(),
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "X-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        var stockStub = new FixedStockQuery(5);
        var ex = await Assert.ThrowsAsync<BizException>(
            () => MakePublishSvc(db, stock: stockStub).DeactivateAsync(locId, "u"));
        Assert.Equal("E-SPACE-401", ex.Code);
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Deactivate_UnavailableStockSource_FailsClosed()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId,
            FloorId = Guid.NewGuid(),
            RackId = Guid.NewGuid(),
            Placed = true,
            Status = 1,
            CodeOrigin = 1,
            LocationCode = "X-02-01-01",
            Col = 1,
            Level = 1,
            Depth = 1,
            Version = 1,
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BizException>(
            () => MakePublishSvc(db, stock: new StubWmsStockQuery())
                .DeactivateAsync(locId, "u"));

        Assert.Equal(SpaceDataSourceErrors.Unavailable, error.Code);
        Assert.Equal(503, error.HttpStatus);
        Assert.Equal(1, (await db.Space_Locations.SingleAsync()).Status);
        Assert.Empty(db.IntegrationEvents);
    }

    [Fact]
    public async Task Deactivate_NotPublished_Throws_E004()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = Guid.NewGuid(), Status = 0, LocationCode = "Y-01-01-01"
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => MakePublishSvc(db).DeactivateAsync(locId, "u"));
        Assert.Equal("E-SPACE-004", ex.Code);
    }

    // ── §7.2 路径B: re-publish ────────────────────────────────────────────

    [Fact]
    public async Task Republish_PublishedLocation_BumpsVersion_EmitsUpsert()
    {
        using var db = Db();
        var execution = NewExecution();
        var (floorId, rackId) = SeedHierarchy(db, siteWarehouseCd: null);
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = rackId,
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 3
        });
        await db.SaveChangesAsync();

        var n = await MakePublishSvc(db, execution: execution)
            .RepublishAsync(new[] { locId }, "u");

        Assert.Equal(1, n);
        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status);                    // 状态不变（不是重新发布生命周期）
        Assert.Equal(4, loc.Version);                   // 只升版
        Assert.Equal("A-01-01-01", loc.LocationCode);   // 码冻结不变（§7.2 B 精髓）
        var evt = await db.IntegrationEvents.SingleAsync();
        var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(evt.PayloadJson);
        Assert.Equal("UPSERT", payload.GetProperty("Items")[0].GetProperty("Op").GetString());
        Assert.Equal(4, payload.GetProperty("Items")[0].GetProperty("Version").GetInt64());
        Assert.Equal(execution.Current!.CorrelationId, evt.CorrelationId);
        Assert.Equal(execution.Current.PublishAttemptId, evt.PublishAttemptId);
        Assert.Equal(execution.Current.JobId, evt.JobId);
        Assert.NotNull(evt.PublishAttemptId);
        Assert.NotNull(evt.JobId);
    }

    [Fact]
    public async Task Republish_IgnoresDraftAndDeactivated()
    {
        using var db = Db();
        var draftId = Guid.NewGuid();
        var deactId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = draftId, FloorId = Guid.NewGuid(), Status = 0, LocationCode = "D-01", Version = 0
        });
        db.Space_Locations.Add(new Space_Location
        {
            Id = deactId, FloorId = Guid.NewGuid(), Status = 2, LocationCode = "X-01", Version = 2
        });
        await db.SaveChangesAsync();

        var execution = NewExecution();
        var n = await MakePublishSvc(db, execution: execution)
            .RepublishAsync(new[] { draftId, deactId }, "u");

        Assert.Equal(0, n);
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());   // 非发布态不产生事件
        Assert.Equal(0, (await db.Space_Locations.FirstAsync(l => l.Id == draftId)).Version);
        Assert.Null(execution.Current!.PublishAttemptId);
        Assert.Null(execution.Current.JobId);
    }

    [Fact]
    public async Task Republish_EmptyInput_Returns0_NoEvent()
    {
        using var db = Db();
        var execution = NewExecution();
        var n = await MakePublishSvc(db, execution: execution)
            .RepublishAsync(Array.Empty<Guid>(), "u");
        Assert.Equal(0, n);
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
        Assert.Null(execution.Current!.PublishAttemptId);
        Assert.Null(execution.Current.JobId);
    }

    // ── D-5: 采纳导入 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Adopt_NewCode_Creates_Status1_CodeOrigin2_PlacedFalse()
    {
        using var db = Db();
        var svc = MakePublishSvc(db);
        var items = new List<(string, Dictionary<string, object?>?)>
        {
            ("EXT-001", null),
            ("EXT-002", null)
        };
        var (imported, skipped) = await svc.AdoptAsync(items, "u");
        Assert.Equal(2, imported);
        Assert.Empty(skipped);
        var locs = await db.Space_Locations.ToListAsync();
        Assert.Equal(2, locs.Count);
        Assert.All(locs, l =>
        {
            Assert.Equal(1, l.Status);
            Assert.Equal(2, l.CodeOrigin);
            Assert.False(l.Placed);
            Assert.Null(l.RackId);
        });
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Adopt_DuplicateCode_Skipped()
    {
        using var db = Db();
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), Status = 1, LocationCode = "EXT-001", CodeOrigin = 2
        });
        await db.SaveChangesAsync();

        var svc = MakePublishSvc(db);
        var items = new List<(string, Dictionary<string, object?>?)>
        {
            ("EXT-001", null),
            ("EXT-003", null)
        };
        var (imported, skipped) = await svc.AdoptAsync(items, "u");
        Assert.Equal(1, imported);
        Assert.Single(skipped);
        Assert.Contains("EXT-001", skipped);
    }

    private sealed class FixedStockQuery : IWmsStockQuery
    {
        private readonly decimal _qty;
        public FixedStockQuery(int qty) => _qty = qty;
        public SpaceDataSourceKind DataSourceKind => SpaceDataSourceKind.Real;
        public string DataSourceId => "TEST_WMS";
        public Task<decimal> GetStockQtyAsync(string locationCode, string? warehouseCd = null, CancellationToken ct = default) => Task.FromResult(_qty);
        public Task<IReadOnlyList<CP6.Core.Services.Integration.WmsStockDto>> GetStockByLocationsAsync(
            IReadOnlyCollection<string> locationCodes, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CP6.Core.Services.Integration.WmsStockDto>>(Array.Empty<CP6.Core.Services.Integration.WmsStockDto>());
        public Task<IReadOnlyList<CP6.Core.Services.Integration.WmsLocationHit>> FindLocationsAsync(
            CP6.Core.Services.Integration.StockLocateQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CP6.Core.Services.Integration.WmsLocationHit>>(Array.Empty<CP6.Core.Services.Integration.WmsLocationHit>());
    }

    private sealed class ContextCheckingStockQuery : IWmsStockQuery
    {
        private readonly ISpaceExecutionContextAccessor _execution;

        public ContextCheckingStockQuery(ISpaceExecutionContextAccessor execution)
        {
            _execution = execution;
        }

        public List<Guid?> SeenPublishAttemptIds { get; } = new();
        public Guid? SeenPublishAttemptId => SeenPublishAttemptIds.LastOrDefault();
        public SpaceDataSourceKind DataSourceKind => SpaceDataSourceKind.Real;
        public string DataSourceId => "TEST_WMS";

        public Task<decimal> GetStockQtyAsync(
            string locationCode,
            string? warehouseCd = null,
            CancellationToken ct = default)
        {
            SeenPublishAttemptIds.Add(
                _execution.RequireCurrent().PublishAttemptId);
            return Task.FromResult(0m);
        }

        public Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
            IReadOnlyCollection<string> locationCodes,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WmsStockDto>>(Array.Empty<WmsStockDto>());

        public Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
            StockLocateQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WmsLocationHit>>(Array.Empty<WmsLocationHit>());
    }

    private sealed class CountingStockQuery : IWmsStockQuery
    {
        public int Calls { get; private set; }
        public SpaceDataSourceKind DataSourceKind => SpaceDataSourceKind.Real;
        public string DataSourceId => "TEST_WMS";

        public Task<decimal> GetStockQtyAsync(
            string locationCode,
            string? warehouseCd = null,
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(0m);
        }

        public Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
            IReadOnlyCollection<string> locationCodes,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WmsStockDto>>(Array.Empty<WmsStockDto>());

        public Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
            StockLocateQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WmsLocationHit>>(Array.Empty<WmsLocationHit>());
    }

    private sealed class ContextCheckingDeactivator : IWmsBinDeactivator
    {
        private readonly ISpaceExecutionContextAccessor _execution;

        public ContextCheckingDeactivator(ISpaceExecutionContextAccessor execution)
        {
            _execution = execution;
        }

        public List<Guid?> SeenPublishAttemptIds { get; } = new();
        public Guid? SeenPublishAttemptId => SeenPublishAttemptIds.LastOrDefault();

        public Task<WmsDeactivateResult> DeactivateAsync(
            WmsDeactivateRequest req,
            CancellationToken ct = default)
        {
            SeenPublishAttemptIds.Add(
                _execution.RequireCurrent().PublishAttemptId);
            return Task.FromResult(new WmsDeactivateResult { Success = true });
        }
    }

    private sealed class RejectSecondDeactivator : IWmsBinDeactivator
    {
        private readonly ISpaceExecutionContextAccessor _execution;

        public RejectSecondDeactivator(
            ISpaceExecutionContextAccessor execution)
        {
            _execution = execution;
        }

        public List<Guid?> SeenPublishAttemptIds { get; } = new();

        public Task<WmsDeactivateResult> DeactivateAsync(
            WmsDeactivateRequest req,
            CancellationToken ct = default)
        {
            SeenPublishAttemptIds.Add(
                _execution.RequireCurrent().PublishAttemptId);
            return Task.FromResult(new WmsDeactivateResult
            {
                Success = SeenPublishAttemptIds.Count == 1
            });
        }
    }

    private sealed class ThrowOnSecondNotifier : ISpaceNotifier
    {
        private int _calls;

        public Task NotifyLocationPublishedAsync(
            string batchNo,
            int count,
            string status)
        {
            if (Interlocked.Increment(ref _calls) == 2)
                throw new InvalidOperationException(
                    "expected notifier failure");

            return Task.CompletedTask;
        }
    }

    private sealed class SharedAuditFactory : ISpaceAuditDbContextFactory
    {
        private readonly DbContextOptions<CP6Context> _options;
        private readonly ITenantContext _tenant;

        public SharedAuditFactory(
            DbContextOptions<CP6Context> options,
            ITenantContext tenant)
        {
            _options = options;
            _tenant = tenant;
        }

        public CP6Context CreateDbContext() => new(_options, _tenant);
    }

    private sealed class RejectingDeactivator : IWmsBinDeactivator
    {
        public Task<WmsDeactivateResult> DeactivateAsync(WmsDeactivateRequest req, CancellationToken ct = default)
            => Task.FromResult(new WmsDeactivateResult { Success = false, Reason = "W-SPACE-404 库存非0" });
    }

    [Fact]
    public async Task Deactivate_WmsRejects_StatusStays1_NoEvent()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = Guid.NewGuid(), RackId = Guid.NewGuid(),
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BizException>(
            () => MakePublishSvc(db, deact: new RejectingDeactivator()).DeactivateAsync(locId, "u"));

        Assert.Equal("W-SPACE-404", ex.Code);
        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status);            // §6.3：不前进、无翻转回滚
        Assert.Equal(1, loc.Version);           // 版本不动
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());   // 决策失败不发兜底事件
    }

    [Fact]
    public async Task Deactivate_Success_WmsBinInactive_VersionSynced()
    {
        using var db = Db();
        var locId = Guid.NewGuid();
        // 预置已消费的 bin（模拟此前 UPSERT 已落库）
        db.WmsBins.Add(new CP6.Entity.DomainModels.Wms.WmsBin
        {
            Id = locId, LocationCode = "A-01-01-01", WarehouseCd = "WH1", Version = 1, IsActive = true
        });
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = Guid.NewGuid(), RackId = Guid.NewGuid(),
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).DeactivateAsync(locId, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(2, loc.Status);
        Assert.Equal(2, loc.Version);
        var bin = await db.WmsBins.SingleAsync();
        Assert.False(bin.IsActive);
        Assert.Equal(2, bin.Version);           // 同步 RPC 落定的新版本
        Assert.Equal(1, await db.IntegrationEvents.CountAsync());   // ④ 兜底事件已补发
    }

    [Fact]
    public async Task Deactivate_NoBinYet_SyncRpc_WritesTombstone()
    {
        // H6：UPSERT 事件尚未消费（bin 不存在）时停用 → 同步 RPC 落墓碑占住 (Id, Version)
        using var db = Db();
        var (floorId, rackId) = SeedHierarchy(db, siteWarehouseCd: null);   // WarehouseCd 默认=SiteCode "WH1"
        var locId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locId, FloorId = floorId, RackId = rackId,
            Placed = true, Status = 1, CodeOrigin = 1, LocationCode = "A-01-01-01",
            Col = 1, Level = 1, Depth = 1, Version = 1
        });
        await db.SaveChangesAsync();

        await MakePublishSvc(db).DeactivateAsync(locId, "u");

        var tomb = await db.WmsBins.SingleAsync();
        Assert.False(tomb.IsActive);
        Assert.Equal(2, tomb.Version);
        Assert.Equal("WH1", tomb.WarehouseCd);
        Assert.Equal(2, (await db.Space_Locations.SingleAsync()).Status);
    }
}
