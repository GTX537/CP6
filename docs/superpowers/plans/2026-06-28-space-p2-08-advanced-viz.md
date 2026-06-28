# Space P3 · 08 高级可视化（拣货路径动画 + 作业热图 + 设备占位）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 P1（05/06 渲染+定位）+ P2（07 库存叠加）之上叠加「作业级」高级可视化：① 拣货路径动画（消费出库单有序明细 + Aisle 中心线，沿巷道规划路径，小车沿路径跑），② 作业热图（消费库存流水频次，复用 07 着色管线），③ 设备联动 v1 占位（接口 + 静态挂点）。

**Architecture:** 沿用 07 单向只读契约族——3 个新契约（`IWmsPickTaskQuery`/`IWmsWorkloadQuery`/`IWmsDeviceQuery`）定义在消费者 Space 侧 `CP6.Core/Services/Integration/`，WMS 接真实现 `CP6.Core/Services/Wms/`，Space 后端 `Controllers/Space/SpaceAdvancedController.cs` 中转（`/pick-path` 服务端解析 AbsXYZ + 打包该层 Aisle 中心线，前端自给自足建图）。前端新建 `cp6.web/src/space-viewer/advanced/`：`PickPathPlanner`（中心线图 + Dijkstra，纯逻辑）+ `pathModel`（折线弧长，纯逻辑）+ `PathAnimator`（Three.js 小车 + RAF）+ `WorkloadHeatmap`（07 `StockOverlay` 兄弟类）+ `DeviceLayer`（占位）。**零改 WMS 写入**（纯读）；多租户走 `CP6Context` 全局过滤（服务构造只注 `CP6Context`，查询不写 `.Where(TenantId==)`）。**任一数据源失败只降级对应高级功能，不拖垮 P1/P2。**

**Tech Stack:** .NET 8 / EF Core（SqlServer 运行期 + InMemory 测试）/ xUnit（`CP6.Tests`）；Vue 3 + TS + Element Plus + Pinia + Three.js / Vite / Vitest（jsdom，纯逻辑单测）。后端启动项目 `CP6.WebApi`，DbContext + 迁移在 `CP6.Core`。

**配套设计（落码前必读）：**
- `docs/superpowers/specs/2026-06-28-space-p2-p3-stock-overlay-advanced-viz-reconcile-design.md` **§4**（本计划落 08）
- 设计源：`docs/space/08-advanced-viz.md`（08 详规）
- as-built 调研：`docs/superpowers/plans/_space-p2-08-asbuilt-notes.md`（已探查，免重跑）

---

## 通用约定

- **分支/worktree**：本计划在 worktree `D:\CP6-space-backend` @ `feat/space-p1-backend`（**别碰 `D:\CP6`=wfs-B 会话**）。Bash 工具 cwd 每次调用后重置回 `D:\CP6`，故每条 dotnet/git 命令前缀 `cd /d/CP6-space-backend && ...`；Edit/Write/Read 用 `D:\CP6-space-backend` 绝对路径。
- **测试基线**：07 末 `dotnet test CP6.Tests` = 1301 passed / 5 skip。每 Task 末跑相关测试；Part A 收尾跑全量，确认零回归。
- **兼容硬闸**：本计划**纯加法**——新契约/新实现/新控制器/新前端文件，零改既有服务行为与既有契约。`dotnet test CP6.Tests` 任一既有测试转红 = 破坏，回退排查。
- **后端测试 DB 工厂**（沿用 `WmsStockQueryTests`）：
  ```csharp
  private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  ```
  实体 `Id`（`DatabaseGenerated.Identity` Guid）由 InMemory 自动生成，种子不必显式设 `Id`（沿用 07 测试惯例）；`FloorId` 等业务键须显式设。
- **WMS / Space DbSet 名**：`db.OutboundOrders` / `db.OutboundOrderDetails` / `db.StockTransactions` / `db.Space_Locations` / `db.Space_Zones` / `db.Space_Aisles`（均继承 `BaseBizEntity`，SaveChanges 自动盖 `TenantId`）。
- **关键常量/字段**：`OutboundOrder.Status` 中 `3=ピッキング中`（Picking）；`OutboundOrderDetail.LineNo` = 拣货序；`Space_Location.AbsX/AbsY/AbsZ`（`int?` mm）；`Space_Aisle.Centerline`（`nvarchar(max)` JSON `[[x,y],…]` mm，默认 `"[]"`）；`Space_Zone.FloorId`（楼层 join）；`StockTransaction.TxnDateTime`/`LocationCd`/`TxnType`（string IN/OUT/MOVE/ADJ/RSV/UNRSV）。
- **错误/消息码**：08 多为前端展示（`W-/I-SPACE-8xx`，见 spec §9）。后端裸码沿用 `InvalidOperationException`（本计划无新增后端业务码）。
- **前端 http**：`http.get<unknown, Envelope<T>>(...)` 直接返回 `Envelope<T>`（拦截器已 unwrap `response.data`）；`Envelope<T> = { code; message; data }`（`types/space/scene.ts`）。
- **坐标系铁律**：所有 08 几何在**数据空间（mm）** 计算；path/cart/device mesh **parent 到 `viewer.getSceneRoot()`**（该 Group 自带 `scale 0.001` + `rotation.x=-π/2`，子节点位置直接用 mm），不必手调 `dataToWorld`。
- **commit**：每 Task 末本地 commit（**不 push**；push 由用户自跑——会话权限拦 git push）。

---

## File Structure（先锁分解）

**后端新建：**
- `CP6.Core/Services/Integration/IWmsAdvancedQueries.cs` — 3 契约接口 + DTO（`IWmsPickTaskQuery`/`PickPathDto`/`PickStop`、`IWmsWorkloadQuery`/`WorkloadDto`、`IWmsDeviceQuery`/`DeviceDto`）
- `CP6.Core/Services/Wms/WmsPickTaskQuery.cs` — 接真（出库单有序明细）
- `CP6.Core/Services/Wms/WmsWorkloadQuery.cs` — 接真（流水频次，按层 Placed 编码 + 时间窗）
- `CP6.Core/Services/Wms/WmsDeviceQuery.cs` — v1 占位（返空）
- `CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs` — `/pick-path`（解析 AbsXYZ + 打包 aisle 中心线）、`/workload`、`/devices`

**后端修改：**
- `CP6.WebApi/Program.cs:374` 后 — DI 注册 3 个新查询

**后端测试（`CP6.Tests/`）：**
- `WmsAdvancedQueryTests.cs` — 拣货序/空单/null 库位过滤、热图时间窗计次/按层 Placed 限定/多租户隔离

**前端新建：**
- `cp6.web/src/types/space/advanced.ts` — `PickStopVO`/`AisleCenterlineVO`/`FloorPickPath`/`WorkloadItem`/`FloorWorkload`/`DeviceDto`
- `cp6.web/src/api/space/advanced.ts` — `advancedApi.pickPath` / `workload` / `devices`
- `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts` — `Pt`/中心线图/投影/Dijkstra/`planPickRoute`（+ `.spec.ts`）
- `cp6.web/src/space-viewer/advanced/pathModel.ts` — `polylineLength`/`pointAtDistance`（+ `.spec.ts`）
- `cp6.web/src/space-viewer/advanced/PathAnimator.ts` — Three 小车 + 路径线 + 播放控制（+ `.spec.ts`）
- `cp6.web/src/space-viewer/advanced/workloadModel.ts` — `normalizeOpCounts` + `workloadToHex`（复用 07 渐变）（+ `.spec.ts`）
- `cp6.web/src/space-viewer/advanced/WorkloadHeatmap.ts` — `StockOverlay` 兄弟类（+ `.spec.ts`）
- `cp6.web/src/space-viewer/advanced/DeviceLayer.ts` — SceneRoot 挂点占位（+ `.spec.ts`）
- `cp6.web/src/views/space/viewer/AdvancedPanel.vue` — 控件（拣货任务/播放控制/热图/设备）

**前端修改：**
- `cp6.web/src/views/space/viewer/FloorViewer.vue` — 接线三模块 + AdvancedPanel + 楼层切换时清理

**种子 + QA：**
- `docs/superpowers/qa/space-p2-08/seed.sql` — 演示种子（出库单 + 流水 + 中心线）
- `docs/superpowers/qa/space-p2-08/` — gstack QA 记录 + 截图

---

# Part A — 后端契约 + 接真

## Task 1：契约 `IWmsAdvancedQueries.cs` + `WmsPickTaskQuery` 接真

**Files:**
- Create: `CP6.Core/Services/Integration/IWmsAdvancedQueries.cs`
- Create: `CP6.Core/Services/Wms/WmsPickTaskQuery.cs`
- Test: `CP6.Tests/WmsAdvancedQueryTests.cs`

> `GetPickPathAsync(taskNo)` = `OutboundOrder.Where(OutboundNo==taskNo)`（无则返空 DTO）+ `OutboundOrderDetail.Where(OutboundNo==taskNo && LocationCd!=null).OrderBy(LineNo)` → `PickStop[]`（Seq=LineNo, Qty=RequiredQty, MaterialNo=ProductCd）。**不硬过滤 Status**（reconcile §4.2 口径，QA 更宽松）。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/WmsAdvancedQueryTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class WmsAdvancedQueryTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedOrderAsync(CP6Context db, string ob, int status,
        params (int line, string code, decimal qty, string product)[] lines)
    {
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = ob, WarehouseCd = "W1", Status = status });
        foreach (var (line, code, qty, product) in lines)
            db.OutboundOrderDetails.Add(new OutboundOrderDetail
            {
                OutboundNo = ob, LineNo = line, ProductCd = product, LocationCd = code,
                RequiredQty = qty, AllocatedQty = qty, ShippedQty = 0m,
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPickPath_OrdersByLineNo()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "OB-1", 3,
            (3, "A-03", 1m, "P3"), (1, "A-01", 5m, "P1"), (2, "A-02", 2m, "P2"));

        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("OB-1");

        Assert.Equal("OB-1", path.TaskNo);
        Assert.Equal(3, path.Items.Count);
        Assert.Equal(new[] { 1, 2, 3 }, path.Items.Select(i => i.Seq).ToArray());
        Assert.Equal("A-01", path.Items[0].LocationCode);
        Assert.Equal(5m, path.Items[0].Qty);
        Assert.Equal("P1", path.Items[0].MaterialNo);
    }

    [Fact]
    public async Task GetPickPath_SkipsNullLocationLines()
    {
        using var db = NewDb();
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OB-2", WarehouseCd = "W1", Status = 3 });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OB-2", LineNo = 1, ProductCd = "P1", LocationCd = "A-01", RequiredQty = 1m });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OB-2", LineNo = 2, ProductCd = "P2", LocationCd = null, RequiredQty = 1m });
        await db.SaveChangesAsync();

        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("OB-2");
        Assert.Equal("A-01", Assert.Single(path.Items).LocationCode);
    }

    [Fact]
    public async Task GetPickPath_UnknownOrder_EmptyItems()
    {
        using var db = NewDb();
        var path = await new WmsPickTaskQuery(db).GetPickPathAsync("NOPE");
        Assert.Equal("NOPE", path.TaskNo);
        Assert.Empty(path.Items);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests --filter "FullyQualifiedName~WmsAdvancedQueryTests"`
Expected: 编译失败（`IWmsPickTaskQuery`/`PickPathDto`/`WmsPickTaskQuery` 未定义）。

- [ ] **Step 3: 建契约文件 `IWmsAdvancedQueries.cs`**

`CP6.Core/Services/Integration/IWmsAdvancedQueries.cs`：
```csharp
namespace CP6.Core.Services.Integration;

/// <summary>
/// WMS 高级可视化只读查询契约族（08；消费者 Space 侧定义，WMS 接真实现）。
/// 与 <see cref="IWmsStockQuery"/> 同族：单向、纯读、join 按 LocationCode/FloorId。
/// 多租户由 CP6Context 全局过滤自动隔离（无 tenantId 参数）。
/// </summary>
public interface IWmsPickTaskQuery
{
    /// <summary>取拣货任务的有序拣货点（源=出库单 + 明细，按 LineNo 序）。未知单 → 空 Items。</summary>
    Task<PickPathDto> GetPickPathAsync(string taskNo, CancellationToken ct = default);
}

/// <summary>拣货任务有序库位序列（Space 后端再补 AbsXYZ）。</summary>
public sealed class PickPathDto
{
    public string TaskNo { get; set; } = "";
    public IReadOnlyList<PickStop> Items { get; set; } = [];
}

/// <summary>单个拣货点（有序）。</summary>
public sealed class PickStop
{
    public int     Seq          { get; set; }          // = LineNo（拣货序）
    public string  LocationCode { get; set; } = "";
    public decimal Qty          { get; set; }          // = RequiredQty
    public string? MaterialNo   { get; set; }          // = ProductCd
}

/// <summary>作业热图查询（源=库存流水按库位×时间窗计次）。</summary>
public interface IWmsWorkloadQuery
{
    Task<IReadOnlyList<WorkloadDto>> GetWorkloadAsync(
        Guid floorId, DateTime from, DateTime to, CancellationToken ct = default);
}

/// <summary>某库位时间窗内作业次数。</summary>
public sealed class WorkloadDto
{
    public string LocationCode { get; set; } = "";
    public int    OpCount      { get; set; }
}

/// <summary>设备联动查询（v1 占位，WMS 返空/示例）。</summary>
public interface IWmsDeviceQuery
{
    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(Guid floorId, CancellationToken ct = default);
}

/// <summary>设备图元（v1 占位；Space 后端可按 LocationCode 补 AbsXYZ）。</summary>
public sealed class DeviceDto
{
    public string  DeviceId     { get; set; } = "";
    public string  Type         { get; set; } = "";   // AGV/Stacker/Conveyor…
    public string? LocationCode { get; set; }
    public int     Status       { get; set; }          // 0闲 1忙 2故障…
}
```

- [ ] **Step 4: 建 `WmsPickTaskQuery.cs`**

`CP6.Core/Services/Wms/WmsPickTaskQuery.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>拣货路径只读实现（读 T_OutboundOrder/T_OutboundOrderDetail；纯读，多租户全局过滤自动隔离）。</summary>
public class WmsPickTaskQuery : IWmsPickTaskQuery
{
    private readonly CP6Context _db;
    public WmsPickTaskQuery(CP6Context db) => _db = db;

    public async Task<PickPathDto> GetPickPathAsync(string taskNo, CancellationToken ct = default)
    {
        var exists = await _db.OutboundOrders.AnyAsync(o => o.OutboundNo == taskNo, ct);
        if (!exists) return new PickPathDto { TaskNo = taskNo };

        var details = await _db.OutboundOrderDetails
            .Where(d => d.OutboundNo == taskNo && d.LocationCd != null)
            .OrderBy(d => d.LineNo)
            .ToListAsync(ct);

        var stops = details.Select(d => new PickStop
        {
            Seq = d.LineNo,
            LocationCode = d.LocationCd!,
            Qty = d.RequiredQty,
            MaterialNo = d.ProductCd,
        }).ToList();

        return new PickPathDto { TaskNo = taskNo, Items = stops };
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests --filter "FullyQualifiedName~WmsAdvancedQueryTests"`
Expected: PASS（3 例）。

- [ ] **Step 6: Commit**

```bash
cd /d/CP6-space-backend && git add CP6.Core/Services/Integration/IWmsAdvancedQueries.cs CP6.Core/Services/Wms/WmsPickTaskQuery.cs CP6.Tests/WmsAdvancedQueryTests.cs && git commit -m "feat(space-08): T1 高级可视化契约族 + WmsPickTaskQuery 接真(出库单有序明细)"
```

---

## Task 2：`WmsWorkloadQuery` 接真（流水频次 / 按层 Placed 编码 + 时间窗）

**Files:**
- Create: `CP6.Core/Services/Wms/WmsWorkloadQuery.cs`
- Test: `CP6.Tests/WmsAdvancedQueryTests.cs`（追加）

> `GetWorkloadAsync(floorId, from, to)` = 取该层 `Space_Location`（Placed∧编码非空）编码集 → `StockTransaction.Where(TxnDateTime∈[from,to) ∧ LocationCd∈编码集).GroupBy(LocationCd).Count()`。默认全 `TxnType` 计次。

- [ ] **Step 1: 写失败测试（追加到 `WmsAdvancedQueryTests`）**

```csharp
    private static async Task SeedPlacedLocAsync(CP6Context db, Guid floorId, string code)
    {
        db.Space_Locations.Add(new Space_Location
        {
            FloorId = floorId, LocationCode = code, Placed = true, Status = 1,
            AbsX = 100, AbsY = 200, AbsZ = 300,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedTxnAsync(CP6Context db, string code, DateTime when, string type = "OUT")
    {
        db.StockTransactions.Add(new StockTransaction
        {
            TxnNo = "TXN-" + Guid.NewGuid().ToString("N")[..8], TxnType = type, TxnDateTime = when,
            WarehouseCd = "W1", LocationCd = code, ProductCd = "P1", LotNo = "", Qty = 1m,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetWorkload_CountsInWindow_ByLocation()
    {
        using var db = NewDb();
        var floor = Guid.NewGuid();
        var t0 = new DateTime(2026, 6, 28, 9, 0, 0);
        await SeedPlacedLocAsync(db, floor, "A-01");
        await SeedPlacedLocAsync(db, floor, "A-02");
        await SeedTxnAsync(db, "A-01", t0);
        await SeedTxnAsync(db, "A-01", t0.AddMinutes(10));
        await SeedTxnAsync(db, "A-01", t0.AddMinutes(20));
        await SeedTxnAsync(db, "A-02", t0.AddMinutes(5));

        var rows = await new WmsWorkloadQuery(db).GetWorkloadAsync(
            floor, t0.Date, t0.Date.AddDays(1));

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows.Single(r => r.LocationCode == "A-01").OpCount);
        Assert.Equal(1, rows.Single(r => r.LocationCode == "A-02").OpCount);
    }

    [Fact]
    public async Task GetWorkload_ExcludesOutsideWindow()
    {
        using var db = NewDb();
        var floor = Guid.NewGuid();
        var t0 = new DateTime(2026, 6, 28, 9, 0, 0);
        await SeedPlacedLocAsync(db, floor, "A-01");
        await SeedTxnAsync(db, "A-01", t0.AddDays(-2));   // 窗外
        await SeedTxnAsync(db, "A-01", t0);               // 窗内

        var rows = await new WmsWorkloadQuery(db).GetWorkloadAsync(floor, t0.Date, t0.Date.AddDays(1));
        Assert.Equal(1, Assert.Single(rows).OpCount);
    }

    [Fact]
    public async Task GetWorkload_OnlyFloorPlacedCodes()
    {
        using var db = NewDb();
        var floor = Guid.NewGuid();
        var t0 = new DateTime(2026, 6, 28, 9, 0, 0);
        await SeedPlacedLocAsync(db, floor, "A-01");      // 本层 placed
        // "B-99" 流水但不属本层任何 placed 库位 → 不计
        await SeedTxnAsync(db, "A-01", t0);
        await SeedTxnAsync(db, "B-99", t0);

        var rows = await new WmsWorkloadQuery(db).GetWorkloadAsync(floor, t0.Date, t0.Date.AddDays(1));
        Assert.Equal("A-01", Assert.Single(rows).LocationCode);
    }

    [Fact]
    public async Task GetWorkload_NoPlacedLocations_Empty()
    {
        using var db = NewDb();
        var rows = await new WmsWorkloadQuery(db).GetWorkloadAsync(
            Guid.NewGuid(), DateTime.Today, DateTime.Today.AddDays(1));
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetWorkload_TenantIsolated()
    {
        var dbName = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(dbName).Options;
        var floor = Guid.NewGuid();
        var t0 = new DateTime(2026, 6, 28, 9, 0, 0);
        var t2 = new CP6.Core.Services.Common.TenantContext { CurrentTenantId = Guid.NewGuid() };

        using (var db2 = new CP6Context(opts, t2))
        {
            db2.Space_Locations.Add(new Space_Location { FloorId = floor, LocationCode = "A-01", Placed = true, Status = 1 });
            db2.StockTransactions.Add(new StockTransaction { TxnNo = "TXN-X", TxnType = "OUT", TxnDateTime = t0, WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P1", LotNo = "", Qty = 1m });
            await db2.SaveChangesAsync();
        }
        using var dbDefault = new CP6Context(opts);
        var rows = await new WmsWorkloadQuery(dbDefault).GetWorkloadAsync(floor, t0.Date, t0.Date.AddDays(1));
        Assert.Empty(rows);   // 默认租户看不到租户2 的层/流水
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests --filter "FullyQualifiedName~WmsAdvancedQueryTests.GetWorkload"`
Expected: 编译失败（`WmsWorkloadQuery` 未定义）。

- [ ] **Step 3: 建 `WmsWorkloadQuery.cs`**

`CP6.Core/Services/Wms/WmsWorkloadQuery.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>作业热图只读实现（读 Space_Location 取该层 Placed 编码 + T_StockTransaction 计次；纯读，全局过滤隔离）。</summary>
public class WmsWorkloadQuery : IWmsWorkloadQuery
{
    private readonly CP6Context _db;
    public WmsWorkloadQuery(CP6Context db) => _db = db;

    public async Task<IReadOnlyList<WorkloadDto>> GetWorkloadAsync(
        Guid floorId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var codes = await _db.Space_Locations
            .Where(l => l.FloorId == floorId && l.Placed && l.LocationCode != null)
            .Select(l => l.LocationCode!)
            .ToListAsync(ct);
        if (codes.Count == 0) return Array.Empty<WorkloadDto>();

        return await _db.StockTransactions
            .Where(t => t.TxnDateTime >= from && t.TxnDateTime < to && codes.Contains(t.LocationCd))
            .GroupBy(t => t.LocationCd)
            .Select(g => new WorkloadDto { LocationCode = g.Key, OpCount = g.Count() })
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests --filter "FullyQualifiedName~WmsAdvancedQueryTests"`
Expected: PASS（全部）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add CP6.Core/Services/Wms/WmsWorkloadQuery.cs CP6.Tests/WmsAdvancedQueryTests.cs && git commit -m "feat(space-08): T2 WmsWorkloadQuery 接真(流水按层Placed编码×时间窗计次)"
```

---

## Task 3：`WmsDeviceQuery` 占位 + DI(3) + `SpaceAdvancedController`

**Files:**
- Create: `CP6.Core/Services/Wms/WmsDeviceQuery.cs`
- Create: `CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs`
- Modify: `CP6.WebApi/Program.cs`（374 行后追加 3 注册）

> 控制器无独立 Service：`/pick-path` 调 `IWmsPickTaskQuery` 取序列 → join `Space_Locations`（本层 Placed）补 AbsXYZ → 再 join `Space_Aisles`×`Space_Zones` 打包本层中心线（前端建图自给自足）；`/workload`、`/devices` 直转。沿用 `Ok2` 信封。

- [ ] **Step 1: 建 `WmsDeviceQuery.cs`（v1 占位返空）**

`CP6.Core/Services/Wms/WmsDeviceQuery.cs`：
```csharp
using CP6.Core.Services.Integration;

namespace CP6.Core.Services.Wms;

/// <summary>设备联动 v1 占位：返空（真接 AGV/WCS 实时流 = P3+）。架构留可注入数据点，未来换源不返工。</summary>
public class WmsDeviceQuery : IWmsDeviceQuery
{
    public Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(Guid floorId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DeviceDto>>(Array.Empty<DeviceDto>());
}
```

- [ ] **Step 2: DI 注册（`Program.cs:374` 后追加）**

在 `builder.Services.AddScoped<...IWmsStockQuery, ...WmsStockQuery>();`（374 行）之后插入：
```csharp
builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsPickTaskQuery, CP6.Core.Services.Wms.WmsPickTaskQuery>();
builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsWorkloadQuery, CP6.Core.Services.Wms.WmsWorkloadQuery>();
builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsDeviceQuery, CP6.Core.Services.Wms.WmsDeviceQuery>();
```

- [ ] **Step 3: 建 `SpaceAdvancedController.cs`**

`CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs`：
```csharp
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
```

- [ ] **Step 4: 编译 + Part A 全回归**

Run: `cd /d/CP6-space-backend && dotnet build CP6.WebApi && dotnet test CP6.Tests`
Expected: build 0 error；全量绿（07 末 1301 + 本计划新增约 8 例，零回归）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add CP6.Core/Services/Wms/WmsDeviceQuery.cs CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs CP6.WebApi/Program.cs && git commit -m "feat(space-08): T3 WmsDeviceQuery占位 + DI(3) + SpaceAdvancedController(/pick-path解析AbsXYZ+aisle中心线、/workload、/devices)"
```

---

# Part B — 前端 advanced

## Task 4：TS 类型 + API 层

**Files:**
- Create: `cp6.web/src/types/space/advanced.ts`
- Create: `cp6.web/src/api/space/advanced.ts`

- [ ] **Step 1: 建类型 `advanced.ts`**

```typescript
// cp6.web/src/types/space/advanced.ts —— 对齐 SpaceAdvancedController 响应
export interface PickStopVO {
  seq: number
  locationCode: string
  qty: number
  materialNo: string | null
  absX: number | null
  absY: number | null
  absZ: number | null
}

export interface AisleCenterlineVO {
  aisleCode: string
  centerline: string   // JSON [[x,y],...]（mm）
}

export interface FloorPickPath {
  taskNo: string
  stops: PickStopVO[]
  aisles: AisleCenterlineVO[]
}

export interface WorkloadItem {
  locationCode: string
  opCount: number
}

export interface FloorWorkload {
  items: WorkloadItem[]
  from: string
  to: string
}

export interface DeviceDto {
  deviceId: string
  type: string
  status: number
  locationCode: string | null
  absX: number | null
  absY: number | null
  absZ: number | null
}
```

- [ ] **Step 2: 建 API `advanced.ts`**

```typescript
// cp6.web/src/api/space/advanced.ts
import http from '../http'
import type { Envelope } from '@/types/space/scene'
import type { FloorPickPath, FloorWorkload, DeviceDto } from '@/types/space/advanced'

export const advancedApi = {
  pickPath(floorId: string, taskNo: string) {
    return http.get<unknown, Envelope<FloorPickPath>>(`/space/floor/${floorId}/pick-path`, { params: { taskNo } })
  },
  workload(floorId: string, from: string, to: string) {
    return http.get<unknown, Envelope<FloorWorkload>>(`/space/floor/${floorId}/workload`, { params: { from, to } })
  },
  devices(floorId: string) {
    return http.get<unknown, Envelope<DeviceDto[]>>(`/space/floor/${floorId}/devices`)
  },
}
```

- [ ] **Step 3: 类型校验**

Run: `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit`
Expected: 0 error。

- [ ] **Step 4: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/types/space/advanced.ts cp6.web/src/api/space/advanced.ts && git commit -m "feat(space-08): T4 前端 advanced 类型 + api(pick-path/workload/devices)"
```

---

## Task 5：`PickPathPlanner.ts` 纯逻辑（中心线图 + Dijkstra + planRoute）+ vitest

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`
- Test: `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts`

> 纯函数：`parseCenterline`（容错）/`buildCenterlineGraph`（顶点按 1mm 取整去重合并交叉点）/`projectToSegment`/`pathBetween`（投影接入点 + Dijkstra 沿巷道，失败退化直连 `degraded`）/`planPickRoute`（依次拼接相邻拣货点）。

- [ ] **Step 1: 写失败测试**

```typescript
// cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts
import { describe, it, expect } from 'vitest'
import { parseCenterline, buildCenterlineGraph, planPickRoute } from './PickPathPlanner'

describe('PickPathPlanner', () => {
  it('parseCenterline parses valid and tolerates garbage', () => {
    expect(parseCenterline('[[0,0],[100,0]]')).toEqual([{ x: 0, y: 0 }, { x: 100, y: 0 }])
    expect(parseCenterline('')).toEqual([])
    expect(parseCenterline('not json')).toEqual([])
    expect(parseCenterline('[]')).toEqual([])
  })

  it('buildCenterlineGraph merges shared endpoints into one node', () => {
    // 两条中心线在 (1000,0) 共端点 → 该点应只有一个图节点
    const g = buildCenterlineGraph([
      { aisleCode: 'H', centerline: '[[0,0],[1000,0]]' },
      { aisleCode: 'V', centerline: '[[1000,0],[1000,1000]]' },
    ])
    expect(g.nodes.has('0,0')).toBe(true)
    expect(g.nodes.has('1000,0')).toBe(true)
    expect(g.nodes.has('1000,1000')).toBe(true)
    expect(g.nodes.size).toBe(3)              // 共端点合并，不是 4
    expect(g.adj.get('1000,0')!.length).toBe(2)  // 连 (0,0) 与 (1000,1000)
  })

  it('planPickRoute routes around the L-corner, not straight diagonal', () => {
    const aisles = [
      { aisleCode: 'H', centerline: '[[0,0],[1000,0]]' },
      { aisleCode: 'V', centerline: '[[1000,0],[1000,1000]]' },
    ]
    const stops = [{ x: 0, y: 100 }, { x: 900, y: 1100 }]
    const route = planPickRoute(aisles, stops)
    expect(route.degraded).toBe(false)
    // 路径经过拐角节点 (1000,0)
    expect(route.points.some((p) => Math.round(p.x) === 1000 && Math.round(p.y) === 0)).toBe(true)
    // 首点=起库位、末点=止库位
    expect(route.points[0]).toEqual({ x: 0, y: 100 })
    expect(route.points[route.points.length - 1]).toEqual({ x: 900, y: 1100 })
    expect(route.totalDistance).toBeGreaterThan(0)
  })

  it('planPickRoute degrades to straight connect when no aisles', () => {
    const route = planPickRoute([], [{ x: 0, y: 0 }, { x: 500, y: 500 }])
    expect(route.degraded).toBe(true)
    expect(route.points).toEqual([{ x: 0, y: 0 }, { x: 500, y: 500 }])
    expect(route.totalDistance).toBeCloseTo(Math.hypot(500, 500))
  })

  it('planPickRoute with <2 stops returns the stops unchanged', () => {
    expect(planPickRoute([], [{ x: 1, y: 2 }]).points).toEqual([{ x: 1, y: 2 }])
    expect(planPickRoute([], []).points).toEqual([])
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts`
Expected: 失败（模块不存在）。

- [ ] **Step 3: 实现 `PickPathPlanner.ts`**

```typescript
// cp6.web/src/space-viewer/advanced/PickPathPlanner.ts
// 拣货路径规划：中心线图 + Dijkstra（纯逻辑，mm 数据空间，2D-XY）。

export interface Pt { x: number; y: number }

export interface PlannedRoute {
  points: Pt[]          // 完整折线（mm，XY），首=起库位 末=止库位
  totalDistance: number // mm
  degraded: boolean     // 任一段退化为直连（W-SPACE-801）
}

interface Graph {
  nodes: Map<string, Pt>
  adj: Map<string, Array<{ to: string; w: number }>>
  segments: Array<{ a: Pt; b: Pt }>
}

const key = (p: Pt): string => `${Math.round(p.x)},${Math.round(p.y)}`
const dist = (a: Pt, b: Pt): number => Math.hypot(a.x - b.x, a.y - b.y)

/** 解析中心线 JSON `[[x,y],…]`；非法/空 → []。 */
export function parseCenterline(json: string): Pt[] {
  if (!json) return []
  try {
    const raw = JSON.parse(json)
    if (!Array.isArray(raw)) return []
    return raw
      .filter((p) => Array.isArray(p) && p.length >= 2 && Number.isFinite(p[0]) && Number.isFinite(p[1]))
      .map((p) => ({ x: p[0], y: p[1] }))
  } catch {
    return []
  }
}

function addEdge(g: Graph, a: Pt, b: Pt): void {
  const ka = key(a), kb = key(b)
  if (ka === kb) return
  if (!g.nodes.has(ka)) g.nodes.set(ka, a)
  if (!g.nodes.has(kb)) g.nodes.set(kb, b)
  const w = dist(a, b)
  if (!g.adj.has(ka)) g.adj.set(ka, [])
  if (!g.adj.has(kb)) g.adj.set(kb, [])
  if (!g.adj.get(ka)!.some((e) => e.to === kb)) g.adj.get(ka)!.push({ to: kb, w })
  if (!g.adj.get(kb)!.some((e) => e.to === ka)) g.adj.get(kb)!.push({ to: ka, w })
  g.segments.push({ a, b })
}

/** 把全部 Aisle 中心线连成一张图（顶点按 1mm 取整去重，共端点/交叉自动合并）。 */
export function buildCenterlineGraph(aisles: Array<{ centerline: string }>): Graph {
  const g: Graph = { nodes: new Map(), adj: new Map(), segments: [] }
  for (const a of aisles) {
    const v = parseCenterline(a.centerline)
    for (let i = 0; i + 1 < v.length; i++) addEdge(g, v[i]!, v[i + 1]!)
  }
  return g
}

/** 点投影到线段 [a,b]，返回垂足（钳制到段内）与距离。 */
function projectToSegment(p: Pt, a: Pt, b: Pt): { foot: Pt; d: number } {
  const dx = b.x - a.x, dy = b.y - a.y
  const len2 = dx * dx + dy * dy
  let t = len2 === 0 ? 0 : ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2
  t = Math.max(0, Math.min(1, t))
  const foot = { x: a.x + t * dx, y: a.y + t * dy }
  return { foot, d: dist(p, foot) }
}

/** 最近接入点：把库位投影到最近中心线段，返回垂足 + 该段两端点。无段 → null。 */
function nearestAccess(g: Graph, p: Pt): { foot: Pt; segA: Pt; segB: Pt } | null {
  let best: { foot: Pt; segA: Pt; segB: Pt; d: number } | null = null
  for (const s of g.segments) {
    const { foot, d } = projectToSegment(p, s.a, s.b)
    if (!best || d < best.d) best = { foot, segA: s.a, segB: s.b, d }
  }
  return best ? { foot: best.foot, segA: best.segA, segB: best.segB } : null
}

/** Dijkstra（邻接表 + 临时接入节点 FA/FB）。返回 key 序列或 null。 */
function dijkstra(adj: Map<string, Array<{ to: string; w: number }>>, start: string, end: string): string[] | null {
  const dists = new Map<string, number>()
  const prev = new Map<string, string>()
  const visited = new Set<string>()
  dists.set(start, 0)
  // 简单 O(V^2) 选最小（一层巷道节点数十级，足够）
  while (true) {
    let u: string | null = null
    let best = Infinity
    for (const [k, d] of dists) if (!visited.has(k) && d < best) { best = d; u = k }
    if (u === null) break
    if (u === end) break
    visited.add(u)
    for (const e of adj.get(u) ?? []) {
      if (visited.has(e.to)) continue
      const nd = best + e.w
      if (nd < (dists.get(e.to) ?? Infinity)) { dists.set(e.to, nd); prev.set(e.to, u) }
    }
  }
  if (!dists.has(end)) return null
  const path: string[] = []
  let cur: string | undefined = end
  while (cur !== undefined) { path.unshift(cur); cur = prev.get(cur) }
  return path[0] === start ? path : null
}

/** 相邻两拣货点路径：a→接入→沿巷道→接入→b。不连通/无段 → 直连 degraded。 */
function pathBetween(g: Graph, a: Pt, b: Pt): { points: Pt[]; degraded: boolean } {
  const accA = nearestAccess(g, a)
  const accB = nearestAccess(g, b)
  if (!accA || !accB) return { points: [a, b], degraded: true }

  // 临时邻接：克隆 + 接入 FA/FB（连到各自段两端；同段则直连 FA-FB）
  const adj = new Map<string, Array<{ to: string; w: number }>>()
  for (const [k, list] of g.adj) adj.set(k, list.slice())
  const FA = 'FA', FB = 'FB'
  const link = (n: string, p: Pt, segA: Pt, segB: Pt) => {
    adj.set(n, [
      { to: key(segA), w: dist(p, segA) },
      { to: key(segB), w: dist(p, segB) },
    ])
    adj.get(key(segA))!.push({ to: n, w: dist(p, segA) })
    adj.get(key(segB))!.push({ to: n, w: dist(p, segB) })
  }
  link(FA, accA.foot, accA.segA, accA.segB)
  link(FB, accB.foot, accB.segA, accB.segB)
  if (key(accA.segA) === key(accB.segA) && key(accA.segB) === key(accB.segB)) {
    adj.get(FA)!.push({ to: FB, w: dist(accA.foot, accB.foot) })
    adj.get(FB)!.push({ to: FA, w: dist(accA.foot, accB.foot) })
  }

  const nodePt = (k: string): Pt => (k === FA ? accA.foot : k === FB ? accB.foot : g.nodes.get(k)!)
  const path = dijkstra(adj, FA, FB)
  if (!path) return { points: [a, b], degraded: true }

  const mid = path.map(nodePt)
  return { points: [a, ...mid, b], degraded: false }
}

function polyDist(pts: Pt[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += dist(pts[i - 1]!, pts[i]!)
  return d
}

/** 整条拣货路径：依次拼接相邻拣货点（去重接缝点）。 */
export function planPickRoute(aisles: Array<{ centerline: string }>, stops: Pt[]): PlannedRoute {
  if (stops.length < 2) return { points: stops.slice(), totalDistance: 0, degraded: false }
  const g = buildCenterlineGraph(aisles)
  const points: Pt[] = []
  let degraded = false
  for (let i = 0; i + 1 < stops.length; i++) {
    const seg = pathBetween(g, stops[i]!, stops[i + 1]!)
    degraded = degraded || seg.degraded
    const segPts = i === 0 ? seg.points : seg.points.slice(1) // 去掉与上段重合的接缝起点
    points.push(...segPts)
  }
  return { points, totalDistance: polyDist(points), degraded }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PickPathPlanner.ts cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts && git commit -m "feat(space-08): T5 PickPathPlanner 纯逻辑(中心线图+投影接入+Dijkstra+退化直连) + vitest"
```

---

## Task 6：`pathModel.ts` 折线弧长（动画采样）+ vitest

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/pathModel.ts`
- Test: `cp6.web/src/space-viewer/advanced/pathModel.spec.ts`

- [ ] **Step 1: 写失败测试**

```typescript
// cp6.web/src/space-viewer/advanced/pathModel.spec.ts
import { describe, it, expect } from 'vitest'
import { polylineLength, pointAtDistance } from './pathModel'

const L = [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }]

describe('pathModel', () => {
  it('polylineLength sums segments', () => {
    expect(polylineLength(L)).toBe(200)
    expect(polylineLength([{ x: 0, y: 0 }])).toBe(0)
    expect(polylineLength([])).toBe(0)
  })

  it('pointAtDistance walks arc-length and clamps to ends', () => {
    expect(pointAtDistance(L, 0)).toEqual({ x: 0, y: 0 })
    expect(pointAtDistance(L, 50)).toEqual({ x: 50, y: 0 })   // 沿第一段
    expect(pointAtDistance(L, 150)).toEqual({ x: 100, y: 50 }) // 沿第二段
    expect(pointAtDistance(L, 999)).toEqual({ x: 100, y: 100 }) // 超尾 → 末点
    expect(pointAtDistance(L, -5)).toEqual({ x: 0, y: 0 })      // 负 → 首点
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/pathModel.spec.ts`
Expected: 失败（模块不存在）。

- [ ] **Step 3: 实现 `pathModel.ts`**

```typescript
// cp6.web/src/space-viewer/advanced/pathModel.ts
import type { Pt } from './PickPathPlanner'

const seg = (a: Pt, b: Pt): number => Math.hypot(a.x - b.x, a.y - b.y)

export function polylineLength(pts: Pt[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += seg(pts[i - 1]!, pts[i]!)
  return d
}

/** 沿折线弧长取点；d 钳制到 [0, 总长]。 */
export function pointAtDistance(pts: Pt[], d: number): Pt {
  if (pts.length === 0) return { x: 0, y: 0 }
  if (pts.length === 1 || d <= 0) return { ...pts[0]! }
  let remain = d
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1]!, b = pts[i]!
    const l = seg(a, b)
    if (remain <= l) {
      const t = l === 0 ? 0 : remain / l
      return { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t }
    }
    remain -= l
  }
  return { ...pts[pts.length - 1]! }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/pathModel.spec.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/pathModel.ts cp6.web/src/space-viewer/advanced/pathModel.spec.ts && git commit -m "feat(space-08): T6 pathModel 折线弧长(polylineLength/pointAtDistance) + vitest"
```

---

## Task 7：`PathAnimator.ts`（Three 路径线 + 小车 + 播放控制）+ vitest

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/PathAnimator.ts`
- Test: `cp6.web/src/space-viewer/advanced/PathAnimator.spec.ts`

> mesh parent 到 `viewer.getSceneRoot()`（mm 数据空间）。`play()` 用自有 `requestAnimationFrame`（07 as-built：`Loop` 无逐帧回调），每帧推进弧长 + `requestRender()`。测试只验 setPath/clear/stepNext/progress（不触发 RAF，留 gstack 验动画手感）。

- [ ] **Step 1: 写失败测试**

```typescript
// cp6.web/src/space-viewer/advanced/PathAnimator.spec.ts
import { describe, it, expect, vi } from 'vitest'
import { Group } from 'three'
import { PathAnimator } from './PathAnimator'

function fakeViewer() {
  const root = new Group()
  return { root, getSceneRoot: () => root, requestRender: vi.fn() }
}
const L = [{ x: 0, y: 0 }, { x: 1000, y: 0 }, { x: 1000, y: 1000 }]

describe('PathAnimator', () => {
  it('setPath builds line+cart under sceneRoot', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    expect(v.root.children.length).toBe(1)        // 动画 Group 挂上
    expect(v.root.children[0]!.children.length).toBe(2) // line + cart
    expect(v.requestRender).toHaveBeenCalled()
    expect(a.progress).toBe(0)
  })

  it('stepNext advances along the polyline and never exceeds 1', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    a.stepNext()
    expect(a.progress).toBeGreaterThan(0)
    a.stepNext(); a.stepNext(); a.stepNext()
    expect(a.progress).toBeLessThanOrEqual(1)
  })

  it('clear removes the group and resets', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    a.clear()
    expect(v.root.children.length).toBe(0)
    expect(a.progress).toBe(0)
    expect(a.playing).toBe(false)
  })

  it('setPath with <2 points renders nothing', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath([{ x: 1, y: 1 }])
    expect(v.root.children.length).toBe(0)
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PathAnimator.spec.ts`
Expected: 失败（模块不存在）。

- [ ] **Step 3: 实现 `PathAnimator.ts`**

```typescript
// cp6.web/src/space-viewer/advanced/PathAnimator.ts
import {
  BoxGeometry, BufferGeometry, Float32BufferAttribute, Group, Line, LineBasicMaterial,
  Mesh, MeshBasicMaterial,
} from 'three'
import type { ViewerHandle } from '../api/ViewerHandle'
import type { Pt } from './PickPathPlanner'
import { polylineLength, pointAtDistance } from './pathModel'

const GROUND_Z = 200       // mm，路径线离地高度
const CART_SIZE = 600      // mm 立方
const PATH_COLOR = 0x00e5ff
const CART_COLOR = 0xff4081
const DEFAULT_SPEED = 4000 // mm/s

export class PathAnimator {
  private _viewer: ViewerHandle
  private _group = new Group()
  private _points: Pt[] = []
  private _length = 0
  private _cart: Mesh | null = null
  private _dist = 0
  private _speed = DEFAULT_SPEED
  private _playing = false
  private _raf = 0
  private _lastTs = 0

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get playing(): boolean { return this._playing }
  get progress(): number { return this._length > 0 ? Math.min(1, this._dist / this._length) : 0 }

  setPath(points: Pt[]): void {
    this.clear()
    this._points = points.slice()
    this._length = polylineLength(points)
    if (points.length < 2) return

    const arr: number[] = []
    for (const p of points) arr.push(p.x, p.y, GROUND_Z)
    const geom = new BufferGeometry()
    geom.setAttribute('position', new Float32BufferAttribute(arr, 3))
    this._group.add(new Line(geom, new LineBasicMaterial({ color: PATH_COLOR })))

    this._cart = new Mesh(new BoxGeometry(CART_SIZE, CART_SIZE, CART_SIZE), new MeshBasicMaterial({ color: CART_COLOR }))
    this._group.add(this._cart)
    this._dist = 0
    this._positionCart()

    this._viewer.getSceneRoot().add(this._group)
    this._viewer.requestRender()
  }

  private _positionCart(): void {
    if (!this._cart) return
    const p = pointAtDistance(this._points, this._dist)
    this._cart.position.set(p.x, p.y, GROUND_Z + CART_SIZE / 2)
  }

  play(): void {
    if (this._playing || this._points.length < 2) return
    this._playing = true
    this._lastTs = 0
    const tick = (ts: number): void => {
      if (!this._playing) return
      if (this._lastTs === 0) this._lastTs = ts
      const dt = (ts - this._lastTs) / 1000
      this._lastTs = ts
      this._dist += this._speed * dt
      if (this._dist >= this._length) { this._dist = this._length; this._playing = false }
      this._positionCart()
      this._viewer.requestRender()
      if (this._playing) this._raf = requestAnimationFrame(tick)
    }
    this._raf = requestAnimationFrame(tick)
  }

  pause(): void {
    this._playing = false
    if (this._raf) cancelAnimationFrame(this._raf)
    this._raf = 0
  }

  /** 步进到下一折线顶点。 */
  stepNext(): void {
    this.pause()
    let acc = 0
    for (let i = 1; i < this._points.length; i++) {
      acc += Math.hypot(this._points[i]!.x - this._points[i - 1]!.x, this._points[i]!.y - this._points[i - 1]!.y)
      if (acc > this._dist + 1) { this._dist = acc; break }
    }
    if (this._dist > this._length) this._dist = this._length
    this._positionCart()
    this._viewer.requestRender()
  }

  setSpeed(mmPerSec: number): void { this._speed = Math.max(100, mmPerSec) }

  replay(): void {
    this.pause()
    this._dist = 0
    this._positionCart()
    this._viewer.requestRender()
    this.play()
  }

  clear(): void {
    this.pause()
    this._group.clear()
    if (this._group.parent) this._group.parent.remove(this._group)
    this._cart = null
    this._points = []
    this._length = 0
    this._dist = 0
    this._viewer.requestRender()
  }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PathAnimator.spec.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PathAnimator.ts cp6.web/src/space-viewer/advanced/PathAnimator.spec.ts && git commit -m "feat(space-08): T7 PathAnimator(Three路径线+小车+播放/暂停/步进/调速/重播) + vitest"
```

---

## Task 8：`workloadModel.ts` + `WorkloadHeatmap.ts`（07 兄弟类）+ vitest

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/workloadModel.ts`
- Create: `cp6.web/src/space-viewer/advanced/WorkloadHeatmap.ts`
- Test: `cp6.web/src/space-viewer/advanced/workloadModel.spec.ts`
- Test: `cp6.web/src/space-viewer/advanced/WorkloadHeatmap.spec.ts`

> `WorkloadHeatmap` 仿 `StockOverlay`（同 `apply`：遍历 code→`getLocationIdByCode`→`setInstanceColor`→`requestRender`），数据=`{code→opCount}`，色=按最大值归一 → 07 `utilizationToHex`（冷蓝→暖红）。**与 07 模式互斥**（FloorViewer 协调，T10）。

- [ ] **Step 1: 写失败测试（两份）**

```typescript
// cp6.web/src/space-viewer/advanced/workloadModel.spec.ts
import { describe, it, expect } from 'vitest'
import { normalizeOpCounts, workloadToHex } from './workloadModel'
import { utilizationToHex } from '@/space-viewer/overlay/stockModel'

describe('workloadModel', () => {
  it('normalizeOpCounts maps to [0,1] by max', () => {
    const m = normalizeOpCounts([
      { locationCode: 'A', opCount: 10 },
      { locationCode: 'B', opCount: 5 },
      { locationCode: 'C', opCount: 0 },
    ])
    expect(m.get('A')).toBe(1)
    expect(m.get('B')).toBe(0.5)
    expect(m.get('C')).toBe(0)
  })

  it('normalizeOpCounts all-zero → all 0 (no divide-by-zero)', () => {
    const m = normalizeOpCounts([{ locationCode: 'A', opCount: 0 }])
    expect(m.get('A')).toBe(0)
  })

  it('workloadToHex reuses 07 cold→warm ramp', () => {
    expect(workloadToHex(0)).toBe(utilizationToHex(0))
    expect(workloadToHex(1)).toBe(utilizationToHex(1))
  })
})
```

```typescript
// cp6.web/src/space-viewer/advanced/WorkloadHeatmap.spec.ts
import { describe, it, expect, vi } from 'vitest'
import { WorkloadHeatmap } from './WorkloadHeatmap'
import { workloadToHex } from './workloadModel'

function fakeViewer() {
  return {
    getLocationIdByCode: (c: string) => (c === 'GHOST' ? null : `id-${c}`),
    setInstanceColor: vi.fn(),
    requestRender: vi.fn(),
  }
}

describe('WorkloadHeatmap', () => {
  it('apply colors busy locations hot when enabled', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 10 }, { locationCode: 'B', opCount: 5 }])
    h.setEnabled(true)
    h.apply()
    expect(v.setInstanceColor).toHaveBeenCalledWith('id-A', workloadToHex(1))
    expect(v.setInstanceColor).toHaveBeenCalledWith('id-B', workloadToHex(0.5))
    expect(v.requestRender).toHaveBeenCalled()
  })

  it('apply is a no-op when disabled', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 1 }])
    h.apply()
    expect(v.setInstanceColor).not.toHaveBeenCalled()
  })

  it('getOpCount returns raw count by code', () => {
    const v = fakeViewer()
    const h = new WorkloadHeatmap(v as any)
    h.setSnapshot([{ locationCode: 'A', opCount: 7 }])
    expect(h.getOpCount('A')).toBe(7)
    expect(h.getOpCount('GHOST')).toBe(0)
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/workloadModel.spec.ts src/space-viewer/advanced/WorkloadHeatmap.spec.ts`
Expected: 失败（模块不存在）。

- [ ] **Step 3: 实现 `workloadModel.ts`**

```typescript
// cp6.web/src/space-viewer/advanced/workloadModel.ts
import type { WorkloadItem } from '@/types/space/advanced'

/** opCount → 归一化 t∈[0,1]（按最大值线性归一；max=0 → 全 0）。 */
export function normalizeOpCounts(items: WorkloadItem[]): Map<string, number> {
  const max = items.reduce((m, i) => Math.max(m, i.opCount), 0)
  const out = new Map<string, number>()
  for (const i of items) out.set(i.locationCode, max > 0 ? i.opCount / max : 0)
  return out
}

/** 作业热力色：复用 07 冷蓝→暖红渐变管线（DRY）。 */
export { utilizationToHex as workloadToHex } from '@/space-viewer/overlay/stockModel'
```

- [ ] **Step 4: 实现 `WorkloadHeatmap.ts`**

```typescript
// cp6.web/src/space-viewer/advanced/WorkloadHeatmap.ts
import type { ViewerHandle } from '../api/ViewerHandle'
import type { WorkloadItem } from '@/types/space/advanced'
import { advancedApi } from '@/api/space/advanced'
import { normalizeOpCounts, workloadToHex } from './workloadModel'

/** 作业热图叠加：StockOverlay 的兄弟类，换数据源(频次)+色映射(冷暖)。与 07 着色模式互斥(调用方协调)。 */
export class WorkloadHeatmap {
  private _viewer: ViewerHandle
  private _norm = new Map<string, number>()  // code → t[0,1]
  private _raw = new Map<string, number>()   // code → opCount
  private _enabled = false
  private _ts = ''

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get enabled(): boolean { return this._enabled }
  get ts(): string { return this._ts }

  setEnabled(on: boolean): void { this._enabled = on }
  setSnapshot(items: WorkloadItem[], ts = ''): void {
    this._raw = new Map(items.map((i) => [i.locationCode, i.opCount]))
    this._norm = normalizeOpCounts(items)
    this._ts = ts
  }
  getOpCount(code: string | null): number {
    return code ? (this._raw.get(code) ?? 0) : 0
  }

  apply(): void {
    if (!this._enabled) return
    for (const [code, t] of this._norm) {
      const id = this._viewer.getLocationIdByCode(code)
      if (!id) continue
      this._viewer.setInstanceColor(id, workloadToHex(t))
    }
    this._viewer.requestRender()
  }

  async refresh(floorId: string, from: string, to: string): Promise<void> {
    const env = await advancedApi.workload(floorId, from, to)
    this.setSnapshot(env.data.items, env.data.to)
    this.apply()
  }

  dispose(): void { this._norm.clear(); this._raw.clear(); this._enabled = false }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/workloadModel.spec.ts src/space-viewer/advanced/WorkloadHeatmap.spec.ts`
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/workloadModel.ts cp6.web/src/space-viewer/advanced/WorkloadHeatmap.ts cp6.web/src/space-viewer/advanced/workloadModel.spec.ts cp6.web/src/space-viewer/advanced/WorkloadHeatmap.spec.ts && git commit -m "feat(space-08): T8 workloadModel(归一+复用07渐变) + WorkloadHeatmap(StockOverlay兄弟类) + vitest"
```

---

## Task 9：`DeviceLayer.ts`（SceneRoot 挂点占位）+ vitest

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/DeviceLayer.ts`
- Test: `cp6.web/src/space-viewer/advanced/DeviceLayer.spec.ts`

> v1 占位：把有坐标的设备渲染成参数化盒体挂 SceneRoot；无坐标跳过。WMS 桩返空 → 空图层（演示时 FloorViewer 弹 I-SPACE-803「示意，未接实时」）。

- [ ] **Step 1: 写失败测试**

```typescript
// cp6.web/src/space-viewer/advanced/DeviceLayer.spec.ts
import { describe, it, expect, vi } from 'vitest'
import { Group } from 'three'
import { DeviceLayer } from './DeviceLayer'
import type { DeviceDto } from '@/types/space/advanced'

function fakeViewer() {
  const root = new Group()
  return { root, getSceneRoot: () => root, requestRender: vi.fn() }
}
const dev = (id: string, x: number | null): DeviceDto =>
  ({ deviceId: id, type: 'AGV', status: 0, locationCode: 'A-01', absX: x, absY: x, absZ: 500 })

describe('DeviceLayer', () => {
  it('setDevices renders boxes for devices with coords', () => {
    const v = fakeViewer()
    const dl = new DeviceLayer(v as any)
    dl.setDevices([dev('D1', 100), dev('D2', 200)])
    expect(dl.count).toBe(2)
    expect(v.root.children.length).toBe(1)             // 设备 Group
    expect(v.root.children[0]!.children.length).toBe(2) // 2 盒
    expect(v.requestRender).toHaveBeenCalled()
  })

  it('skips devices without coords', () => {
    const v = fakeViewer()
    const dl = new DeviceLayer(v as any)
    dl.setDevices([dev('D1', null)])
    expect(dl.count).toBe(0)
    expect(v.root.children.length).toBe(0)
  })

  it('clear removes the group and resets count', () => {
    const v = fakeViewer()
    const dl = new DeviceLayer(v as any)
    dl.setDevices([dev('D1', 100)])
    dl.clear()
    expect(dl.count).toBe(0)
    expect(v.root.children.length).toBe(0)
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/DeviceLayer.spec.ts`
Expected: 失败（模块不存在）。

- [ ] **Step 3: 实现 `DeviceLayer.ts`**

```typescript
// cp6.web/src/space-viewer/advanced/DeviceLayer.ts
import { BoxGeometry, Group, Mesh, MeshBasicMaterial } from 'three'
import type { ViewerHandle } from '../api/ViewerHandle'
import type { DeviceDto } from '@/types/space/advanced'

const DEVICE_SIZE = 1000   // mm
const DEVICE_COLOR = 0x7e57c2
const DEVICE_Z = 500       // 缺 absZ 时的默认高度

/** 设备图层 v1 占位：参数化盒体挂 SceneRoot（mm 数据空间）；真接实时流 = P3+。 */
export class DeviceLayer {
  private _viewer: ViewerHandle
  private _group = new Group()
  private _count = 0

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get count(): number { return this._count }

  setDevices(devices: DeviceDto[]): void {
    this.clear()
    for (const d of devices) {
      if (d.absX == null || d.absY == null) continue
      const mesh = new Mesh(
        new BoxGeometry(DEVICE_SIZE, DEVICE_SIZE, DEVICE_SIZE),
        new MeshBasicMaterial({ color: DEVICE_COLOR }),
      )
      mesh.position.set(d.absX, d.absY, d.absZ ?? DEVICE_Z)
      this._group.add(mesh)
      this._count++
    }
    if (this._count > 0) {
      this._viewer.getSceneRoot().add(this._group)
    }
    this._viewer.requestRender()
  }

  clear(): void {
    this._group.clear()
    if (this._group.parent) this._group.parent.remove(this._group)
    this._count = 0
    this._viewer.requestRender()
  }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/DeviceLayer.spec.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/DeviceLayer.ts cp6.web/src/space-viewer/advanced/DeviceLayer.spec.ts && git commit -m "feat(space-08): T9 DeviceLayer 占位(参数化盒体挂SceneRoot) + vitest"
```

---

## Task 10：`AdvancedPanel.vue` 控件 + 接线 `FloorViewer.vue`

**Files:**
- Create: `cp6.web/src/views/space/viewer/AdvancedPanel.vue`
- Modify: `cp6.web/src/views/space/viewer/FloorViewer.vue`

> Panel 纯展示 + emit（视觉态留 gstack）。FloorViewer 实例化三模块、协调热图与 07 互斥、楼层切换/卸载时清理。

- [ ] **Step 1: 建 `AdvancedPanel.vue`**

```vue
<!-- cp6.web/src/views/space/viewer/AdvancedPanel.vue -->
<template>
  <div class="advanced-panel">
    <div class="ap-section">
      <div class="ap-title">{{ t('拣货路径') }}</div>
      <div class="ap-row">
        <input v-model="taskNo" class="ap-input" :placeholder="t('拣货单号')" />
        <button class="ap-btn" @click="$emit('load-path', taskNo)">{{ t('加载') }}</button>
      </div>
      <div class="ap-row" v-if="pathLoaded">
        <button class="ap-btn" @click="$emit('play')">▶</button>
        <button class="ap-btn" @click="$emit('pause')">⏸</button>
        <button class="ap-btn" @click="$emit('step')">⏭</button>
        <button class="ap-btn" @click="$emit('replay')">↺</button>
        <select class="ap-input" @change="onSpeed">
          <option value="2000">0.5x</option>
          <option value="4000" selected>1x</option>
          <option value="8000">2x</option>
        </select>
      </div>
      <div class="ap-info" v-if="pathInfo">{{ pathInfo }}</div>
    </div>

    <div class="ap-section">
      <div class="ap-title">{{ t('作业热图') }}</div>
      <div class="ap-row">
        <label class="ap-check"><input type="checkbox" :checked="workloadOn" @change="$emit('toggle-workload')" />{{ t('开启') }}</label>
        <input type="date" v-model="from" class="ap-input" />
        <input type="date" v-model="to" class="ap-input" />
        <button class="ap-btn" @click="$emit('apply-workload', { from, to })">{{ t('应用') }}</button>
      </div>
    </div>

    <div class="ap-section">
      <div class="ap-title">{{ t('设备示意') }}</div>
      <label class="ap-check"><input type="checkbox" :checked="deviceOn" @change="$emit('toggle-device')" />{{ t('显示设备') }}</label>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
defineProps<{ pathLoaded: boolean; pathInfo: string; workloadOn: boolean; deviceOn: boolean }>()
const emit = defineEmits<{
  (e: 'load-path', taskNo: string): void
  (e: 'play'): void; (e: 'pause'): void; (e: 'step'): void; (e: 'replay'): void
  (e: 'speed', v: number): void
  (e: 'toggle-workload'): void
  (e: 'apply-workload', win: { from: string; to: string }): void
  (e: 'toggle-device'): void
}>()
const taskNo = ref('')
const today = new Date().toISOString().slice(0, 10)
const from = ref(today)
const to = ref(today)
function onSpeed(ev: Event): void {
  emit('speed', Number((ev.target as HTMLSelectElement).value))
}
</script>

<style scoped>
.advanced-panel { position: absolute; right: 16px; bottom: 16px; background: rgba(12,12,28,.92);
  border: 1px solid rgba(126,87,194,.4); border-radius: 6px; color: #e0e0e0; font-size: 12px; padding: 8px 10px; z-index: 10; width: 240px; }
.ap-section { margin-bottom: 8px; }
.ap-title { color: #b39ddb; font-weight: 600; margin-bottom: 4px; }
.ap-row { display: flex; align-items: center; gap: 4px; flex-wrap: wrap; margin-bottom: 4px; }
.ap-input { background: #1a1a2e; color: #e0e0e0; border: 1px solid #37474f; border-radius: 4px; padding: 2px 4px; width: 70px; }
.ap-btn { background: transparent; color: #b39ddb; border: 1px solid #5e35b1; border-radius: 4px; cursor: pointer; padding: 2px 6px; }
.ap-btn:hover { background: rgba(126,87,194,.2); }
.ap-check { display: flex; align-items: center; gap: 4px; }
.ap-info { color: #80cbc4; margin-top: 2px; }
</style>
```

- [ ] **Step 2: 类型校验（Panel 先单独过）**

Run: `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit`
Expected: 0 error（AdvancedPanel 未接线也应类型自洽）。

- [ ] **Step 3: 接线 `FloorViewer.vue` —— import + 模板**

在 `<script setup>` import 区（07 已有 StockOverlay/stockApi import 之后）追加：
```typescript
import { PathAnimator } from '@/space-viewer/advanced/PathAnimator'
import { WorkloadHeatmap } from '@/space-viewer/advanced/WorkloadHeatmap'
import { DeviceLayer } from '@/space-viewer/advanced/DeviceLayer'
import { planPickRoute, type Pt } from '@/space-viewer/advanced/PickPathPlanner'
import { advancedApi } from '@/api/space/advanced'
import AdvancedPanel from './AdvancedPanel.vue'
```

在 `<template>` 的 `<StockLegend ... />` 之后插入：
```vue
      <!-- Advanced panel (bottom-right): pick-path / workload / devices -->
      <AdvancedPanel
        :path-loaded="pathLoaded"
        :path-info="pathInfo"
        :workload-on="workloadOn"
        :device-on="deviceOn"
        @load-path="onLoadPath"
        @play="pathAnimator?.play()"
        @pause="pathAnimator?.pause()"
        @step="pathAnimator?.stepNext()"
        @replay="pathAnimator?.replay()"
        @speed="(v: number) => pathAnimator?.setSpeed(v)"
        @toggle-workload="onToggleWorkload"
        @apply-workload="onApplyWorkload"
        @toggle-device="onToggleDevice"
      />
```

- [ ] **Step 4: 接线 `FloorViewer.vue` —— 状态 + 模块实例 + 方法**

在 `<script setup>` 的状态声明区（`selectedStock` 之后）追加：
```typescript
let pathAnimator: PathAnimator | null = null
let heatmap: WorkloadHeatmap | null = null
let deviceLayer: DeviceLayer | null = null

const pathLoaded = ref(false)
const pathInfo = ref('')
const workloadOn = ref(false)
const deviceOn = ref(false)
let workloadWin = { from: new Date().toISOString().slice(0, 10), to: new Date().toISOString().slice(0, 10) }
```

在 `onMounted` 内 `overlay = new StockOverlay(...)` 之后追加（同 cast 手法）：
```typescript
  const vh = viewer as unknown as import('@/space-viewer/api/ViewerHandle').ViewerHandle
  pathAnimator = new PathAnimator(vh)
  heatmap = new WorkloadHeatmap(vh)
  deviceLayer = new DeviceLayer(vh)
```

在 `onLocateMaterial` 之后追加方法：
```typescript
async function onLoadPath(taskNo: string): Promise<void> {
  if (!taskNo || !pathAnimator) return
  try {
    const env = await advancedApi.pickPath(currentFloorId.value, taskNo)
    const data = env.data
    const stopPts: Pt[] = data.stops
      .filter((s) => s.absX != null && s.absY != null)
      .map((s) => ({ x: s.absX as number, y: s.absY as number }))
    if (stopPts.length < 2) { ElMessage.info(t('该拣货单无可定位拣货点')); return }
    const route = planPickRoute(data.aisles, stopPts)
    pathAnimator.setPath(route.points)
    pathLoaded.value = true
    pathInfo.value = t('拣货路径：{n} 点，总距 {d} 米')
      .replace('{n}', String(stopPts.length))
      .replace('{d}', (route.totalDistance / 1000).toFixed(1))   // I-SPACE-801
    if (route.degraded) ElMessage.warning(t('巷道路径不连通，近似直连显示'))  // W-SPACE-801
  } catch {
    ElMessage.warning(t('高级可视化数据获取失败'))   // W-SPACE-802
  }
}

async function onToggleWorkload(): Promise<void> {
  if (!heatmap || !viewer) return
  workloadOn.value = !workloadOn.value
  if (workloadOn.value) {
    overlayMode.value = 'off'            // 与 07 着色互斥
    await viewer.load(currentFloorId.value)  // 复位为默认灰（不重叠 07 着色）
    heatmap.setEnabled(true)
    await heatmap.refresh(currentFloorId.value, workloadWin.from, workloadWin.to)
    ElMessage.info(t('作业热图（时间窗 {f}~{t}）已加载').replace('{f}', workloadWin.from).replace('{t}', workloadWin.to)) // I-SPACE-802
  } else {
    heatmap.setEnabled(false)
    await loadFloor(currentFloorId.value)  // 复位 + 还原 07 库存着色
  }
}

async function onApplyWorkload(win: { from: string; to: string }): Promise<void> {
  workloadWin = win
  if (workloadOn.value && heatmap) {
    await heatmap.refresh(currentFloorId.value, win.from, win.to)
  }
}

async function onToggleDevice(): Promise<void> {
  if (!deviceLayer) return
  deviceOn.value = !deviceOn.value
  if (deviceOn.value) {
    try {
      const env = await advancedApi.devices(currentFloorId.value)
      deviceLayer.setDevices(env.data)
      ElMessage.info(t('设备联动为演示示意（未接实时）'))   // I-SPACE-803
    } catch {
      ElMessage.warning(t('高级可视化数据获取失败'))
    }
  } else {
    deviceLayer.clear()
  }
}
```

在 `loadFloor` 函数开头（`selectedId.value = null` 之后）追加：切层先清理路径/设备（热图随 loadFloor 自然复位）：
```typescript
  pathAnimator?.clear()
  pathLoaded.value = false
  pathInfo.value = ''
  deviceLayer?.clear()
  deviceOn.value = false
```

在 `onBeforeUnmount` 内（`overlay?.dispose()` 之后）追加：
```typescript
  pathAnimator?.clear(); pathAnimator = null
  heatmap?.dispose(); heatmap = null
  deviceLayer?.clear(); deviceLayer = null
```

- [ ] **Step 5: 三门校验（vue-tsc + vitest + build）**

Run: `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit && npx vitest run && npm run build`
Expected: vue-tsc 0 error；vitest 全绿（126 + 08 新增约 20 例）；build 成功。

- [ ] **Step 6: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/views/space/viewer/AdvancedPanel.vue cp6.web/src/views/space/viewer/FloorViewer.vue && git commit -m "feat(space-08): T10 AdvancedPanel 控件 + FloorViewer 接线(路径/热图/设备+楼层切换清理+与07互斥)"
```

---

# Part C — 演示种子 + gstack QA

## Task 11：08 演示种子（出库单 + 流水 + 中心线）

**Files:**
- Create: `docs/superpowers/qa/space-p2-08/seed.sql`

> 隔离库 `CP6DB_SpaceQA`（localhost\KOUSQLSERVER，Windows 认证）。真实已发布编码 = `A-01-01-01/A-01-01-02/A-01-02-01/A-01-02-02`，Floor `5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F`。种子幂等（先 DELETE 再 INSERT）。**纯 ASCII**（sqlcmd `-Q` 传中文乱码）；`[LineNo]` 保留字加括号。中心线数据驱动（按 4 库位 AbsXY 算），无 Aisle 则插一条。

- [ ] **Step 1: 写 `seed.sql`**

```sql
-- Space 08 advanced-viz demo seed (CP6DB_SpaceQA). ASCII only. Idempotent.
SET NOCOUNT ON;
DECLARE @floor uniqueidentifier = '5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F';
DECLARE @tenant uniqueidentifier =
  (SELECT TOP 1 TenantId FROM Space_Location WHERE FloorId=@floor AND LocationCode='A-01-01-01');
DECLARE @wh nvarchar(10) = N'QAWH';
DECLARE @ob nvarchar(20) = N'OB-PICK-DEMO';

IF @tenant IS NULL BEGIN PRINT 'NO TENANT - check floor/codes'; RETURN; END;

-- 1) Pick order with 4 ordered lines across the 4 real codes -----------------
DELETE FROM T_OutboundOrderDetail WHERE OutboundNo=@ob;
DELETE FROM T_OutboundOrder       WHERE OutboundNo=@ob;

INSERT INTO T_OutboundOrder (Id, TenantId, IsDeleted, CreateDate, OutboundNo, OutboundType, WarehouseCd, PlannedDate, Status, Priority)
VALUES (NEWID(), @tenant, 0, GETDATE(), @ob, 2, @wh, GETDATE(), 3, 1);

INSERT INTO T_OutboundOrderDetail (Id, TenantId, IsDeleted, CreateDate, OutboundNo, [LineNo], ProductCd, RequiredQty, AllocatedQty, ShippedQty, LocationCd)
VALUES
 (NEWID(), @tenant, 0, GETDATE(), @ob, 1, N'CARTON-A4', 10, 10, 0, N'A-01-01-01'),
 (NEWID(), @tenant, 0, GETDATE(), @ob, 2, N'CARTON-A4',  5,  5, 0, N'A-01-01-02'),
 (NEWID(), @tenant, 0, GETDATE(), @ob, 3, N'CARTON-A4',  8,  8, 0, N'A-01-02-01'),
 (NEWID(), @tenant, 0, GETDATE(), @ob, 4, N'CARTON-A4',  3,  3, 0, N'A-01-02-02');

-- 2) Stock transactions (workload heatmap): 5/3/1/2 ops today ----------------
DELETE FROM T_StockTransaction WHERE TxnNo LIKE N'TXN-DEMO-%';
DECLARE @now datetime = GETDATE();
INSERT INTO T_StockTransaction (Id, TenantId, IsDeleted, CreateDate, TxnNo, TxnType, TxnDateTime, WarehouseCd, LocationCd, ProductCd, LotNo, Qty)
VALUES
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0001', N'OUT', @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0002', N'OUT', @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0003', N'OUT', @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0004', N'IN',  @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0005', N'OUT', @now, @wh, N'A-01-01-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0006', N'OUT', @now, @wh, N'A-01-01-02', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0007', N'OUT', @now, @wh, N'A-01-01-02', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0008', N'IN',  @now, @wh, N'A-01-01-02', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0009', N'OUT', @now, @wh, N'A-01-02-01', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0010', N'OUT', @now, @wh, N'A-01-02-02', N'CARTON-A4', N'', 1),
 (NEWID(), @tenant, 0, @now, N'TXN-DEMO-0011', N'OUT', @now, @wh, N'A-01-02-02', N'CARTON-A4', N'', 1);

-- 3) Aisle centerline (data-driven from the 4 codes' AbsXY) -------------------
DECLARE @cy int = (SELECT AVG(AbsY) FROM Space_Location
  WHERE FloorId=@floor AND LocationCode IN (N'A-01-01-01',N'A-01-01-02',N'A-01-02-01',N'A-01-02-02') AND AbsY IS NOT NULL);
DECLARE @minx int = (SELECT MIN(AbsX)-1000 FROM Space_Location
  WHERE FloorId=@floor AND LocationCode IN (N'A-01-01-01',N'A-01-01-02',N'A-01-02-01',N'A-01-02-02') AND AbsX IS NOT NULL);
DECLARE @maxx int = (SELECT MAX(AbsX)+1000 FROM Space_Location
  WHERE FloorId=@floor AND LocationCode IN (N'A-01-01-01',N'A-01-01-02',N'A-01-02-01',N'A-01-02-02') AND AbsX IS NOT NULL);
DECLARE @line nvarchar(200) =
  N'[[' + CAST(ISNULL(@minx,0) AS nvarchar(20)) + N',' + CAST(ISNULL(@cy,0) AS nvarchar(20)) + N'],['
        + CAST(ISNULL(@maxx,10000) AS nvarchar(20)) + N',' + CAST(ISNULL(@cy,0) AS nvarchar(20)) + N']]';

-- update existing aisles of this floor whose centerline is empty
UPDATE a SET Centerline=@line
FROM Space_Aisle a JOIN Space_Zone z ON a.ZoneId=z.Id
WHERE z.FloorId=@floor AND (a.Centerline IS NULL OR a.Centerline=N'' OR a.Centerline=N'[]');

-- if floor has no aisle at all, insert one on its first zone
IF NOT EXISTS (SELECT 1 FROM Space_Aisle a JOIN Space_Zone z ON a.ZoneId=z.Id WHERE z.FloorId=@floor)
BEGIN
  DECLARE @zone uniqueidentifier = (SELECT TOP 1 Id FROM Space_Zone WHERE FloorId=@floor ORDER BY ZoneCode);
  IF @zone IS NOT NULL
    INSERT INTO Space_Aisle (Id, TenantId, IsDeleted, CreateDate, ZoneId, AisleCode, Polygon, Centerline)
    VALUES (NEWID(), @tenant, 0, GETDATE(), @zone, N'AISLE-DEMO', N'[]', @line);
END;

PRINT 'space-08 seed done';
```

- [ ] **Step 2: 跑种子（用户监督；若 QA 库已起）**

Run（PowerShell；sqlcmd 路径见 as-built）：
```
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'localhost\KOUSQLSERVER' -d CP6DB_SpaceQA -E -i 'D:\CP6-space-backend\docs\superpowers\qa\space-p2-08\seed.sql'
```
Expected: 输出 `space-08 seed done`（若 `NO TENANT` → floorId/编码与库不符，按当前库重灌的 Space 数据调整 @floor）。

> **注**：QA 库每次重灌演示数据后 Floor/Site GUID 可能变（见 07 QA 记录）。跑前用 `SELECT TOP 5 FloorId, LocationCode FROM Space_Location WHERE Status=1 ORDER BY LocationCode` 核对 @floor 与 4 编码。

- [ ] **Step 3: Commit**

```bash
cd /d/CP6-space-backend && git add docs/superpowers/qa/space-p2-08/seed.sql && git commit -m "test(space-08): T11 演示种子(出库单4序明细+流水频次+数据驱动中心线)"
```

---

## Task 12：gstack 真浏览器 QA + 固化

**Files:**
- Create: `docs/superpowers/qa/space-p2-08/README.md`（QA 记录 + 截图引用）

> 用 gstack（headless Chromium）跑端到端。环境（07 留下，可能仍在跑，可复用或重起）：后端 5177（读 `appsettings.Local.json`→`CP6DB_SpaceQA`）、前端 5173（vite proxy→5177）。登录 admin/123456（`POST /api/auth/login` {userName,password}，dev Csrf 关）。viewer 需 `?floorId=`。**单测覆盖不到的集成 bug（如 07 抓到 4 个）在此修，每修一个单独 commit。**

- [ ] **Step 1: 起环境（若未在跑）**

后端：`cd /d/CP6-space-backend && dotnet run --project CP6.WebApi`（后台）。前端：`cd /d/CP6-space-backend/cp6.web && npm run dev`（后台）。确认 `appsettings.Local.json` 指向 `CP6DB_SpaceQA`。

- [ ] **Step 2: 核对 floorId + 跑种子**

用 sqlcmd 查 `SELECT TOP 1 FloorId FROM Space_Location WHERE LocationCode='A-01-01-01' AND Status=1`，必要时改 seed.sql 的 @floor，跑 Task 11 Step 2。

- [ ] **Step 3: gstack QA 脚本（用 browse skill）**

用 `gstack`/`browse` skill 依次验证（每步截图）：
1. 登录 → 打开 `/space/viewer/{siteId}?floorId={floorId}`，确认 3D 场景渲染（zone 多边形 + rack/库位盒）。
2. **拣货路径**：AdvancedPanel 输入 `OB-PICK-DEMO` → 点「加载」→ 确认出现青色路径线 + 粉色小车；点 ▶ 播放 → 小车沿线移动；面板显「拣货路径：4 点，总距 X 米」。**截图**。
   - el-input/原生 input 用 `browse click + type`（非 `fill`，07 实测 fill 不触发 input 事件）。
3. **作业热图**：勾选「作业热图开启」→ 确认 4 库位按频次冷暖着色（A-01-01-01 最热=红/暖，A-01-02-01 最冷=蓝）；toast「作业热图…已加载」。**截图**。点击某库位 → InfoCard 仍可见（热图与选中不冲突）。
4. **设备示意**：勾选「显示设备」→ toast「设备联动为演示示意（未接实时）」（v1 桩返空，无盒为正常）。**截图**。
5. 切层 → 确认路径/小车/设备被清理（不残留）。
6. 关热图 → 确认恢复 07 库存状态着色（不残留热图色）。

- [ ] **Step 4: 修集成 bug（如有）**

对每个发现的 bug：systematic-debugging 定位 → 最小修 → 单独 commit `fix(space-08): gstack QA - <症状>`。常见疑点：
- floorId/编码时序（07 抓到过 `GET /floor//...` 404 → 确认方法内 `currentFloorId.value` 已赋值再调）。
- code↔GUID：热图 `getLocationIdByCode` 已封装；若盒不着色，查 viewer `_codeToId` 是否含这些编码（须库位盒已渲染——07 修过 `placed` 字段缺失致盒从不渲染）。
- 路径坐标：小车若飞出楼层，查 stops 的 absX/absY 是否 mm（应是）+ 是否误用 dataToWorld（不该——parent 到 sceneRoot 已含变换）。

- [ ] **Step 5: 全回归复核**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests` 和 `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit && npx vitest run && npm run build`
Expected: 后端全绿、前端三门绿（含任何 QA 修复后）。

- [ ] **Step 6: 固化 + Commit**

写 `docs/superpowers/qa/space-p2-08/README.md`：环境、步骤、结论、截图清单、抓到/修复的 bug 列表、已知运行态留点（动画手感/相机跟随/热图像素色肉眼核——同 07 靠 API+InfoCard+单测闭环证）。
```bash
cd /d/CP6-space-backend && git add docs/superpowers/qa/space-p2-08/ && git commit -m "test(space-08): T12 gstack 真浏览器 QA 通过 + 证据固化"
```

---

## Self-Review（写完对照 spec §4 / 丛书 08）

**Spec §4 覆盖：**
- §4.1 三契约（IWmsPickTaskQuery/IWmsWorkloadQuery/IWmsDeviceQuery + DTO）→ Task 1（契约）+ T2/T3（实现）。✅
- §4.2 拣货路径做实（出库单序列 + 中心线图 Dijkstra + 动画 + 退化 W-SPACE-801 + I-SPACE-801 + 按需渲染）→ T1（源）+ T3（AbsXYZ/aisle 打包）+ T5（图+Dijkstra+退化）+ T6（弧长）+ T7（动画）+ T10（接线 + I/W-801）。✅
- §4.3 作业热图做实（流水计次 + 复用 07 着色 + I-SPACE-802）→ T2（源）+ T8（归一+兄弟类）+ T10（接线 + I-802 + 与 07 互斥）。✅
- §4.4 设备 v1 占位（桩返空 + DeviceLayer 挂点 + I-SPACE-803）→ T3（桩）+ T9（图层）+ T10（接线 + I-803）。✅
- §4.5 三中转端点 → T3。✅
- §6 多租户（全局过滤）→ T2 含跨租户隔离测试。✅
- §7 测试与 QA（单测 + gstack 接真种子）→ T1/T2 单测、T5~T9 vitest、T11 种子、T12 gstack。✅
- §9 消息码 W/I-SPACE-801/802/803、W-802 → T10 各 toast 落位。✅

**丛书 08 关键点：** 路径必走 Aisle 中心线（T5 投影 + Dijkstra，不直连）；不连通降级（T5 degraded + T10 W-801）；热图与 07 利用率区别（不同数据源/色映射，同着色管线，T8 复用 utilizationToHex）；设备占位留挂点不砍（T9）；全只读不影响 P1/P2（catch 降级 toast，T10）。✅

**Placeholder / 一致性扫描：** 无 TBD/TODO；类型贯穿一致——后端 `PickStop{Seq,LocationCode,Qty,MaterialNo}` / 前端 `PickStopVO`（+absXYZ）/ `Pt{x,y}`（planner+pathModel+animator 同源）/ `WorkloadItem{locationCode,opCount}`（types↔model↔heatmap↔后端 WorkloadDto 驼峰映射）；方法名一致：`planPickRoute`/`setPath`/`stepNext`/`setEnabled`/`getOpCount`/`setDevices` 在定义与调用处吻合；`getSceneRoot`/`getLocationIdByCode`/`setInstanceColor`/`requestRender` 均属既有 `ViewerHandle`（已亲验）。✅

---

## 执行顺序与依赖

Part A（T1→T2→T3，后端契约+接真，串行）→ Part B（T4 类型先；T5/T6/T7 路径链、T8 热图、T9 设备 可并行；T10 接线收口，依赖 T4~T9）→ Part C（T11 种子 → T12 QA，依赖全部）。每 Task TDD 红→绿→commit；Part A 末 + T10 末跑全量回归。
