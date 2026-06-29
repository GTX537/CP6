# Space P4 · 3D 多层路由 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 跨楼层拣货路由 —— 连接体实体 + 站点级 pick-path 契约 + 前端多层图/3D A* + 编辑器连接体放置工具 + 全站堆叠 3D viewer。

**Architecture:** 承 SP3「图在前端」：后端新增连接体 CRUD + 站点级 `/site/{id}/pick-path`（供 stops+floors+aisles+connectors），前端建多层图（每层复用 SP3 `buildCenterlineGraph` 后按 floorId 命名空间合并 + 连接体竖直边）跑 3D-启发 A*。viewer 新建 `StackedViewer`（每层 `SceneBuilder.build` 置于 `z=层标高`）。**纯加法：单层 08/SP3 全链零改、零回归。**

**Tech Stack:** .NET 8 + EF Core（多租户反射块自动盖章/全局过滤/索引升复合）；Vue3.5 + TS + Three.js + vitest + xUnit。

**spec:** `docs/superpowers/specs/2026-06-29-space-p4-multifloor-design.md`（v1.0）

> 命令在 worktree 跑：bash cwd 每次重置回 `D:\CP6` → 前缀 `cd /d/CP6-space-backend && ...`；前端 `cd /d/CP6-space-backend/cp6.web && ...`。Edit/Read 用 `D:\CP6-space-backend` 绝对路径。提交本地，不 push。

---

## File Structure

**后端新增**：`CP6.Entity/DomainModels/Space/{Space_Connector,Space_ConnectorStop}.cs`、`CP6.Entity/DTOs/Space/ConnectorDtos.cs`、`CP6.Core/Services/Space/{IConnectorService,ConnectorService}.cs`、`CP6.WebApi/Controllers/Space/ConnectorController.cs`、`CP6.Core/Migrations/*_SpaceP4Connector.*`、测试 `CP6.Tests/Space/{ConnectorServiceTests,SitePickPathTests}.cs`。
**后端改动**：`CP6.Core/EFDbContext/CP6Context.cs`（2 DbSet + 索引块）、`CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs`（+site pick-path）、`CP6.WebApi/Program.cs`（+DI）。
**前端新增**：`types/space/connector.ts`、`api/space/connector.ts`、`space-viewer/advanced/multiFloor.ts`(+`.spec`)、`space-viewer/advanced/planMultiFloor.ts`(+`.spec`)、`space-viewer/stacked/StackedViewer.ts`(+`.spec`)、`views/space/stacked/StackedViewer.vue`、`space-editor/connector/ConnectorTool.ts`+`ConnectorPanel.vue`。
**前端改动**：`space-viewer/advanced/PickPathPlanner.ts`(`astar` 启发 3D-tolerant)、`PathAnimator.ts`(Pt3/逐点 z)、`pathModel.ts`(3D)、`api/space/advanced.ts`(+sitePickPath)、`types/space/advanced.ts`(+VO)、`router/index.ts`(+stacked 路由)。
**无既有单层链改动**：`FloorViewer.vue`/`/floor/{id}/pick-path`/`SpaceViewer.ts`/07/08 零改。

---

## Phase A — 后端数据（实体 + 迁移）

### Task A1: 连接体实体 + 枚举

**Files:**
- Create: `CP6.Entity/DomainModels/Space/Space_Connector.cs`
- Create: `CP6.Entity/DomainModels/Space/Space_ConnectorStop.cs`

- [ ] **Step 1: 写实体**（无测试，纯声明；编译即验）

`Space_Connector.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>连接体（电梯/楼梯/坡道）：竖井，经 N 条 ConnectorStop 服务多层（Space P4）。</summary>
[Table("Space_Connector")]
public class Space_Connector : BaseBizEntity
{
    public Guid SiteId { get; set; }

    /// <summary>连接体编码（站内唯一）</summary>
    [Required, MaxLength(50)]
    public string ConnectorCode { get; set; } = string.Empty;

    /// <summary>类型 1=Elevator 2=Stairs 3=Ramp</summary>
    public int ConnectorType { get; set; } = 1;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}
```

`Space_ConnectorStop.cs`:
```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>连接体在某楼层的落点（楼层局部坐标 mm）。一连接体每层至多一落点（Space P4）。</summary>
[Table("Space_ConnectorStop")]
public class Space_ConnectorStop : BaseBizEntity
{
    public Guid ConnectorId { get; set; }
    public Guid FloorId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}
```

- [ ] **Step 2: 编译验证**

Run: `cd /d/CP6-space-backend && dotnet build CP6.Entity/CP6.Entity.csproj -v q`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: 提交**
```bash
cd /d/CP6-space-backend && git add CP6.Entity/DomainModels/Space/Space_Connector.cs CP6.Entity/DomainModels/Space/Space_ConnectorStop.cs && git commit -m "feat(space-p4): Space_Connector + Space_ConnectorStop 实体

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task A2: DbContext DbSet + 索引 + 迁移

**Files:**
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（Space DbSet 块 ~L406-424；Space 索引块 ~L1974-2014）
- Create: `CP6.Core/Migrations/*_SpaceP4Connector.*`（dotnet ef 生成）

- [ ] **Step 1: 加 DbSet**

在 `CP6Context.cs` 的 Space DbSet 块（`public DbSet<Space_Marker> Space_Markers { get; set; }` 之后）加：
```csharp
// ───── Space P4 多层路由 ─────
public DbSet<Space_Connector> Space_Connectors { get; set; }
public DbSet<Space_ConnectorStop> Space_ConnectorStops { get; set; }
```

- [ ] **Step 2: 加显式 (TenantId,…) 唯一索引**

在 Space 索引块（`modelBuilder.Entity<Space_Template>().HasIndex(...)` 之后）加：
```csharp
modelBuilder.Entity<Space_Connector>(e =>
{
    e.HasIndex(x => new { x.TenantId, x.SiteId, x.ConnectorCode }).IsUnique();
    e.HasIndex(x => new { x.TenantId, x.SiteId });
});
modelBuilder.Entity<Space_ConnectorStop>(e =>
{
    e.HasIndex(x => new { x.TenantId, x.ConnectorId, x.FloorId }).IsUnique();
    e.HasIndex(x => new { x.TenantId, x.ConnectorId });
});
```
（反射块见已含 TenantId 前缀，跳过升级；全局查询过滤照旧自动注册。）

- [ ] **Step 3: 编译 + 生成迁移**

Run:
```bash
cd /d/CP6-space-backend && dotnet build CP6.WebApi/CP6.WebApi.csproj -v q && dotnet ef migrations add SpaceP4Connector --project CP6.Core --startup-project CP6.WebApi
```
Expected: Build succeeded；迁移文件 `CP6.Core/Migrations/{ts}_SpaceP4Connector.cs` 生成。

- [ ] **Step 4: 核验迁移**

Read 生成的迁移 `Up()`：确认 2 张表 `Space_Connector`/`Space_ConnectorStop`，每表含 `Id/TenantId/IsDeleted/RowVersion/Creator/CreateDate/Modifier/ModifyDate`，复合唯一索引 `IX_Space_Connector_TenantId_SiteId_ConnectorCode`、`IX_Space_ConnectorStop_TenantId_ConnectorId_FloorId`（TenantId 前缀），无既有表 alter。
Run（确认无待迁移漂移）：`cd /d/CP6-space-backend && dotnet ef migrations has-pending-model-changes --project CP6.Core --startup-project CP6.WebApi`
Expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 5: 提交**
```bash
cd /d/CP6-space-backend && git add CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations && git commit -m "feat(space-p4): DbContext 2 DbSet+复合唯一索引 + SpaceP4Connector 迁移

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase B — 后端契约（CRUD + 站点级 pick-path）

### Task B1: DTO + ConnectorService CRUD + 测试

**Files:**
- Create: `CP6.Entity/DTOs/Space/ConnectorDtos.cs`
- Create: `CP6.Core/Services/Space/IConnectorService.cs` + `ConnectorService.cs`
- Test: `CP6.Tests/Space/ConnectorServiceTests.cs`

- [ ] **Step 1: 写失败测试**

Create `CP6.Tests/Space/ConnectorServiceTests.cs`:
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Space;

public class ConnectorServiceTests
{
    private static (CP6Context, ConnectorService) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new ConnectorService(db));
    }

    [Fact]
    public async Task Create_then_AddStops_then_ListBySite_returns_connector_with_stops()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        var f1 = Guid.NewGuid(); var f2 = Guid.NewGuid();
        var cid = await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "电梯1" }, "u");
        await svc.UpsertStopAsync(cid, new ConnectorStopDto { FloorId = f1, X = 500, Y = 500 }, "u");
        await svc.UpsertStopAsync(cid, new ConnectorStopDto { FloorId = f2, X = 500, Y = 500 }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Single(list);
        Assert.Equal("E1", list[0].ConnectorCode);
        Assert.Equal(2, list[0].Stops.Count);
    }

    [Fact]
    public async Task Create_DuplicateCode_same_site_throws_E501()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "a" }, "u");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "b" }, "u"));
        Assert.Equal("E-SPACE-501", ex.Message);
    }

    [Fact]
    public async Task UpsertStop_same_floor_twice_updates_not_duplicates()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid(); var f1 = Guid.NewGuid();
        var cid = await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "a" }, "u");
        await svc.UpsertStopAsync(cid, new ConnectorStopDto { FloorId = f1, X = 100, Y = 100 }, "u");
        await svc.UpsertStopAsync(cid, new ConnectorStopDto { FloorId = f1, X = 200, Y = 200 }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Single(list[0].Stops);
        Assert.Equal(200, list[0].Stops[0].X);
    }
}
```

- [ ] **Step 2: 运行验证失败**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests/CP6.Tests.csproj --filter ConnectorServiceTests`
Expected: 编译失败（ConnectorService/DTO 未定义）。

- [ ] **Step 3: 写 DTO**

Create `CP6.Entity/DTOs/Space/ConnectorDtos.cs`:
```csharp
namespace CP6.Entity.DTOs.Space;

public class ConnectorDto
{
    public Guid SiteId { get; set; }
    public string ConnectorCode { get; set; } = string.Empty;
    public int ConnectorType { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
}

public class ConnectorStopDto
{
    public Guid FloorId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public class ConnectorView
{
    public Guid Id { get; set; }
    public string ConnectorCode { get; set; } = string.Empty;
    public int ConnectorType { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<ConnectorStopView> Stops { get; set; } = new();
}

public class ConnectorStopView
{
    public Guid FloorId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}
```

- [ ] **Step 4: 写 Service**

Create `CP6.Core/Services/Space/IConnectorService.cs`:
```csharp
using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

public interface IConnectorService
{
    Task<List<ConnectorView>> ListBySiteAsync(Guid siteId);
    Task<Guid> CreateAsync(ConnectorDto d, string? user);
    Task UpsertStopAsync(Guid connectorId, ConnectorStopDto d, string? user);
    Task DeleteStopAsync(Guid connectorId, Guid floorId);
    Task DeleteAsync(Guid id);
}
```

Create `CP6.Core/Services/Space/ConnectorService.cs`:
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>连接体 CRUD（构造只注 CP6Context；TenantId 由 SaveChanges 盖章，查询走全局过滤）。</summary>
public class ConnectorService : IConnectorService
{
    private readonly CP6Context _db;
    public ConnectorService(CP6Context db) => _db = db;

    public async Task<List<ConnectorView>> ListBySiteAsync(Guid siteId)
    {
        var conns = await _db.Space_Connectors.Where(c => c.SiteId == siteId).ToListAsync();
        var ids = conns.Select(c => c.Id).ToList();
        var stops = await _db.Space_ConnectorStops.Where(s => ids.Contains(s.ConnectorId)).ToListAsync();
        return conns.Select(c => new ConnectorView
        {
            Id = c.Id, ConnectorCode = c.ConnectorCode, ConnectorType = c.ConnectorType, Name = c.Name,
            Stops = stops.Where(s => s.ConnectorId == c.Id)
                         .Select(s => new ConnectorStopView { FloorId = s.FloorId, X = s.X, Y = s.Y }).ToList()
        }).ToList();
    }

    public async Task<Guid> CreateAsync(ConnectorDto d, string? user)
    {
        if (await _db.Space_Connectors.AnyAsync(c => c.SiteId == d.SiteId && c.ConnectorCode == d.ConnectorCode))
            throw new InvalidOperationException("E-SPACE-501");
        var e = new Space_Connector
        {
            Id = Guid.NewGuid(), SiteId = d.SiteId, ConnectorCode = d.ConnectorCode,
            ConnectorType = d.ConnectorType, Name = d.Name, Creator = user, CreateDate = DateTime.Now
        };
        _db.Space_Connectors.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpsertStopAsync(Guid connectorId, ConnectorStopDto d, string? user)
    {
        _ = await _db.Space_Connectors.FirstOrDefaultAsync(c => c.Id == connectorId)
            ?? throw new InvalidOperationException("E-SPACE-502");
        var s = await _db.Space_ConnectorStops.FirstOrDefaultAsync(x => x.ConnectorId == connectorId && x.FloorId == d.FloorId);
        if (s is null)
        {
            _db.Space_ConnectorStops.Add(new Space_ConnectorStop
            {
                Id = Guid.NewGuid(), ConnectorId = connectorId, FloorId = d.FloorId, X = d.X, Y = d.Y,
                Creator = user, CreateDate = DateTime.Now
            });
        }
        else { s.X = d.X; s.Y = d.Y; s.Modifier = user; s.ModifyDate = DateTime.Now; }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteStopAsync(Guid connectorId, Guid floorId)
    {
        var s = await _db.Space_ConnectorStops.FirstOrDefaultAsync(x => x.ConnectorId == connectorId && x.FloorId == floorId);
        if (s is null) return;
        _db.Space_ConnectorStops.Remove(s);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var conn = await _db.Space_Connectors.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return;
        var stops = await _db.Space_ConnectorStops.Where(s => s.ConnectorId == id).ToListAsync();
        _db.Space_ConnectorStops.RemoveRange(stops);
        _db.Space_Connectors.Remove(conn);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: 运行测试通过**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests/CP6.Tests.csproj --filter ConnectorServiceTests`
Expected: Passed! 3 tests.

- [ ] **Step 6: 提交**
```bash
cd /d/CP6-space-backend && git add CP6.Entity/DTOs/Space/ConnectorDtos.cs CP6.Core/Services/Space/IConnectorService.cs CP6.Core/Services/Space/ConnectorService.cs CP6.Tests/Space/ConnectorServiceTests.cs && git commit -m "feat(space-p4): ConnectorService CRUD（站内唯一 E-SPACE-501/逐层 upsert stop）+ 测试

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task B2: ConnectorController + DI

**Files:**
- Create: `CP6.WebApi/Controllers/Space/ConnectorController.cs`
- Modify: `CP6.WebApi/Program.cs`（Space DI 块）

- [ ] **Step 1: 写 Controller**

Create `CP6.WebApi/Controllers/Space/ConnectorController.cs`:
```csharp
using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Route("api/space")]
[Authorize]
public class ConnectorController : ControllerBase
{
    private readonly IConnectorService _svc;
    public ConnectorController(IConnectorService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string msg = "OK") => Ok(new { code = 0, message = msg, data });

    [HttpGet("site/{siteId:guid}/connector")]
    public async Task<IActionResult> ListBySite(Guid siteId) => Ok2(await _svc.ListBySiteAsync(siteId));

    [HttpPost("connector")]
    public async Task<IActionResult> Create([FromBody] ConnectorDto d)
    {
        try { return Ok2(new { id = await _svc.CreateAsync(d, CurrentUser) }); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpPut("connector/{id:guid}/stop")]
    public async Task<IActionResult> UpsertStop(Guid id, [FromBody] ConnectorStopDto d)
    {
        try { await _svc.UpsertStopAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpDelete("connector/{id:guid}/stop/{floorId:guid}")]
    public async Task<IActionResult> DeleteStop(Guid id, Guid floorId) { await _svc.DeleteStopAsync(id, floorId); return Ok2(); }

    [HttpDelete("connector/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) { await _svc.DeleteAsync(id); return Ok2(); }
}
```

- [ ] **Step 2: 注册 DI**

在 `Program.cs` Space DI 块（`ISceneIoService` 注册行之后）加：
```csharp
builder.Services.AddScoped<CP6.Core.Services.Space.IConnectorService, CP6.Core.Services.Space.ConnectorService>();
```

- [ ] **Step 3: 编译验证**

Run: `cd /d/CP6-space-backend && dotnet build CP6.WebApi/CP6.WebApi.csproj -v q`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: 提交**
```bash
cd /d/CP6-space-backend && git add CP6.WebApi/Controllers/Space/ConnectorController.cs CP6.WebApi/Program.cs && git commit -m "feat(space-p4): ConnectorController CRUD + DI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task B3: 站点级 pick-path 端点 + VO + 测试

**Files:**
- Modify: `CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs`（加 site-level 端点；既有 `/floor/{id}/pick-path` 不动）
- Test: `CP6.Tests/Space/SitePickPathTests.cs`

- [ ] **Step 1: 写失败测试**

Create `CP6.Tests/Space/SitePickPathTests.cs`（直接测控制器逻辑较重，改测一个可注入的解析助手——为简，测「楼层 Z 累加」纯函数 + 端点集成留 QA）。**先在控制器抽一个 internal static `ComputeFloorZ`**，测它：
```csharp
using CP6.WebApi.Controllers.Space;
using Xunit;

namespace CP6.Tests.Space;

public class SitePickPathTests
{
    [Fact]
    public void ComputeFloorZ_accumulates_by_level_ascending_from_zero()
    {
        // 三层：Level 1 高 6000、Level 2 高 5000、Level 3 高 4000
        var floors = new[]
        {
            (FloorId: Guid.NewGuid(), Level: 2, Height: 5000),
            (FloorId: Guid.NewGuid(), Level: 1, Height: 6000),
            (FloorId: Guid.NewGuid(), Level: 3, Height: 4000),
        };
        var z = SpaceAdvancedController.ComputeFloorZ(
            floors.Select(f => (f.FloorId, f.Level, f.Height)).ToList());
        // Level1 Z=0；Level2 Z=6000；Level3 Z=6000+5000=11000
        Assert.Equal(0, z[floors[1].FloorId]);
        Assert.Equal(6000, z[floors[0].FloorId]);
        Assert.Equal(11000, z[floors[2].FloorId]);
    }
}
```

- [ ] **Step 2: 运行验证失败**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests/CP6.Tests.csproj --filter SitePickPathTests`
Expected: 编译失败（`ComputeFloorZ` 未定义）。

- [ ] **Step 3: 实现端点 + Z 助手**

在 `SpaceAdvancedController` 加（既有 `/floor/{id}/pick-path` 保留）：
```csharp
/// <summary>全站楼层按 Level 升序自底向上累加层高赋 Z（mm）。最低 Level Z=0。 </summary>
public static Dictionary<Guid, int> ComputeFloorZ(List<(Guid FloorId, int Level, int Height)> floors)
{
    var ordered = floors.OrderBy(f => f.Level).ToList();
    var z = new Dictionary<Guid, int>();
    int acc = 0;
    for (int i = 0; i < ordered.Count; i++)
    {
        z[ordered[i].FloorId] = acc;
        acc += ordered[i].Height;
    }
    return z;
}

/// <summary>站点级跨层拣货路径：stops(带 floorId)+全站楼层(含 Z)+涉及层 aisles+站点连接体。</summary>
[HttpGet("site/{siteId:guid}/pick-path")]
public async Task<IActionResult> SitePickPath(Guid siteId, [FromQuery] string taskNo, CancellationToken ct)
{
    var path = await _pick.GetPickPathAsync(taskNo ?? "", ct);
    var codes = path.Items.Select(i => i.LocationCode).Distinct().ToList();

    // 站点全部楼层（供堆叠 + Z）
    var siteFloors = await _db.Space_Floors.Where(f => f.SiteId == siteId)
        .Select(f => new { f.Id, f.Level, f.Height, f.FloorCode }).ToListAsync(ct);
    var zMap = ComputeFloorZ(siteFloors.Select(f => (f.Id, f.Level, f.Height)).ToList());

    // 每 stop → 库位坐标 + floorId（join Space_Location；Placed 且属本站楼层）
    var siteFloorIds = siteFloors.Select(f => f.Id).ToList();
    var locs = (await _db.Space_Locations
        .Where(l => siteFloorIds.Contains(l.FloorId) && l.Placed && l.LocationCode != null && codes.Contains(l.LocationCode!))
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

    var involved = stops.Where(s => s.floorId != null).Select(s => s.floorId!.Value).Distinct().ToList();
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
        stops = connStops.Where(s => s.ConnectorId == c.Id).Select(s => new { floorId = s.FloorId, x = s.X, y = s.Y }).ToList()
    }).ToList();

    var floors = siteFloors.Select(f => new { floorId = f.Id, floorCode = f.FloorCode, level = f.Level, height = f.Height, z = zMap[f.Id] }).ToList();
    return Ok2(new { taskNo = path.TaskNo, floors, stops, aisles, connectors });
}
```
（`_pick`/`_db` 已在控制器构造注入；`Ok2` 已有。）

- [ ] **Step 4: 运行测试通过**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests/CP6.Tests.csproj --filter SitePickPathTests`
Expected: Passed! 1 test。

- [ ] **Step 5: 全量后端回归**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests/CP6.Tests.csproj`
Expected: Passed!（既有 1438 + 新增 connector/site 测，0 fail）。

- [ ] **Step 6: 提交**
```bash
cd /d/CP6-space-backend && git add CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs CP6.Tests/Space/SitePickPathTests.cs && git commit -m "feat(space-p4): site-level pick-path 端点（floorId+楼层Z+连接体）+ ComputeFloorZ 测

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase C — 前端多层图 + 3D A*

### Task C1: `multiFloor.ts`（Pt3 / mfKey / dist3 / FloorMeta）

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/multiFloor.ts` + `.spec.ts`

- [ ] **Step 1: 写失败测试** — `multiFloor.spec.ts`:
```ts
import { describe, it, expect } from 'vitest'
import { mfKey, dist3 } from './multiFloor'

describe('multiFloor', () => {
  it('mfKey namespaces by floorId + 1mm rounded xy', () => {
    expect(mfKey('F1', { x: 100.4, y: 200.6 })).toBe('F1:100,201')
  })
  it('dist3 is 3D euclidean; z=0 reduces to 2D', () => {
    expect(dist3({ x: 0, y: 0, z: 0 }, { x: 3, y: 4, z: 0 })).toBeCloseTo(5)
    expect(dist3({ x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: 7 })).toBeCloseTo(7)
    expect(dist3({ x: 0, y: 0, z: 0 }, { x: 2, y: 3, z: 6 })).toBeCloseTo(7)
  })
})
```

- [ ] **Step 2: 验证失败** — `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/multiFloor.spec.ts` → FAIL（import 未解析）。

- [ ] **Step 3: 实现** — `multiFloor.ts`:
```ts
// cp6.web/src/space-viewer/advanced/multiFloor.ts —— 多层图基元（mm，floorId 命名空间）
import type { Pt } from './PickPathPlanner'

export interface Pt3 { x: number; y: number; z: number }
export interface FloorMeta { floorId: string; z: number }  // z=堆叠标高 mm

/** 多层节点键：楼层命名空间 + 1mm 取整 XY。 */
export const mfKey = (floorId: string, p: { x: number; y: number }): string =>
  `${floorId}:${Math.round(p.x)},${Math.round(p.y)}`

export const dist3 = (a: Pt3, b: Pt3): number => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z)

/** 把 2D 点 + 层 z 升 Pt3。 */
export const lift = (p: Pt, z: number): Pt3 => ({ x: p.x, y: p.y, z })
```

- [ ] **Step 4: 验证通过** — 同命令 → PASS。
- [ ] **Step 5: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/multiFloor.ts cp6.web/src/space-viewer/advanced/multiFloor.spec.ts && git commit -m "feat(space-p4): multiFloor 基元（mfKey/dist3/Pt3/FloorMeta）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task C2: `astar` 启发升 3D-tolerant（单层零回归）

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`（仅 `astar` 启发 + nodePt 类型）
- Test: `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts`（加 3D 用例）

- [ ] **Step 1: 写失败测试** — 在 `PickPathPlanner.spec.ts` `describe` 内加：
```ts
  it('astar heuristic is 3D-tolerant: prefers vertical-short across z', () => {
    // 直连 S→E 经 z；中转 M 平面绕远。验证 3D 启发不破坏最短性。
    const adj = new Map<string, Array<{ to: string; w: number }>>([
      ['A', [{ to: 'B', w: 10 }]],
      ['B', [{ to: 'A', w: 10 }, { to: 'C', w: 10 }]],
      ['C', [{ to: 'B', w: 10 }]],
    ])
    const coords: Record<string, { x: number; y: number; z?: number }> = {
      A: { x: 0, y: 0, z: 0 }, B: { x: 0, y: 0, z: 10 }, C: { x: 0, y: 0, z: 20 },
    }
    expect(astar(adj, 'A', 'C', (k) => coords[k]!)).toEqual(['A', 'B', 'C'])
  })
```
（既有 13 用例不动 = 单层等价回归守卫。）

- [ ] **Step 2: 验证失败** — `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts` → 失败（nodePt 返回 `{x,y,z}` 不满足 `(k)=>Pt` 类型 → tsc/vitest 报错）或断言失败。

- [ ] **Step 3: 改 astar 启发** — 在 `PickPathPlanner.ts`：
  - 把 `astar` 的 `nodePt` 参数类型从 `(k: string) => Pt` 改为 `(k: string) => { x: number; y: number; z?: number }`。
  - 在 `astar` 内加局部启发 `const h = (a: {x:number;y:number;z?:number}, b: typeof a) => Math.hypot(a.x - b.x, a.y - b.y, (a.z ?? 0) - (b.z ?? 0))`。
  - 把两处 `dist(nodePt(...), endPt)` 改为 `h(nodePt(...), endPt)`。
  - **边权 `e.w` 与 global `dist`（addEdge/nearestAccess）一律不动。**

具体：`astar` 体内 `const endPt = nodePt(end)` 后加 `const h = (a:{x:number;y:number;z?:number}, b:{x:number;y:number;z?:number}) => Math.hypot(a.x-b.x, a.y-b.y, (a.z??0)-(b.z??0))`；`f.set(start, dist(nodePt(start), endPt))` → `f.set(start, h(nodePt(start), endPt))`；`f.set(e.to, nd + dist(nodePt(e.to), endPt))` → `f.set(e.to, nd + h(nodePt(e.to), endPt))`。

- [ ] **Step 4: 验证通过** — 同命令 → PASS（新 3D 用例 + 既有 13 全绿；单层 z=undefined→0 与 2D 完全一致）。
- [ ] **Step 5: 全量 advanced 回归** — `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/` → 全绿。
- [ ] **Step 6: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PickPathPlanner.ts cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts && git commit -m "feat(space-p4): astar 启发升 3D-tolerant hypot（z缺省0，边权/global dist 不动，单层零回归）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task C3: `buildMultiFloorGraph`（合并各层图 + 连接体竖直边）

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/planMultiFloor.ts` + `.spec.ts`

> 复用 SP3 内部函数：需把 `PickPathPlanner.ts` 的 `nearestAccess`、`polyDist`、`projectToSegment`、`pathBetween` 改为 **export**（供 planMultiFloor 复用），并 export `Graph`/`key`。改动仅加 `export` 关键字，不改逻辑 → 既有零回归。

- [ ] **Step 1: 把 SP3 复用件 export** — 在 `PickPathPlanner.ts`：`function projectToSegment` → `export function projectToSegment`；`function nearestAccess` → `export function nearestAccess`；`function polyDist` → `export function polyDist`；并 `export const key = ...`（已 const，加 export）。运行 `npx vitest run src/space-viewer/advanced/` 确认零回归后提交一小步（或并入 C3 提交）。

- [ ] **Step 2: 写失败测试** — `planMultiFloor.spec.ts`:
```ts
import { describe, it, expect } from 'vitest'
import { buildMultiFloorGraph } from './planMultiFloor'

const F1 = 'F1', F2 = 'F2'
// 两层各一条横巷 y=500 x[0,1000]；电梯 E1 在两层 (500,500) 各一 stop；层高 6000 → F2 z=6000
const floors = [{ floorId: F1, z: 0 }, { floorId: F2, z: 6000 }]
const aislesByFloor = new Map([
  [F1, [{ aisleCode: 'H1', centerline: '[[0,500],[1000,500]]' }]],
  [F2, [{ aisleCode: 'H2', centerline: '[[0,500],[1000,500]]' }]],
])
const connectors = [{ connectorCode: 'E1', type: 1, stops: [{ floorId: F1, x: 500, y: 500 }, { floorId: F2, x: 500, y: 500 }] }]

describe('buildMultiFloorGraph', () => {
  it('namespaces nodes per floor and adds a vertical connector edge of weight |Δz|', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    // 两层端点都在，floorId 前缀
    expect(g.nodes.has('F1:0,500')).toBe(true)
    expect(g.nodes.has('F2:0,500')).toBe(true)
    // 电梯 stop 节点在两层
    expect(g.nodes.has('F1:500,500')).toBe(true)
    expect(g.nodes.has('F2:500,500')).toBe(true)
    // F1 电梯节点 → F2 电梯节点 竖直边，权 = 6000
    const up = g.adj.get('F1:500,500')!.find((e) => e.to === 'F2:500,500')
    expect(up).toBeTruthy()
    expect(up!.w).toBeCloseTo(6000)
  })
})
```

- [ ] **Step 3: 验证失败** — `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/planMultiFloor.spec.ts` → FAIL。

- [ ] **Step 4: 实现 `buildMultiFloorGraph`** — `planMultiFloor.ts`（起始部分）:
```ts
// cp6.web/src/space-viewer/advanced/planMultiFloor.ts —— 多层图 + 跨层路径（承 SP3，图在前端）
import {
  buildCenterlineGraph, nearestAccess, polyDist, key, astar,
  type Graph, type Pt,
} from './PickPathPlanner'
import { mfKey, dist3, type Pt3, type FloorMeta } from './multiFloor'

export interface MFGraph {
  nodes: Map<string, Pt3>                                   // key=mfKey；z=层标高
  adj: Map<string, Array<{ to: string; w: number }>>
  segments: Array<{ a: Pt; b: Pt; floorId: string }>        // 供按层投影接入
}
export interface AisleVOLite { aisleCode: string; centerline: string }
export interface ConnectorPath { connectorCode: string; type: number; stops: Array<{ floorId: string; x: number; y: number }> }

function addMFEdge(g: MFGraph, ka: string, pa: Pt3, kb: string, pb: Pt3, w: number): void {
  if (ka === kb) return
  if (!g.nodes.has(ka)) g.nodes.set(ka, pa)
  if (!g.nodes.has(kb)) g.nodes.set(kb, pb)
  if (!g.adj.has(ka)) g.adj.set(ka, [])
  if (!g.adj.has(kb)) g.adj.set(kb, [])
  if (!g.adj.get(ka)!.some((e) => e.to === kb)) g.adj.get(ka)!.push({ to: kb, w })
  if (!g.adj.get(kb)!.some((e) => e.to === ka)) g.adj.get(kb)!.push({ to: ka, w })
}

/** 合并各层 SP3 子图（按 floorId 命名空间）+ 连接体接入本层巷道 + 同连接体相邻层 stop 竖直边（权=|Δz|）。 */
export function buildMultiFloorGraph(
  floors: FloorMeta[],
  aislesByFloor: Map<string, AisleVOLite[]>,
  connectors: ConnectorPath[],
): MFGraph {
  const zOf = new Map(floors.map((f) => [f.floorId, f.z]))
  const g: MFGraph = { nodes: new Map(), adj: new Map(), segments: [] }

  // 1) 各层 SP3 子图 → 前缀合并
  for (const f of floors) {
    const z = f.z
    const g2d = buildCenterlineGraph(aislesByFloor.get(f.floorId) ?? [])
    for (const [k2d, pt] of g2d.nodes) g.nodes.set(`${f.floorId}:${k2d}`, { x: pt.x, y: pt.y, z })
    for (const [k2d, list] of g2d.adj) g.adj.set(`${f.floorId}:${k2d}`, list.map((e) => ({ to: `${f.floorId}:${e.to}`, w: e.w })))
    for (const s of g2d.segments) g.segments.push({ a: s.a, b: s.b, floorId: f.floorId })
  }

  // 2) 连接体：每 stop 接入本层最近巷道（投影到段两端）；同连接体相邻层 stop 竖直边
  for (const c of connectors) {
    const placed = c.stops
      .filter((s) => zOf.has(s.floorId))
      .map((s) => ({ s, z: zOf.get(s.floorId)! }))
    for (const { s, z } of placed) {
      // 用该层 segments 建临时 2D 图投影（nearestAccess 吃 SP3 Graph，故拼一个本层 Graph 视图）
      const floorSegs = g.segments.filter((seg) => seg.floorId === s.floorId)
      const acc = nearestAccessOnSegments(floorSegs, { x: s.x, y: s.y })
      const nodeK = mfKey(s.floorId, s)
      const nodeP: Pt3 = { x: s.x, y: s.y, z }
      if (acc) {
        addMFEdge(g, nodeK, nodeP, `${s.floorId}:${key(acc.segA)}`, { x: acc.segA.x, y: acc.segA.y, z }, Math.hypot(s.x - acc.segA.x, s.y - acc.segA.y))
        addMFEdge(g, nodeK, nodeP, `${s.floorId}:${key(acc.segB)}`, { x: acc.segB.x, y: acc.segB.y, z }, Math.hypot(s.x - acc.segB.x, s.y - acc.segB.y))
      } else {
        g.nodes.set(nodeK, nodeP)  // 本层无巷道：仍建节点（竖直边可达，水平段退化）
      }
    }
    // 相邻层竖直边（按 z 排序）
    const sorted = placed.slice().sort((a, b) => a.z - b.z)
    for (let i = 0; i + 1 < sorted.length; i++) {
      const a = sorted[i]!, b = sorted[i + 1]!
      addMFEdge(g, mfKey(a.s.floorId, a.s), { x: a.s.x, y: a.s.y, z: a.z },
                   mfKey(b.s.floorId, b.s), { x: b.s.x, y: b.s.y, z: b.z }, Math.abs(a.z - b.z))
    }
  }
  return g
}

/** nearestAccess 的 segments 版（不依赖 SP3 Graph 实例；投影到最近段取两端）。 */
function nearestAccessOnSegments(segs: Array<{ a: Pt; b: Pt }>, p: Pt): { segA: Pt; segB: Pt } | null {
  let best: { segA: Pt; segB: Pt; d: number } | null = null
  for (const s of segs) {
    const dx = s.b.x - s.a.x, dy = s.b.y - s.a.y
    const len2 = dx * dx + dy * dy
    let t = len2 === 0 ? 0 : ((p.x - s.a.x) * dx + (p.y - s.a.y) * dy) / len2
    t = Math.max(0, Math.min(1, t))
    const foot = { x: s.a.x + t * dx, y: s.a.y + t * dy }
    const d = Math.hypot(p.x - foot.x, p.y - foot.y)
    if (!best || d < best.d) best = { segA: s.a, segB: s.b, d }
  }
  return best ? { segA: best.segA, segB: best.segB } : null
}
```

- [ ] **Step 5: 验证通过** — 同命令 → PASS。
- [ ] **Step 6: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PickPathPlanner.ts cp6.web/src/space-viewer/advanced/planMultiFloor.ts cp6.web/src/space-viewer/advanced/planMultiFloor.spec.ts && git commit -m "feat(space-p4): buildMultiFloorGraph（各层SP3子图前缀合并+连接体接入+竖直边|Δz|）；SP3 复用件 export

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task C4: `pathBetweenMF` + `distanceMatrixMF`

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/planMultiFloor.ts`（追加）+ `.spec.ts`

- [ ] **Step 1: 写失败测试** — 在 `planMultiFloor.spec.ts` 加：
```ts
import { pathBetweenMF, distanceMatrixMF, polyDist3 } from './planMultiFloor'

describe('pathBetweenMF', () => {
  it('routes across floors via the connector (path has a z change)', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const r = pathBetweenMF(g, { floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 })
    expect(r.degraded).toBe(false)
    const zs = r.points.map((p) => p.z)
    expect(Math.min(...zs)).toBeCloseTo(0)        // 起于 F1
    expect(Math.max(...zs)).toBeCloseTo(6000)     // 经电梯到 F2
  })
  it('distanceMatrixMF symmetric + includes vertical leg', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const stops = [{ floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 }]
    const m = distanceMatrixMF(g, stops)
    expect(m[0]![1]).toBeCloseTo(m[1]![0]!)
    expect(m[0]![1]).toBeGreaterThan(6000)        // 含 6000 竖直 + 水平
  })
})
```

- [ ] **Step 2: 验证失败** → FAIL。

- [ ] **Step 3: 实现** — 追加到 `planMultiFloor.ts`:
```ts
export interface MFStop { floorId: string; x: number; y: number }
export interface MFRoute { points: Pt3[]; totalDistance: number; degraded: boolean }

export function polyDist3(pts: Pt3[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += dist3(pts[i - 1]!, pts[i]!)
  return d
}

const nodePt3Of = (g: MFGraph) => (k: string): Pt3 => g.nodes.get(k)!

/** 跨层相邻两拣货点：各端投影到本层巷道接入（临时 FA/FB），astar 跑多层图。不连通→直连 degraded。 */
export function pathBetweenMF(g: MFGraph, a: MFStop, b: MFStop): { points: Pt3[]; degraded: boolean } {
  const zOf = (fid: string): number => {
    for (const p of g.nodes.values()) void p
    // z 来自任一该层节点；用 stop 自身找：扫 nodes 取该 floor 的 z
    for (const [k, p] of g.nodes) if (k.startsWith(`${fid}:`)) return p.z
    return 0
  }
  const za = zOf(a.floorId), zb = zOf(b.floorId)
  const pa: Pt3 = { x: a.x, y: a.y, z: za }, pb: Pt3 = { x: b.x, y: b.y, z: zb }

  const accA = nearestAccessOnSegments(g.segments.filter((s) => s.floorId === a.floorId), { x: a.x, y: a.y })
  const accB = nearestAccessOnSegments(g.segments.filter((s) => s.floorId === b.floorId), { x: b.x, y: b.y })
  if (!accA || !accB) return { points: [pa, pb], degraded: true }

  const adj = new Map<string, Array<{ to: string; w: number }>>()
  for (const [k, list] of g.adj) adj.set(k, list.slice())
  const FA = 'FA', FB = 'FB'
  const link = (n: string, p: MFStop, segA: Pt, segB: Pt, z: number) => {
    const ka = `${p.floorId}:${key(segA)}`, kb = `${p.floorId}:${key(segB)}`
    adj.set(n, [{ to: ka, w: Math.hypot(p.x - segA.x, p.y - segA.y) }, { to: kb, w: Math.hypot(p.x - segB.x, p.y - segB.y) }])
    adj.get(ka)?.push({ to: n, w: Math.hypot(p.x - segA.x, p.y - segA.y) })
    adj.get(kb)?.push({ to: n, w: Math.hypot(p.x - segB.x, p.y - segB.y) })
  }
  link(FA, a, accA.segA, accA.segB, za)
  link(FB, b, accB.segA, accB.segB, zb)

  const nodePt = (k: string): Pt3 => (k === FA ? pa : k === FB ? pb : g.nodes.get(k)!)
  const path = astar(adj, FA, FB, nodePt)
  if (!path) return { points: [pa, pb], degraded: true }
  return { points: path.map(nodePt), degraded: false }
}

export function distanceMatrixMF(g: MFGraph, stops: MFStop[], degradedPairs?: { count: number }): number[][] {
  const n = stops.length
  const m: number[][] = Array.from({ length: n }, () => new Array<number>(n).fill(0))
  for (let i = 0; i < n; i++) for (let j = i + 1; j < n; j++) {
    const seg = pathBetweenMF(g, stops[i]!, stops[j]!)
    const d = polyDist3(seg.points)
    m[i]![j] = d; m[j]![i] = d
    if (seg.degraded && degradedPairs) degradedPairs.count++
  }
  return m
}
```
> 注：`zOf` 简化版扫 nodes 取该层 z；可优化为传入 `FloorMeta[]` 查表（实现时若 stops 多可改 `Map<floorId,z>` 缓存，逻辑等价）。

- [ ] **Step 4: 验证通过** → PASS。
- [ ] **Step 5: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/planMultiFloor.ts cp6.web/src/space-viewer/advanced/planMultiFloor.spec.ts && git commit -m "feat(space-p4): pathBetweenMF + distanceMatrixMF + polyDist3（跨层经连接体，3D折线）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase D — 前端跨层重排对比

### Task D1: `pathModel` 3D

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/pathModel.ts` + `.spec.ts`（若无则建）

- [ ] **Step 1: 写失败测试** — `pathModel.spec.ts`（追加或新建）:
```ts
import { describe, it, expect } from 'vitest'
import { polylineLength3, pointAtDistance3 } from './pathModel'

describe('pathModel 3D', () => {
  it('polylineLength3 sums 3D segments', () => {
    expect(polylineLength3([{ x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: 6000 }, { x: 800, y: 0, z: 6000 }])).toBeCloseTo(6800)
  })
  it('pointAtDistance3 interpolates with z', () => {
    const p = pointAtDistance3([{ x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: 6000 }], 3000)
    expect(p.z).toBeCloseTo(3000)
  })
})
```

- [ ] **Step 2: 验证失败** → FAIL。

- [ ] **Step 3: 实现** — 追加到 `pathModel.ts`:
```ts
import type { Pt3 } from './multiFloor'
const seg3 = (a: Pt3, b: Pt3): number => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z)

export function polylineLength3(pts: Pt3[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += seg3(pts[i - 1]!, pts[i]!)
  return d
}
export function pointAtDistance3(pts: Pt3[], d: number): Pt3 {
  if (pts.length === 0) return { x: 0, y: 0, z: 0 }
  if (pts.length === 1 || d <= 0) return { ...pts[0]! }
  let remain = d
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1]!, b = pts[i]!
    const l = seg3(a, b)
    if (remain <= l) {
      const t = l === 0 ? 0 : remain / l
      return { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, z: a.z + (b.z - a.z) * t }
    }
    remain -= l
  }
  return { ...pts[pts.length - 1]! }
}
```
（既有 2D `polylineLength`/`pointAtDistance` 不动 → 单层 PathAnimator 零回归。）

- [ ] **Step 4: 验证通过** → PASS。
- [ ] **Step 5: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/pathModel.ts cp6.web/src/space-viewer/advanced/pathModel.spec.ts && git commit -m "feat(space-p4): pathModel 3D（polylineLength3/pointAtDistance3，2D 保留）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task D2: `planPickComparisonMF`

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/planMultiFloor.ts`（追加）+ `.spec.ts`

- [ ] **Step 1: 写失败测试** — 在 `planMultiFloor.spec.ts` 加：
```ts
import { planPickComparisonMF } from './planMultiFloor'

describe('planPickComparisonMF', () => {
  it('optimized never longer than actual; savings>=0; points carry z', () => {
    const g3 = { floors, aislesByFloor, connectors }
    // 绕路 LineNo：F1 左 → F2 右 → F1 右 → F2 左
    const stops = [
      { floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 },
      { floorId: F1, x: 900, y: 520 }, { floorId: F2, x: 100, y: 520 },
    ]
    const cmp = planPickComparisonMF(g3.floors, g3.aislesByFloor, g3.connectors, stops)
    expect(cmp.order[0]).toBe(0)
    expect(cmp.optimizedMm).toBeLessThanOrEqual(cmp.actualMm + 1e-6)
    expect(cmp.savingsPct).toBeGreaterThanOrEqual(0)
    expect(cmp.actual.points.some((p) => p.z > 0)).toBe(true)   // 含跨层段
  })
  it('single stop -> zero, savings 0', () => {
    const cmp = planPickComparisonMF(floors, aislesByFloor, connectors, [{ floorId: F1, x: 100, y: 520 }])
    expect(cmp.actualMm).toBe(0); expect(cmp.savingsPct).toBe(0)
  })
})
```

- [ ] **Step 2: 验证失败** → FAIL。

- [ ] **Step 3: 实现** — 追加到 `planMultiFloor.ts`（复用 SP3 `routeOptimize`，矩阵法楼层无关）:
```ts
import { optimizeOrder, routeLengthByOrder } from './routeOptimize'

export interface MFComparison {
  actual: MFRoute; optimized: MFRoute; order: number[]
  actualMm: number; optimizedMm: number; savingsPct: number; degradedPairCount: number
}

function planRouteOnMFGraph(g: MFGraph, stops: MFStop[]): MFRoute {
  if (stops.length < 2) return { points: stops.map((s) => ({ x: s.x, y: s.y, z: nodeZ(g, s.floorId) })), totalDistance: 0, degraded: false }
  const points: Pt3[] = []
  let degraded = false
  for (let i = 0; i + 1 < stops.length; i++) {
    const seg = pathBetweenMF(g, stops[i]!, stops[i + 1]!)
    degraded = degraded || seg.degraded
    const pts = i === 0 ? seg.points : seg.points.slice(1)
    points.push(...pts)
  }
  return { points, totalDistance: polyDist3(points), degraded }
}
function nodeZ(g: MFGraph, fid: string): number {
  for (const [k, p] of g.nodes) if (k.startsWith(`${fid}:`)) return p.z
  return 0
}

export function planPickComparisonMF(
  floors: FloorMeta[], aislesByFloor: Map<string, AisleVOLite[]>, connectors: ConnectorPath[], stops: MFStop[],
): MFComparison {
  const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
  const actual = planRouteOnMFGraph(g, stops)
  if (stops.length < 2) return { actual, optimized: actual, order: stops.map((_, i) => i), actualMm: actual.totalDistance, optimizedMm: actual.totalDistance, savingsPct: 0, degradedPairCount: 0 }
  const degradedPairs = { count: 0 }
  const matrix = distanceMatrixMF(g, stops, degradedPairs)
  const actualOrder = stops.map((_, i) => i)
  const candidate = optimizeOrder(matrix)
  const order = routeLengthByOrder(matrix, candidate) + 1e-9 < routeLengthByOrder(matrix, actualOrder) ? candidate : actualOrder
  const optimized = planRouteOnMFGraph(g, order.map((i) => stops[i]!))
  const actualMm = actual.totalDistance, optimizedMm = optimized.totalDistance
  const savingsPct = actualMm === 0 ? 0 : Math.max(0, ((actualMm - optimizedMm) / actualMm) * 100)
  return { actual, optimized, order, actualMm, optimizedMm, savingsPct, degradedPairCount: degradedPairs.count }
}
```

- [ ] **Step 4: 验证通过** → PASS。
- [ ] **Step 5: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/planMultiFloor.ts cp6.web/src/space-viewer/advanced/planMultiFloor.spec.ts && git commit -m "feat(space-p4): planPickComparisonMF（复用 routeOptimize，跨层 actual baseline 兜底）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase E — 编辑器连接体放置工具

### Task E1: connector 类型 + api

**Files:**
- Create: `cp6.web/src/types/space/connector.ts` + `cp6.web/src/api/space/connector.ts`

- [ ] **Step 1: 类型** — `types/space/connector.ts`:
```ts
export interface ConnectorStopVO { floorId: string; x: number; y: number }
export interface ConnectorVO { id: string; connectorCode: string; connectorType: number; name: string; stops: ConnectorStopVO[] }
export interface ConnectorCreate { siteId: string; connectorCode: string; connectorType: number; name: string }
```

- [ ] **Step 2: api** — `api/space/connector.ts`（仿 `api/space/advanced.ts` 用 http + Envelope）:
```ts
import http from '@/api/http'
import type { ConnectorVO, ConnectorCreate, ConnectorStopVO } from '@/types/space/connector'
type Envelope<T> = { code: number; message: string; data: T }

export const connectorApi = {
  listBySite: (siteId: string) => http.get<unknown, Envelope<ConnectorVO[]>>(`/space/site/${siteId}/connector`),
  create: (d: ConnectorCreate) => http.post<unknown, Envelope<{ id: string }>>(`/space/connector`, d),
  upsertStop: (id: string, s: ConnectorStopVO) => http.put<unknown, Envelope<null>>(`/space/connector/${id}/stop`, s),
  deleteStop: (id: string, floorId: string) => http.delete<unknown, Envelope<null>>(`/space/connector/${id}/stop/${floorId}`),
  remove: (id: string) => http.delete<unknown, Envelope<null>>(`/space/connector/${id}`),
}
```
> 实现前确认 `api/http` 的 get/post/put/delete 签名（看 `api/space/advanced.ts`），对齐返回 `Envelope<T>`。

- [ ] **Step 3: 三门（类型 + build）** — `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit` → 0 错。
- [ ] **Step 4: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/types/space/connector.ts cp6.web/src/api/space/connector.ts && git commit -m "feat(space-p4): connector 类型 + api

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task E2: 编辑器放置工具 + 面板（运行态 QA，无 vitest）

**Files:**
- Create: `cp6.web/src/space-editor/connector/ConnectorTool.ts`、`cp6.web/src/views/space/editor/ConnectorPanel.vue`
- Modify: 编辑器壳（`FloorEditor.vue` 或 `InteractionManager`）接入工具

- [ ] **Step 1: 放置工具** — `ConnectorTool.ts`：编辑器当前楼层点击 → 取 Konva pointer worldXY（复用编辑器既有 screen→world，见 `space-editor/SceneStage.ts` 的坐标换算）→ 回调 `onPlace(x,y)`。提供 `enable()/disable()`，激活时 stage 加临时标记。
- [ ] **Step 2: 面板** — `ConnectorPanel.vue`：`connectorApi.listBySite(siteId)` 列站点连接体（每条示意服务楼层 + 本层 stop 高亮）；「新建连接体」表单（code/type/name → `create`）；「在本层放置」按钮 → 启用 ConnectorTool → 点击落点 → `upsertStop(id,{floorId,x,y})`；删 stop / 删连接体。文本用 `t()` plain Chinese（`missingWarn:false`）。
- [ ] **Step 3: 接入编辑器壳** — 在编辑器视图挂 `ConnectorPanel`，传 siteId/floorId。
- [ ] **Step 4: 三门** — `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit && npx vitest run && npm run build` → 全绿（无新 vitest，既有不破）。
- [ ] **Step 5: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/connector cp6.web/src/views/space/editor/ConnectorPanel.vue && git commit -m "feat(space-p4): 编辑器连接体放置工具 + 面板（逐层落点累积）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase F — 全站堆叠 3D viewer

### Task F1: `StackedViewer` 类（每层 build + Z 堆叠 + 多桶）

**Files:**
- Create: `cp6.web/src/space-viewer/stacked/StackedViewer.ts` + `.spec.ts`

> `StackedViewer` 复用 `Renderer`/`SceneRoot`/`Loop`/`CameraController`/`SceneBuilder`（见 as-built）。**不复用 `SpaceViewer.load()`（单层会 `_clearSceneData`）**：自己对每层 `sceneApi.get(floorId)` + `new SceneBuilder().build(scene)`，把 `result.objects` 包进 `new Group()` 置 `position.z = floor.z(mm)`，加到 `sceneRoot`，并按 floorId 存各层 `buckets`。

- [ ] **Step 1: 写纯逻辑测试**（Z 累加 + 分组）— `StackedViewer.spec.ts`（测可抽的纯函数 `accumulateFloorZ`，渲染留运行态）:
```ts
import { describe, it, expect } from 'vitest'
import { accumulateFloorZ } from './StackedViewer'

describe('accumulateFloorZ', () => {
  it('sorts by level asc, z from 0 cumulative by height', () => {
    const z = accumulateFloorZ([
      { id: 'B', level: 2, height: 5000 }, { id: 'A', level: 1, height: 6000 }, { id: 'C', level: 3, height: 4000 },
    ])
    expect(z.get('A')).toBe(0); expect(z.get('B')).toBe(6000); expect(z.get('C')).toBe(11000)
  })
})
```

- [ ] **Step 2: 验证失败** → FAIL。

- [ ] **Step 3: 实现 `StackedViewer`** — `StackedViewer.ts`（导出 `accumulateFloorZ` + 类）:
```ts
import { Group } from 'three'
import { Renderer } from '../core/Renderer'
import { SceneRoot } from '../core/SceneRoot'
import { Loop } from '../core/Loop'
import { CameraController } from '../navigate/CameraController'
import { SceneBuilder } from '../build/SceneBuilder'
import { sceneApi } from '@/api/space/scene'
import { floorApi } from '@/api/space/floor'
import type { FloorVO } from '@/types/space/scene'

export function accumulateFloorZ(floors: Array<{ id: string; level: number; height: number }>): Map<string, number> {
  const ordered = [...floors].sort((a, b) => a.level - b.level)
  const z = new Map<string, number>()
  let acc = 0
  for (const f of ordered) { z.set(f.id, acc); acc += f.height }
  return z
}

export class StackedViewer {
  private _renderer: Renderer
  private _sceneRoot = new SceneRoot()
  // ... scene/camera/loop（仿 SpaceViewer 构造：见 as-built）
  private _floorGroups = new Map<string, Group>()
  private _floorZ = new Map<string, number>()

  constructor(canvas: HTMLCanvasElement) { this._renderer = new Renderer(canvas); /* scene/cam/loop 同 SpaceViewer */ }

  async loadSite(siteId: string): Promise<void> {
    const floors = (await floorApi.list(siteId)).data as FloorVO[]
    this._floorZ = accumulateFloorZ(floors.map((f) => ({ id: f.id, level: f.level, height: f.height })))
    for (const f of floors) {
      const scene = (await sceneApi.get(f.id)).data
      const result = new SceneBuilder().build(scene)
      const grp = new Group()
      grp.position.z = this._floorZ.get(f.id) ?? 0   // 数据空间 mm；SceneRoot 自动 scale/rotate
      for (const o of result.objects) grp.add(o)
      this._sceneRoot.add(grp)
      this._floorGroups.set(f.id, grp)
      // 存 result.buckets by f.id（供后续拾取/着色，可推迟）
      this.requestRender()
    }
    this.frameAll()
  }
  setFloorVisible(floorId: string, v: boolean): void { const g = this._floorGroups.get(floorId); if (g) { g.visible = v; this.requestRender() } }
  getSceneRoot(): Group { return this._sceneRoot }
  getFloorZ(floorId: string): number { return this._floorZ.get(floorId) ?? 0 }
  requestRender(): void { /* loop.markDirty() */ }
  frameAll(): void { /* cameraController.focusObject(整栈包围盒) */ }
  start(): void { /* loop.start() */ }
  dispose(): void { /* renderer.dispose() + 清 groups */ }
}
```
> 构造内 scene/camera/loop 的装配**逐字仿 `SpaceViewer` 构造**（as-built 报告：Renderer + Scene + SceneRoot + Loop(renderFn 调 cameraController.update + gl.render) + CameraController）。本步只需 `accumulateFloorZ` 测过 + 类编译过；渲染正确性留 H gstack。

- [ ] **Step 4: 验证通过 + 编译** — `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/stacked/StackedViewer.spec.ts && npx vue-tsc --noEmit` → PASS + 0 错。
- [ ] **Step 5: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/stacked && git commit -m "feat(space-p4): StackedViewer 类（accumulateFloorZ + 每层 build 置 z + 多桶）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task F2: `StackedViewer.vue` + 路由

**Files:**
- Create: `cp6.web/src/views/space/stacked/StackedViewer.vue`
- Modify: `cp6.web/src/router/index.ts`

- [ ] **Step 1: 视图** — `StackedViewer.vue`：仿 `FloorViewer.vue` onMounted（建 `StackedViewer(canvas)` → `start()` → `loadSite(route.params.siteId)`）；FloorList 侧栏每层加显隐 toggle（调 `setFloorVisible`）+ 点楼层 `flyTo` 该层 Z 带；advanced 面板（拣货单输入 + 加载）。
- [ ] **Step 2: 路由** — 在 `router/index.ts` space-viewer 路由后加：
```ts
{
  path: '/space/stacked/:siteId',
  name: 'space-stacked',
  component: () => import('@/views/space/stacked/StackedViewer.vue'),
  meta: { standalone: true, title: 'Space 3D 全层叠视图' },
},
```
- [ ] **Step 3: 三门** — `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit && npx vitest run && npm run build` → 全绿。
- [ ] **Step 4: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/views/space/stacked/StackedViewer.vue cp6.web/src/router/index.ts && git commit -m "feat(space-p4): StackedViewer.vue + /space/stacked/:siteId 路由

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase G — 3D 动画 + 接线

### Task G1: `PathAnimator` 升 3D

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/PathAnimator.ts` + `.spec.ts`

- [ ] **Step 1: 写失败测试** — 在 `PathAnimator.spec.ts` 加（fakeViewer 同既有）:
```ts
import type { Pt3 } from './multiFloor'
it('setPath accepts 3D points (z varies); cart positioned with z', () => {
  const v = fakeViewer()
  const a = new PathAnimator(v as any)
  a.setPath([{ x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: 6000 }, { x: 800, y: 0, z: 6000 }] as Pt3[])
  expect(v.root.children[0]!.children.length).toBe(2)   // line + cart
})
```

- [ ] **Step 2: 验证失败** → FAIL（`setPath` 收 `Pt[]`，Pt3 缺 z 处理 + 类型）。

- [ ] **Step 3: 改 PathAnimator 用 Pt3** — 把 `_points: Pt[]` → `Pt3[]`；`setPath(points: Pt3[])`/`setComparisonPath(points: Pt3[]|null)`；线顶点 `arr.push(p.x, p.y, p.z + GROUND_Z)`（**逐点 z + 抬升**，替原 `GROUND_Z` 常量）；`_length = polylineLength3(_points)`；`_positionCart` 用 `pointAtDistance3(_points, _dist)` 设 `cart.position.set(p.x, p.y, p.z + GROUND_Z + CART_SIZE/2)`；`stepNext` 累加用 `dist3`。import `polylineLength3/pointAtDistance3` from `./pathModel`、`Pt3/dist3` from `./multiFloor`。
> 单层调用方（FloorViewer 现传 `Pt[]`）：SP3 的 `cmp.actual.points` 是 `Pt[]`（无 z）。**为单层零回归**：在 FloorViewer 喂前把 `Pt[]` map 成 `Pt3{z:0}`，或 PathAnimator `setPath` 内 `const z = (p as any).z ?? 0`。取后者（`p.z ?? 0`）→ 单层传 Pt 仍 work，零改 FloorViewer。

- [ ] **Step 4: 验证通过 + advanced 回归** — `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/` → 全绿（PathAnimator 既有 6 + 新；单层 z=0 等价）。
- [ ] **Step 5: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PathAnimator.ts cp6.web/src/space-viewer/advanced/PathAnimator.spec.ts && git commit -m "feat(space-p4): PathAnimator 升 3D（逐点 z + pointAtDistance3，单层 z=0 零回归）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task G2: advanced api/VO + StackedViewer 接线 + 三门

**Files:**
- Modify: `cp6.web/src/api/space/advanced.ts`、`cp6.web/src/types/space/advanced.ts`、`cp6.web/src/views/space/stacked/StackedViewer.vue`

- [ ] **Step 1: VO + api** — `types/space/advanced.ts` 加 `SitePickPath`：
```ts
export interface SiteFloorVO { floorId: string; floorCode: string; level: number; height: number; z: number }
export interface SitePickStopVO { seq: number; locationCode: string; qty: number; materialNo: string | null; floorId: string | null; absX: number | null; absY: number | null; absZ: number | null }
export interface SiteAisleVO { floorId: string; aisleCode: string; centerline: string }
export interface SiteConnectorVO { connectorCode: string; type: number; stops: Array<{ floorId: string; x: number; y: number }> }
export interface SitePickPath { taskNo: string; floors: SiteFloorVO[]; stops: SitePickStopVO[]; aisles: SiteAisleVO[]; connectors: SiteConnectorVO[] }
```
`api/space/advanced.ts` 加：
```ts
sitePickPath: (siteId: string, taskNo: string) =>
  http.get<unknown, Envelope<SitePickPath>>(`/space/site/${siteId}/pick-path`, { params: { taskNo } }),
```

- [ ] **Step 2: StackedViewer.vue 接线** — 加载拣货单：调 `advancedApi.sitePickPath(siteId, taskNo)` → 组装 `floors`(FloorMeta: floorId+z)、`aislesByFloor`(Map<floorId, {aisleCode,centerline}[]>)、`connectors`(ConnectorPath)、`stops`(MFStop[]，**按 seq 升序** + 过滤 floorId/absX/absY 非空 → `{floorId, x:absX, y:absY}`) → `planPickComparisonMF(...)` → `pathAnimator.setPath(cmp.actual.points)` + `setComparisonPath(showOptimized? cmp.optimized.points : null)`；面板显「实际/优化/省%」+「显示优化路径」复选框（仿 SP3 FloorViewer 接线 §4.4）。`PathAnimator` 挂 `stackedViewer.getSceneRoot()`（数据空间 mm，点已含层 z）。

- [ ] **Step 3: 三门** — `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit && npx vitest run && npm run build` → vue-tsc 0 / vitest 全绿（≥218 既有 + 新增 multiFloor/planMultiFloor/pathModel3D/StackedViewer/PathAnimator3D）/ build 成功。
- [ ] **Step 4: 提交**
```bash
cd /d/CP6-space-backend && git add cp6.web/src/api/space/advanced.ts cp6.web/src/types/space/advanced.ts cp6.web/src/views/space/stacked/StackedViewer.vue && git commit -m "feat(space-p4): site-pick-path VO/api + StackedViewer 接线（planPickComparisonMF→3D actual/optimized）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase H — QA（多层种子 + gstack 真栈）

### Task H1: 多层 demo 种子 + gstack 验收

**Files:**
- Create: `docs/superpowers/qa/space-p4-multifloor/seed.sql` + `README.md` + 截图

> 环境沿用 SP3：隔离 vite（5180）→ 后端 5177（`appsettings.Local.json`→`CP6DB_SpaceQA`）→ admin/123456。种子坑：`[LineNo]` 保留字 / `SET QUOTED_IDENTIFIER ON`（Space_Location 过滤唯一索引）/ `Space_Aisle.Polygon`+`Centerline` NOT NULL（Polygon `'[]'`）/ Placed 库位须 RackId / sqlcmd 用 PowerShell + ASCII。

- [ ] **Step 1: 写多层种子** — `seed.sql`（幂等 `IF NOT EXISTS`，`SET QUOTED_IDENTIFIER ON`）：
  1. 站点 `QAWH` 已有 F1（Level1）；**新增 F2**（`Space_Floor` SiteId=QAWH、Level=2、Height=6000、FloorCode='F2'）。
  2. F2 一个 Zone + 一条横巷 `Space_Aisle`（Centerline `[[0,500],[4000,500]]`，Polygon `'[]'`）+ 一个 Rack + ≥2 个 Placed 库位（带 RackId，AbsX/Y 沿巷道，如 `B-01-01-01`(500,450)、`B-01-01-02`(3500,450)）。
  3. F1 复用 SP3 的 `SP3-*` 或 `A-01-*` 库位（已 Placed）。
  4. 电梯 `Space_Connector`(SiteId=QAWH, ConnectorCode='E1', ConnectorType=1, Name='电梯1') + 2 `Space_ConnectorStop`（F1 (500,500)、F2 (500,500)）。
  5. 出库单 `OB-P4-CROSS`（QAWH, Status=3）+ 明细跨两层：LineNo 1=F1库位、2=F2库位、3=F1库位、4=F2库位（绕路）。
  运行（PowerShell）：`& "…\SQLCMD.EXE" -S "localhost\KOUSQLSERVER" -E -d CP6DB_SpaceQA -i "D:\CP6-space-backend\docs\superpowers\qa\space-p4-multifloor\seed.sql"`。

- [ ] **Step 2: 启栈 + API 冒烟** — 起后端 5177 + 隔离 vite 5180；登录；curl `GET /api/space/site/{QAWH}/pick-path?taskNo=OB-P4-CROSS` → 200，`floors` 含 F1(z=0)/F2(z=6000)，`stops` 带正确 floorId+absXYZ，`connectors` 含 E1 两 stop。

- [ ] **Step 3: gstack 验收 5 点**（headless Chromium，截图）：
  1. **堆叠**：`/space/stacked/{QAWH}` 渲染 F1+F2（F2 在 F1 上方 z=6000），全几何。
  2. **跨层路径**：加载 `OB-P4-CROSS` → 路径 F1 段走巷道 → 经 E1 竖直上 F2 → F2 段；小车沿 3D 上下。
  3. **对比**：面板「实际/优化/省%」，绿优化线 3D 叠加；开关增删。
  4. **编辑器**：`/space/editor/{F2}` 放置连接体工具落一 stop 指派 E1，面板示意 E1 服务 F1+F2。
  5. **无回归**：单层 `/space/viewer/{QAWH}?floorId={F1}`（08/SP3 单巷 pick-path）、07 库存、08 热图正常。

- [ ] **Step 4: 固化证据 + 提交** — 写 `README.md`（环境 + 5 验收点结论 + 截图 + headless 限制）。
```bash
cd /d/CP6-space-backend && git add docs/superpowers/qa/space-p4-multifloor && git commit -m "test(space-p4): 多层 demo 种子 + gstack 真栈验收（堆叠/跨层路径/对比/编辑器/无回归）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage（逐节核对 spec v1.0）：**
- §2 数据模型（Space_Connector/Stop + enum + DbSet/索引/迁移）→ A1/A2 ✓
- §3 站点级 pick-path + Z 标高 + VO（floorId 在 Space VO，WMS 不动）→ B3 ✓
- §4 多层图（mfKey/dist3/buildMultiFloorGraph/竖直边）+ 3D A*（仅启发，单层零回归）+ pathBetweenMF/distanceMatrixMF → C1/C2/C3/C4 ✓
- §5 planPickComparisonMF（复用 routeOptimize/baseline 兜底）+ pathModel 3D → D1/D2 ✓
- §6 编辑器放置工具 + ConnectorController CRUD → B1/B2/E1/E2 ✓
- §7 堆叠 viewer（StackedViewer 每层 build 置 z + 多桶 + 显隐 + 相机）+ PathAnimator 3D → F1/F2/G1 ✓
- §8 测试（vitest 表 + 后端 + gstack 5 点）→ 各 Task vitest + B + H1 ✓
- §9 文件清单 → File Structure ✓；§10 交付序 A~H → Phase A~H ✓

**2. Placeholder scan:** 无 TBD/TODO；逻辑任务含完整 test+impl 代码与断言。Vue SFC（E2/F2/G2）给结构+关键接线（运行态 QA，非 vitest）——这是有意的（画布/SFC 不单测，仿 SP2/SP3 既有做法），非占位。`StackedViewer` 构造的 scene/camera/loop 装配明确标注「逐字仿 SpaceViewer 构造（as-built 报告）」。

**3. Type consistency:**
- `Pt3{x,y,z}`/`mfKey`/`dist3`/`FloorMeta`（C1）→ planMultiFloor/pathModel3D/PathAnimator/StackedViewer 一致 ✓
- `astar` nodePt 类型 `(k)=>{x;y;z?}`（C2）→ SP3 单层 `(k)=>Pt` 仍可赋值（Pt 是其子集），多层传 Pt3 ✓
- `MFGraph`/`MFStop`/`MFRoute`/`MFComparison`（C3/C4/D2）一致；`buildMultiFloorGraph(floors,aislesByFloor,connectors)` 签名 C3 定义、C4/D2/G2 调用一致 ✓
- `ConnectorDto/ConnectorStopDto/ConnectorView`（B1）→ Controller（B2）/前端 ConnectorVO（E1）镜像一致 ✓
- `SitePickPath` VO（G2）↔ 后端 SitePickPath 响应（B3）字段一致（floors/stops+floorId/aisles+floorId/connectors）✓
- `accumulateFloorZ`（F1 前端）≡ `ComputeFloorZ`（B3 后端）同算法（Level 升序累加）✓
- `planPickComparisonMF` 复用 `optimizeOrder/routeLengthByOrder`（SP3 routeOptimize，零改）✓
- `PathAnimator.setPath(Pt3[])`（G1）+ `p.z ?? 0` 单层兼容 → SP3 FloorViewer 传 Pt[] 零改 ✓

---

## Execution Handoff

见会话——本计划默认 subagent-driven TDD（用户已定流程）。分期 A→H，每阶段实现→spec审→质量审→修；逻辑 vitest/xUnit 当场绿，画布/堆叠运行态留 H gstack。
