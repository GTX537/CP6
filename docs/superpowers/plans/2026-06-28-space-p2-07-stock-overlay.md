# Space P2 · 07 实时库存叠加（数据底座）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 P1（05/06 渲染+定位）之上叠加 WMS 实时库存：扩 `IWmsStockQuery` 批量+反查契约并**接真**（替掉 StubWmsStockQuery），Space 后端 `/stock`、`/stock/locate` 中转端点，前端 overlay 状态着色 / 库容利用率 / 按物料定位 / 信息卡叠库存 / 按需快照+可选轮询，配演示种子 + gstack QA。

**Architecture:** 三层中 L2 应用层只读：契约定义在消费者（Space）侧 `CP6.Core/Services/Integration/`，WMS 接真实现 `CP6.Core/Services/Wms/WmsStockQuery.cs`，Space 后端 `Controllers/Space/SpaceStockController.cs` 中转，前端 `cp6.web/src/space-viewer/overlay/` 复用 05 `ViewerHandle.setInstanceColor` 着色、06 `Locator` 定位。**零改 WMS 库存写入**（纯读）；多租户走 `CP6Context` 全局过滤（服务构造只注 `CP6Context`，查询不写 `.Where(TenantId==)`）。

**Tech Stack:** .NET 8 / EF Core（SqlServer 运行期 + InMemory 测试）/ xUnit（`CP6.Tests`）；Vue 3 + TS + Element Plus + Pinia + Three.js / Vite / Vitest（jsdom，纯逻辑单测）。后端启动项目 `CP6.WebApi`，DbContext+迁移在 `CP6.Core`。

**配套 spec（落码前必读）：**
- `docs/superpowers/specs/2026-06-28-space-p2-p3-stock-overlay-advanced-viz-reconcile-design.md`（本计划落其 §3 = 07）
- 设计源：`docs/space/07-stock-overlay.md`（07 详规）

---

## 通用约定

- **测试基线**：`dotnet test CP6.Tests`（P1 末 1287 测 / 5 skip）。每 Task 末跑相关测试；Part A 收尾跑全量。
- **兼容硬闸**：本计划**零改 WMS 既有服务行为**（只读查询）。`IWmsStockQuery.GetStockQtyAsync` 由 `int`→`decimal`（04 唯一调用方 `if(qty>0)` 兼容）。`dotnet test CP6.Tests --filter "FullyQualifiedName~LocationPublish"` 任一既有测试转红 = 兼容破坏，回退排查。
- **测试 DB 工厂**（沿用 `SpaceLocateServiceTests`）：
  ```csharp
  private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
  private static IWmsStockQuery Stock(CP6Context db) => new WmsStockQuery(db);
  ```
- **WMS 实体 DbSet 名**：`db.Stocks` / `db.Locations` / `db.OutboundOrders` / `db.OutboundOrderDetails`（均继承 `BaseBizEntity`，SaveChanges 自动盖 TenantId）。
- **状态常量**：`OutboundOrderStatus.Picking == 3`（`CP6.Entity.DomainModels.Wms.WmsTxnType.cs`）；`StockQcStatus.Pending`、`StockOwnerType.Self`（Stock 必填字段播种用）。
- **错误/消息码**：07 多为前端展示（`W-/I-SPACE-7xx`）。后端裸码沿用 `E-SPACE-xxx` throw `InvalidOperationException`。
- **前端 http**：`http.get<unknown, Envelope<T>>(...)` 直接返回 `Envelope<T>`（拦截器已 unwrap `response.data`）；`Envelope<T> = { code; message; data }`（`types/space/scene.ts`）。
- **commit**：每 Task 末本地 commit（不 push；push 由用户自跑）。
- **分支/worktree**：本计划在 worktree `D:\CP6-space-backend` @ `feat/space-p1-backend`。

---

## File Structure（先锁分解）

**后端修改：**
- `CP6.Core/Services/Integration/IWmsStockQuery.cs` — 扩接口（批量+反查）+ DTO records；`GetStockQtyAsync` 改 decimal；`StubWmsStockQuery` 补新方法（返空/0）
- `CP6.Core/Services/Space/LocationPublishService.cs` — `GetStockQtyAsync` 调用零改（decimal 兼容，仅确认）
- `CP6.WebApi/Program.cs:374` — DI `StubWmsStockQuery` → `WmsStockQuery`

**后端新建：**
- `CP6.Core/Services/Wms/WmsStockQuery.cs` — 接真实现（批量聚合 + BinStatus 派生 + 反查）
- `CP6.WebApi/Controllers/Space/SpaceStockController.cs` — `/stock`、`/stock/locate` 端点

**后端测试（`CP6.Tests/`）：**
- `WmsStockQueryTests.cs` — 批量/5态派生/优先级/反查/04兼容/多租户

**前端新建：**
- `cp6.web/src/types/space/overlay.ts` — `WmsStockDto`/`WmsLocationHit`/`StockSnapshot`/`OverlayMode`
- `cp6.web/src/api/space/stock.ts` — `stockApi.floorStock` / `stockApi.locate`
- `cp6.web/src/space-viewer/overlay/stockModel.ts` — 纯逻辑：`binStatusToHex` / `utilizationToHex` / `locationUtilization` / `aggregateUtilization`（+ `stockModel.spec.ts`）
- `cp6.web/src/space-viewer/overlay/StockOverlay.ts` — 持快照 + 三模式着色 + 刷新/轮询（+ `StockOverlay.spec.ts`）
- `cp6.web/src/views/space/viewer/StockLegend.vue` — 图例 + 模式切换 + 刷新/轮询开关

**前端修改：**
- `cp6.web/src/views/space/viewer/FloorViewer.vue` — 接 overlay：实例化/工具栏图例/刷新/物料定位/把库存喂 InfoCard
- `cp6.web/src/views/space/viewer/InfoCard.vue` — 叠库存行（状态/量/容量/利用率/主物料 + 数据时间戳）
- `cp6.web/src/views/space/viewer/SearchBox.vue` — 增"按物料"模式
- `cp6.web/src/space-viewer/navigate/Locator.ts` — 复用既有 `locate(code)`（物料定位多命中循环调）

---

# Part A — 后端契约 + 接真

## Task 1：扩 `IWmsStockQuery` 契约 + DTO + Stub 更新（建类型 + 04 兼容）

**Files:**
- Modify: `CP6.Core/Services/Integration/IWmsStockQuery.cs`
- Test: `CP6.Tests/WmsStockQueryTests.cs`（建文件，先放 Stub 兼容一例）

- [ ] **Step 1: 写失败测试**

`CP6.Tests/WmsStockQueryTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class WmsStockQueryTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static IWmsStockQuery Stock(CP6Context db) => new WmsStockQuery(db);

    [Fact]
    public async Task GetStockQtyAsync_SumsPhysicalQty_Decimal()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P1", LotNo = "",
            PhysicalQty = 2.5m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P2", LotNo = "",
            PhysicalQty = 1.5m, QcStatus = StockQcStatus.Pending });
        await db.SaveChangesAsync();

        Assert.Equal(4.0m, await Stock(db).GetStockQtyAsync("A-01"));
        Assert.Equal(0m, await Stock(db).GetStockQtyAsync("NOPE"));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WmsStockQueryTests"`
Expected: 编译失败（`WmsStockQuery` 未定义 / 接口方法缺）。

- [ ] **Step 3: 扩接口 + DTO（替换整个 `IWmsStockQuery.cs`）**

```csharp
namespace CP6.Core.Services.Integration;

/// <summary>
/// WMS 库存只读查询契约（消费者 Space 侧定义；WMS 接真实现 <see cref="CP6.Core.Services.Wms.WmsStockQuery"/>）。
/// 单向、纯读、join 按 LocationCode。多租户由 CP6Context 全局过滤自动隔离（无 tenantId 参数）。
/// </summary>
public interface IWmsStockQuery
{
    /// <summary>批量按库位编码查库存（叠加主力）。未命中编码不在结果集。</summary>
    Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default);

    /// <summary>按物料/批次/容器反查"哪些库位有它"（D8 P2 半）。</summary>
    Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default);

    /// <summary>单库位库存量（= 批量的单元素特例；04 停用前置 0 库存校验用）。</summary>
    Task<decimal> GetStockQtyAsync(string locationCode, CancellationToken ct = default);
}

/// <summary>库位库存叠加 DTO（join key=LocationCode）。</summary>
public sealed class WmsStockDto
{
    public string  LocationCode { get; set; } = "";
    public int     BinStatus    { get; set; }   // 0空 1有货 2满 3锁定 4在拣
    public decimal Qty          { get; set; }   // ΣPhysicalQty
    public decimal AllocatedQty { get; set; }   // ΣAllocatedQty
    public decimal? Capacity    { get; set; }   // Location.CapacityQty（0/未设→null）
    public string?  TopMaterial { get; set; }   // 占量最大 ProductCd
    public int      ProductKinds{ get; set; }   // distinct ProductCd 数
}

/// <summary>按物料/批/容器反查条件（非空即 AND；全空→空结果）。</summary>
public sealed class StockLocateQuery
{
    public string? MaterialNo { get; set; }
    public string? Lot        { get; set; }
    public string? Container  { get; set; }
}

/// <summary>反查命中库位。</summary>
public sealed class WmsLocationHit
{
    public string  LocationCode { get; set; } = "";
    public decimal Qty          { get; set; }
    public string? Lot          { get; set; }
}

/// <summary>P1 桩：恒空/0。测试与 WMS 未接真时兜底。</summary>
public sealed class StubWmsStockQuery : IWmsStockQuery
{
    public Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WmsStockDto>>(Array.Empty<WmsStockDto>());

    public Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WmsLocationHit>>(Array.Empty<WmsLocationHit>());

    public Task<decimal> GetStockQtyAsync(string locationCode, CancellationToken ct = default)
        => Task.FromResult(0m);
}
```

- [ ] **Step 4: 建最小 `WmsStockQuery`（只够本 Task 编译 + GetStockQtyAsync）**

`CP6.Core/Services/Wms/WmsStockQuery.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>WMS 库存只读查询接真实现（读 T_Stock/T_Location/T_OutboundOrder*；纯读，多租户全局过滤自动隔离）。</summary>
public class WmsStockQuery : IWmsStockQuery
{
    private readonly CP6Context _db;
    public WmsStockQuery(CP6Context db) => _db = db;

    public async Task<decimal> GetStockQtyAsync(string locationCode, CancellationToken ct = default)
        => await _db.Stocks.Where(s => s.LocationCd == locationCode).SumAsync(s => s.PhysicalQty, ct);

    public Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default)
        => throw new NotImplementedException();   // Task 2

    public Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default)
        => throw new NotImplementedException();   // Task 3
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WmsStockQueryTests"`
Expected: PASS（1 例）。

- [ ] **Step 6: 04 兼容回归**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~LocationPublish"`
Expected: 全绿（`GetStockQtyAsync` 改 decimal，调用方 `if(qty>0)` 兼容；既有 04 测仍用 `StubWmsStockQuery` 返 0m）。

- [ ] **Step 7: Commit**

```bash
git add CP6.Core/Services/Integration/IWmsStockQuery.cs CP6.Core/Services/Wms/WmsStockQuery.cs CP6.Tests/WmsStockQueryTests.cs
git commit -m "feat(space-07): T1 扩 IWmsStockQuery 批量+反查契约 + DTO + Stub + GetStockQtyAsync decimal"
```

---

## Task 2：`WmsStockQuery.GetStockByLocationsAsync` 接真 + BinStatus 5 态派生

**Files:**
- Modify: `CP6.Core/Services/Wms/WmsStockQuery.cs`
- Test: `CP6.Tests/WmsStockQueryTests.cs`

> 优先级 **锁定 > 在拣 > 满 > 有货 > 空**。在拣 = 该库位存在 `OutboundOrderDetail`（`AllocatedQty>ShippedQty`）且头 `OutboundOrder.Status==Picking(3)`。

- [ ] **Step 1: 写失败测试（追加到 `WmsStockQueryTests`）**

```csharp
    // 播一个库位的 Stock + Location + 可选出库拣货
    private static async Task SeedLocAsync(CP6Context db, string code, decimal qty,
        decimal cap = 0m, bool blocked = false, string product = "P1")
    {
        if (qty > 0)
            db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = code, ProductCd = product, LotNo = "",
                PhysicalQty = qty, AllocatedQty = 0m, QcStatus = StockQcStatus.Pending });
        db.Locations.Add(new Location { LocationCd = code, WarehouseCd = "W1", CapacityQty = cap, IsBlocked = blocked });
        await db.SaveChangesAsync();
    }

    private static async Task SeedPickingAsync(CP6Context db, string code, int status)
    {
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OB-" + code, WarehouseCd = "W1", Status = status });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OB-" + code, LineNo = 1,
            ProductCd = "P1", LocationCd = code, RequiredQty = 5m, AllocatedQty = 5m, ShippedQty = 0m });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetStockByLocations_Empty_Status0()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 0m, cap: 10m);
        var dto = Assert.Single(await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }));
        Assert.Equal(0, dto.BinStatus);
        Assert.Equal(0m, dto.Qty);
        Assert.Equal(10m, dto.Capacity);
    }

    [Fact]
    public async Task GetStockByLocations_HasStock_Status1()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 3m, cap: 10m);
        var dto = Assert.Single(await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }));
        Assert.Equal(1, dto.BinStatus);
        Assert.Equal(3m, dto.Qty);
    }

    [Fact]
    public async Task GetStockByLocations_Full_Status2()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 10m, cap: 10m);
        Assert.Equal(2, (await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }))[0].BinStatus);
    }

    [Fact]
    public async Task GetStockByLocations_Blocked_Status3_OverridesFull()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 10m, cap: 10m, blocked: true);
        Assert.Equal(3, (await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }))[0].BinStatus);
    }

    [Fact]
    public async Task GetStockByLocations_Picking_Status4_OverridesFull()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 10m, cap: 10m);
        await SeedPickingAsync(db, "A-01", OutboundOrderStatus.Picking);  // 3
        Assert.Equal(4, (await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }))[0].BinStatus);
    }

    [Fact]
    public async Task GetStockByLocations_AllocatedNotPicking_NotStatus4()
    {
        using var db = NewDb();
        await SeedLocAsync(db, "A-01", qty: 3m, cap: 10m);
        await SeedPickingAsync(db, "A-01", OutboundOrderStatus.Allocated);  // 2，非 Picking → 不算在拣
        Assert.Equal(1, (await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }))[0].BinStatus);
    }

    [Fact]
    public async Task GetStockByLocations_Aggregates_TopMaterial_Kinds()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "PA", LotNo = "",
            PhysicalQty = 2m, AllocatedQty = 1m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "PB", LotNo = "",
            PhysicalQty = 5m, AllocatedQty = 0m, QcStatus = StockQcStatus.Pending });
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W1", CapacityQty = 0m });
        await db.SaveChangesAsync();

        var dto = Assert.Single(await Stock(db).GetStockByLocationsAsync(new[] { "A-01" }));
        Assert.Equal(7m, dto.Qty);
        Assert.Equal(1m, dto.AllocatedQty);
        Assert.Equal("PB", dto.TopMaterial);   // 占量最大
        Assert.Equal(2, dto.ProductKinds);
        Assert.Null(dto.Capacity);             // CapacityQty=0 → null
    }

    [Fact]
    public async Task GetStockByLocations_NoData_NotReturned()
    {
        using var db = NewDb();
        var r = await Stock(db).GetStockByLocationsAsync(new[] { "GHOST" });
        Assert.Empty(r);
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WmsStockQueryTests"`
Expected: 新增例失败（`GetStockByLocationsAsync` 抛 NotImplementedException）。

- [ ] **Step 3: 实现 `GetStockByLocationsAsync`（替换 Task 1 的占位方法）**

```csharp
    public async Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default)
    {
        var codes = locationCodes.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        if (codes.Count == 0) return Array.Empty<WmsStockDto>();

        var stockRows = await _db.Stocks.Where(s => codes.Contains(s.LocationCd)).ToListAsync(ct);
        var stockByLoc = stockRows.GroupBy(s => s.LocationCd).ToDictionary(g => g.Key, g => new
        {
            Qty = g.Sum(x => x.PhysicalQty),
            Allocated = g.Sum(x => x.AllocatedQty),
            Kinds = g.Select(x => x.ProductCd).Distinct().Count(),
            Top = g.OrderByDescending(x => x.PhysicalQty).Select(x => x.ProductCd).FirstOrDefault(),
        });

        var locs = await _db.Locations.Where(l => codes.Contains(l.LocationCd)).ToListAsync(ct);
        var locByCode = locs.GroupBy(l => l.LocationCd).ToDictionary(g => g.Key, g => g.First());

        var pickingCodes = await (
            from d in _db.OutboundOrderDetails
            where d.LocationCd != null && codes.Contains(d.LocationCd) && d.AllocatedQty > d.ShippedQty
            join o in _db.OutboundOrders on d.OutboundNo equals o.OutboundNo
            where o.Status == OutboundOrderStatus.Picking
            select d.LocationCd!).Distinct().ToListAsync(ct);
        var pickingSet = pickingCodes.ToHashSet();

        var result = new List<WmsStockDto>();
        foreach (var code in codes)
        {
            stockByLoc.TryGetValue(code, out var st);
            locByCode.TryGetValue(code, out var loc);
            if (st is null && loc is null) continue;   // 无数据 → 不返回

            var qty = st?.Qty ?? 0m;
            var cap = (loc is not null && loc.CapacityQty > 0) ? loc.CapacityQty : (decimal?)null;

            int status;
            if (loc?.IsBlocked == true)                 status = 3; // 锁定
            else if (pickingSet.Contains(code))         status = 4; // 在拣
            else if (cap.HasValue && qty >= cap.Value)  status = 2; // 满
            else if (qty > 0)                           status = 1; // 有货
            else                                        status = 0; // 空

            result.Add(new WmsStockDto
            {
                LocationCode = code, BinStatus = status, Qty = qty,
                AllocatedQty = st?.Allocated ?? 0m, Capacity = cap,
                TopMaterial = st?.Top, ProductKinds = st?.Kinds ?? 0,
            });
        }
        return result;
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WmsStockQueryTests"`
Expected: PASS（全部例）。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Wms/WmsStockQuery.cs CP6.Tests/WmsStockQueryTests.cs
git commit -m "feat(space-07): T2 GetStockByLocations 接真 + BinStatus 5态派生(锁定>在拣>满>有货>空)"
```

---

## Task 3：`WmsStockQuery.FindLocationsAsync` 接真（按物料/批/容器反查）

**Files:**
- Modify: `CP6.Core/Services/Wms/WmsStockQuery.cs`
- Test: `CP6.Tests/WmsStockQueryTests.cs`

- [ ] **Step 1: 写失败测试（追加）**

```csharp
    [Fact]
    public async Task FindLocations_ByMaterial_ReturnsHitsWithQty()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "PX", LotNo = "L1",
            PhysicalQty = 3m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-02", ProductCd = "PX", LotNo = "L2",
            PhysicalQty = 5m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-03", ProductCd = "PY", LotNo = "",
            PhysicalQty = 9m, QcStatus = StockQcStatus.Pending });
        await db.SaveChangesAsync();

        var hits = await Stock(db).FindLocationsAsync(new StockLocateQuery { MaterialNo = "PX" });
        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.LocationCode == "A-01" && h.Qty == 3m);
        Assert.Contains(hits, h => h.LocationCode == "A-02" && h.Qty == 5m);
    }

    [Fact]
    public async Task FindLocations_ByMaterialAndLot_Filters()
    {
        using var db = NewDb();
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "PX", LotNo = "L1",
            PhysicalQty = 3m, QcStatus = StockQcStatus.Pending });
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-02", ProductCd = "PX", LotNo = "L2",
            PhysicalQty = 5m, QcStatus = StockQcStatus.Pending });
        await db.SaveChangesAsync();

        var hits = await Stock(db).FindLocationsAsync(new StockLocateQuery { MaterialNo = "PX", Lot = "L2" });
        var h = Assert.Single(hits);
        Assert.Equal("A-02", h.LocationCode);
    }

    [Fact]
    public async Task FindLocations_ByContainer_UsesPallet()
    {
        using var db = NewDb();
        db.Pallets.Add(new Pallet { PalletNo = "PLT-1", LocationCd = "A-09", ProductCd = "PZ", LotNo = "L1" });
        await db.SaveChangesAsync();

        var hits = await Stock(db).FindLocationsAsync(new StockLocateQuery { Container = "PLT-1" });
        Assert.Equal("A-09", Assert.Single(hits).LocationCode);
    }

    [Fact]
    public async Task FindLocations_AllEmpty_ReturnsEmpty()
    {
        using var db = NewDb();
        Assert.Empty(await Stock(db).FindLocationsAsync(new StockLocateQuery()));
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WmsStockQueryTests.FindLocations"`
Expected: 失败（NotImplementedException）。

- [ ] **Step 3: 实现 `FindLocationsAsync`（替换占位）**

```csharp
    public async Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default)
    {
        var hasMat = !string.IsNullOrWhiteSpace(query.MaterialNo);
        var hasLot = !string.IsNullOrWhiteSpace(query.Lot);
        var hasCon = !string.IsNullOrWhiteSpace(query.Container);
        if (!hasMat && !hasLot && !hasCon) return Array.Empty<WmsLocationHit>();

        // 容器：经 Pallet 反查库位
        if (hasCon)
        {
            return await _db.Pallets
                .Where(p => p.PalletNo == query.Container && p.LocationCd != null)
                .GroupBy(p => p.LocationCd!)
                .Select(g => new WmsLocationHit { LocationCode = g.Key, Qty = 0m, Lot = null })
                .ToListAsync(ct);
        }

        // 物料/批次：经 Stock 反查
        var q = _db.Stocks.Where(s => s.PhysicalQty > 0);
        if (hasMat) q = q.Where(s => s.ProductCd == query.MaterialNo);
        if (hasLot) q = q.Where(s => s.LotNo == query.Lot);
        return await q
            .GroupBy(s => s.LocationCd)
            .Select(g => new WmsLocationHit
            {
                LocationCode = g.Key,
                Qty = g.Sum(x => x.PhysicalQty),
                Lot = hasLot ? query.Lot : null,
            })
            .ToListAsync(ct);
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WmsStockQueryTests"`
Expected: PASS（全部）。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Wms/WmsStockQuery.cs CP6.Tests/WmsStockQueryTests.cs
git commit -m "feat(space-07): T3 FindLocations 接真(物料/批次→Stock、容器→Pallet)"
```

---

## Task 4：DI 替换 + `SpaceStockController`（`/stock`、`/stock/locate`）

**Files:**
- Modify: `CP6.WebApi/Program.cs:374`
- Create: `CP6.WebApi/Controllers/Space/SpaceStockController.cs`
- Test: `CP6.Tests/WmsStockQueryTests.cs`（多租户一例，确认全局过滤）

> 控制器无独立 Service（直接注 `IWmsStockQuery` + `CP6Context` 枚举该层 Placed 编码）。沿用 `Ok2` 信封。

- [ ] **Step 1: 写失败测试（多租户隔离，追加）**

```csharp
    [Fact]
    public async Task GetStockByLocations_TenantIsolated()
    {
        var dbName = Guid.NewGuid().ToString();
        var optsA = new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(dbName).Options;
        var t2 = new CP6.Core.Services.Common.TenantContext { CurrentTenantId = Guid.NewGuid() };

        // 租户2 播一条
        using (var db2 = new CP6Context(optsA, t2))
        {
            db2.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "A-01", ProductCd = "P1", LotNo = "",
                PhysicalQty = 5m, QcStatus = StockQcStatus.Pending });
            db2.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W1" });
            await db2.SaveChangesAsync();
        }
        // 默认租户查 → 看不到租户2 的数据
        using var dbDefault = new CP6Context(optsA);
        Assert.Empty(await new WmsStockQuery(dbDefault).GetStockByLocationsAsync(new[] { "A-01" }));
    }
```

- [ ] **Step 2: 跑测试确认通过（验证既有实现已隔离）**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WmsStockQueryTests.GetStockByLocations_TenantIsolated"`
Expected: PASS（全局过滤天然隔离；若失败说明实现误用了跨租户查询）。

- [ ] **Step 3: DI 替换（`Program.cs:374`）**

把：
```csharp
builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsStockQuery, CP6.Core.Services.Integration.StubWmsStockQuery>();
```
改为：
```csharp
builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsStockQuery, CP6.Core.Services.Wms.WmsStockQuery>();
```

- [ ] **Step 4: 建 `SpaceStockController.cs`**

`CP6.WebApi/Controllers/Space/SpaceStockController.cs`：
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
public class SpaceStockController : ControllerBase
{
    private readonly IWmsStockQuery _stock;
    private readonly CP6Context _db;
    public SpaceStockController(IWmsStockQuery stock, CP6Context db) { _stock = stock; _db = db; }

    private IActionResult Ok2(object? data = null, string msg = "OK") => Ok(new { code = 0, message = msg, data });

    /// <summary>取某层库存快照（服务端枚举该层 Placed 库位编码 → 批量查 WMS）。</summary>
    [HttpGet("floor/{floorId:guid}/stock")]
    public async Task<IActionResult> FloorStock(Guid floorId, CancellationToken ct)
    {
        var codes = await _db.Space_Locations
            .Where(l => l.FloorId == floorId && l.Placed && l.LocationCode != null)
            .Select(l => l.LocationCode!)
            .ToListAsync(ct);
        var items = await _stock.GetStockByLocationsAsync(codes, ct);
        return Ok2(new { items, ts = DateTime.Now });
    }

    /// <summary>按物料/批次/容器反查库位（命中列表，前端逐个复用 06 定位）。</summary>
    [HttpGet("stock/locate")]
    public async Task<IActionResult> Locate(
        [FromQuery] string? material, [FromQuery] string? lot, [FromQuery] string? container, CancellationToken ct)
    {
        var hits = await _stock.FindLocationsAsync(
            new StockLocateQuery { MaterialNo = material, Lot = lot, Container = container }, ct);
        return Ok2(hits);
    }
}
```

- [ ] **Step 5: 编译 + Part A 全回归**

Run: `dotnet build CP6.WebApi` 然后 `dotnet test CP6.Tests`
Expected: build 0 error；全量绿（P1 1287 + 本计划新增，零回归；04 仍绿）。

- [ ] **Step 6: Commit**

```bash
git add CP6.WebApi/Program.cs CP6.WebApi/Controllers/Space/SpaceStockController.cs CP6.Tests/WmsStockQueryTests.cs
git commit -m "feat(space-07): T4 DI 接真 + SpaceStockController(/stock、/stock/locate)"
```

---

# Part B — 前端 overlay

## Task 5：API 层 + TS 类型

**Files:**
- Create: `cp6.web/src/types/space/overlay.ts`
- Create: `cp6.web/src/api/space/stock.ts`

- [ ] **Step 1: 建类型 `overlay.ts`**

```typescript
// cp6.web/src/types/space/overlay.ts —— 对齐后端 WmsStockDto/WmsLocationHit
export interface WmsStockDto {
  locationCode: string
  binStatus: number    // 0空 1有货 2满 3锁定 4在拣
  qty: number
  allocatedQty: number
  capacity: number | null
  topMaterial: string | null
  productKinds: number
}

export interface FloorStockSnapshot {
  items: WmsStockDto[]
  ts: string           // 服务器快照时间戳
}

export interface WmsLocationHit {
  locationCode: string
  qty: number
  lot: string | null
}

export type OverlayMode = 'status' | 'utilization' | 'off'
```

- [ ] **Step 2: 建 API `stock.ts`**

```typescript
// cp6.web/src/api/space/stock.ts
import http from '../http'
import type { Envelope } from '@/types/space/scene'
import type { FloorStockSnapshot, WmsLocationHit } from '@/types/space/overlay'

export const stockApi = {
  floorStock(floorId: string) {
    return http.get<unknown, Envelope<FloorStockSnapshot>>(`/space/floor/${floorId}/stock`)
  },
  locate(params: { material?: string; lot?: string; container?: string }) {
    return http.get<unknown, Envelope<WmsLocationHit[]>>(`/space/stock/locate`, { params })
  },
}
```

- [ ] **Step 3: 类型校验**

Run: `cd cp6.web && npx vue-tsc --noEmit`
Expected: 0 error。

- [ ] **Step 4: Commit**

```bash
git add cp6.web/src/types/space/overlay.ts cp6.web/src/api/space/stock.ts
git commit -m "feat(space-07): T5 前端 overlay 类型 + stock api"
```

---

## Task 6：`stockModel.ts` 纯逻辑 + vitest（色映射 / 利用率聚合）

**Files:**
- Create: `cp6.web/src/space-viewer/overlay/stockModel.ts`
- Test: `cp6.web/src/space-viewer/overlay/stockModel.spec.ts`

- [ ] **Step 1: 写失败测试**

```typescript
// cp6.web/src/space-viewer/overlay/stockModel.spec.ts
import { describe, it, expect } from 'vitest'
import { binStatusToHex, NO_DATA_HEX, locationUtilization, utilizationToHex, aggregateUtilization } from './stockModel'

describe('stockModel', () => {
  it('binStatusToHex maps 5 states', () => {
    expect(binStatusToHex(0)).toBe(0x4caf50) // 空 绿
    expect(binStatusToHex(1)).toBe(0x2196f3) // 有货 蓝
    expect(binStatusToHex(2)).toBe(0xf44336) // 满 红
    expect(binStatusToHex(3)).toBe(0x9e9e9e) // 锁定 灰
    expect(binStatusToHex(4)).toBe(0xffc107) // 在拣 黄
    expect(binStatusToHex(99)).toBe(NO_DATA_HEX) // 未知/无数据 中性
  })

  it('locationUtilization: qty/capacity, fallback to status coarse', () => {
    expect(locationUtilization({ qty: 5, capacity: 10, binStatus: 1 } as any)).toBeCloseTo(0.5)
    // 无容量 → 按 BinStatus 粗估：空0 / 有货0.5 / 满1
    expect(locationUtilization({ qty: 3, capacity: null, binStatus: 0 } as any)).toBe(0)
    expect(locationUtilization({ qty: 3, capacity: null, binStatus: 1 } as any)).toBe(0.5)
    expect(locationUtilization({ qty: 3, capacity: null, binStatus: 2 } as any)).toBe(1)
  })

  it('utilizationToHex: cold→warm at 0/0.5/1', () => {
    expect(utilizationToHex(0)).toBe(0x2196f3)   // 蓝
    expect(utilizationToHex(1)).toBe(0xf44336)   // 红
    expect(typeof utilizationToHex(0.5)).toBe('number')
  })

  it('aggregateUtilization sums qty/capacity (capacity-bearing only)', () => {
    const agg = aggregateUtilization([
      { qty: 5, capacity: 10, binStatus: 1 },
      { qty: 10, capacity: 10, binStatus: 2 },
      { qty: 3, capacity: null, binStatus: 1 }, // 无容量不计入分母
    ] as any)
    expect(agg).toBeCloseTo(15 / 20)
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd cp6.web && npx vitest run src/space-viewer/overlay/stockModel.spec.ts`
Expected: 失败（模块不存在）。

- [ ] **Step 3: 实现 `stockModel.ts`**

```typescript
// cp6.web/src/space-viewer/overlay/stockModel.ts
import type { WmsStockDto } from '@/types/space/overlay'

const STATUS_HEX: Record<number, number> = {
  0: 0x4caf50, // 空 绿
  1: 0x2196f3, // 有货 蓝
  2: 0xf44336, // 满 红
  3: 0x9e9e9e, // 锁定 灰
  4: 0xffc107, // 在拣 黄
}
export const NO_DATA_HEX = 0x455a64 // 无数据 中性灰（区别于锁定灰）

export function binStatusToHex(status: number): number {
  return STATUS_HEX[status] ?? NO_DATA_HEX
}

/** 库位利用率 [0,1]：有容量用 qty/capacity；无容量按 BinStatus 粗估（空0/有货0.5/满1，锁定/在拣按量近似）。 */
export function locationUtilization(d: WmsStockDto): number {
  if (d.capacity && d.capacity > 0) return Math.min(1, d.qty / d.capacity)
  if (d.binStatus === 2) return 1
  if (d.binStatus === 0) return 0
  return d.qty > 0 ? 0.5 : 0
}

/** 冷→暖渐变：0=蓝 0.5=黄 1=红（线性插值 RGB）。 */
export function utilizationToHex(u: number): number {
  const t = Math.max(0, Math.min(1, u))
  const lerp = (a: number, b: number, k: number) => Math.round(a + (b - a) * k)
  let r: number, g: number, b: number
  if (t < 0.5) { const k = t / 0.5; r = lerp(0x21, 0xff, k); g = lerp(0x96, 0xc1, k); b = lerp(0xf3, 0x07, k) }
  else { const k = (t - 0.5) / 0.5; r = lerp(0xff, 0xf4, k); g = lerp(0xc1, 0x43, k); b = lerp(0x07, 0x36, k) }
  return (r << 16) | (g << 8) | b
}

/** 货架/库区聚合利用率：Σqty / Σcapacity（仅含有容量库位；无则返 0）。 */
export function aggregateUtilization(items: WmsStockDto[]): number {
  let q = 0, c = 0
  for (const it of items) if (it.capacity && it.capacity > 0) { q += it.qty; c += it.capacity }
  return c > 0 ? q / c : 0
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd cp6.web && npx vitest run src/space-viewer/overlay/stockModel.spec.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add cp6.web/src/space-viewer/overlay/stockModel.ts cp6.web/src/space-viewer/overlay/stockModel.spec.ts
git commit -m "feat(space-07): T6 stockModel 纯逻辑(色映射/利用率/聚合) + vitest"
```

---

## Task 7：`StockOverlay` 类（持快照 + 三模式着色 + 刷新）+ vitest

**Files:**
- Create: `cp6.web/src/space-viewer/overlay/StockOverlay.ts`
- Test: `cp6.web/src/space-viewer/overlay/StockOverlay.spec.ts`

> 依赖 `ViewerHandle`（`setInstanceColor`/`requestRender`/`getCurrentFloorId`）。着色前若有高亮态先交给调用方 `clear`（避免 hover/select 竞态，T11 FloorViewer 接线时处理）。

- [ ] **Step 1: 写失败测试**

```typescript
// cp6.web/src/space-viewer/overlay/StockOverlay.spec.ts
import { describe, it, expect, vi } from 'vitest'
import { StockOverlay } from './StockOverlay'
import { binStatusToHex } from './stockModel'
import type { WmsStockDto } from '@/types/space/overlay'

function fakeViewer() {
  return { setInstanceColor: vi.fn(), requestRender: vi.fn() }
}
const dto = (locationCode: string, binStatus: number): WmsStockDto =>
  ({ locationCode, binStatus, qty: 1, allocatedQty: 0, capacity: 10, topMaterial: null, productKinds: 1 })

describe('StockOverlay', () => {
  it('applies status colors per location in status mode', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 0), dto('A-02', 2)])
    o.setMode('status')
    o.apply()
    expect(v.setInstanceColor).toHaveBeenCalledWith('A-01', binStatusToHex(0))
    expect(v.setInstanceColor).toHaveBeenCalledWith('A-02', binStatusToHex(2))
    expect(v.requestRender).toHaveBeenCalled()
  })

  it('off mode does not color (caller resets to grey)', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 0)])
    o.setMode('off')
    o.apply()
    expect(v.setInstanceColor).not.toHaveBeenCalled()
  })

  it('getStock returns cached dto by code', () => {
    const v = fakeViewer()
    const o = new StockOverlay(v as any)
    o.setSnapshot([dto('A-01', 1)])
    expect(o.getStock('A-01')?.binStatus).toBe(1)
    expect(o.getStock('GHOST')).toBeNull()
  })
})
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd cp6.web && npx vitest run src/space-viewer/overlay/StockOverlay.spec.ts`
Expected: 失败（模块不存在）。

- [ ] **Step 3: 实现 `StockOverlay.ts`**

```typescript
// cp6.web/src/space-viewer/overlay/StockOverlay.ts
import type { ViewerHandle } from '../api/ViewerHandle'
import type { WmsStockDto, OverlayMode } from '@/types/space/overlay'
import { stockApi } from '@/api/space/stock'
import { binStatusToHex, locationUtilization, utilizationToHex } from './stockModel'

export class StockOverlay {
  private _viewer: ViewerHandle
  private _mode: OverlayMode = 'status'
  private _byCode = new Map<string, WmsStockDto>()
  private _ts = ''
  private _pollTimer = 0
  private _minIntervalMs = 5000

  constructor(viewer: ViewerHandle) { this._viewer = viewer }

  get mode(): OverlayMode { return this._mode }
  get ts(): string { return this._ts }

  setMode(m: OverlayMode): void { this._mode = m }
  setSnapshot(items: WmsStockDto[], ts = ''): void {
    this._byCode = new Map(items.map((i) => [i.locationCode, i]))
    this._ts = ts
  }
  getStock(code: string | null): WmsStockDto | null {
    return code ? (this._byCode.get(code) ?? null) : null
  }

  /** 按当前模式着色（off 不着色，由调用方先回灰）。 */
  apply(): void {
    if (this._mode === 'off') return
    for (const [code, d] of this._byCode) {
      const hex = this._mode === 'utilization'
        ? utilizationToHex(locationUtilization(d))
        : binStatusToHex(d.binStatus)
      this._viewer.setInstanceColor(code, hex)
    }
    this._viewer.requestRender()
  }

  /** 拉当前楼层快照并着色。 */
  async refresh(floorId: string): Promise<void> {
    const env = await stockApi.floorStock(floorId)
    this.setSnapshot(env.data.items, env.data.ts)
    this.apply()
  }

  startPolling(getFloorId: () => string, intervalMs: number): void {
    this.stopPolling()
    const ms = Math.max(this._minIntervalMs, intervalMs)
    this._pollTimer = window.setInterval(() => { void this.refresh(getFloorId()) }, ms)
  }
  stopPolling(): void {
    if (this._pollTimer) { clearInterval(this._pollTimer); this._pollTimer = 0 }
  }
  dispose(): void { this.stopPolling(); this._byCode.clear() }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd cp6.web && npx vitest run src/space-viewer/overlay/StockOverlay.spec.ts`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add cp6.web/src/space-viewer/overlay/StockOverlay.ts cp6.web/src/space-viewer/overlay/StockOverlay.spec.ts
git commit -m "feat(space-07): T7 StockOverlay 持快照+三模式着色+刷新/轮询 + vitest"
```

---

## Task 8：`StockLegend.vue` 图例 + 模式切换 + 刷新/轮询开关

**Files:**
- Create: `cp6.web/src/views/space/viewer/StockLegend.vue`

> 纯展示组件，emit 事件给 FloorViewer（T11 接线）。无独立逻辑测试（视觉态留 gstack）。

- [ ] **Step 1: 建组件**

```vue
<!-- cp6.web/src/views/space/viewer/StockLegend.vue -->
<template>
  <div class="stock-legend">
    <div class="legend-modes">
      <button :class="{ on: mode === 'status' }" @click="$emit('mode', 'status')">{{ t('状态') }}</button>
      <button :class="{ on: mode === 'utilization' }" @click="$emit('mode', 'utilization')">{{ t('利用率') }}</button>
      <button :class="{ on: mode === 'off' }" @click="$emit('mode', 'off')">{{ t('关闭') }}</button>
    </div>
    <button class="legend-refresh" @click="$emit('refresh')">{{ t('刷新库存') }}</button>
    <label class="legend-poll"><input type="checkbox" :checked="polling" @change="$emit('toggle-poll')" />{{ t('自动刷新') }}</label>
    <div v-if="ts" class="legend-ts">{{ t('数据时间') }} {{ ts }}</div>
    <ul v-if="mode === 'status'" class="legend-items">
      <li><i class="sw" style="background:#4caf50" />{{ t('空') }}</li>
      <li><i class="sw" style="background:#2196f3" />{{ t('有货') }}</li>
      <li><i class="sw" style="background:#f44336" />{{ t('满') }}</li>
      <li><i class="sw" style="background:#9e9e9e" />{{ t('锁定') }}</li>
      <li><i class="sw" style="background:#ffc107" />{{ t('在拣') }}</li>
    </ul>
    <div v-else-if="mode === 'utilization'" class="legend-grad">{{ t('低') }} <i class="grad" /> {{ t('高') }}</div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { OverlayMode } from '@/types/space/overlay'
const { t } = useI18n()
defineProps<{ mode: OverlayMode; polling: boolean; ts: string }>()
defineEmits<{ (e: 'mode', m: OverlayMode): void; (e: 'refresh'): void; (e: 'toggle-poll'): void }>()
</script>

<style scoped>
.stock-legend { position: absolute; left: 16px; bottom: 16px; background: rgba(12,12,28,.92);
  border: 1px solid rgba(79,195,247,.35); border-radius: 6px; color: #e0e0e0; font-size: 12px; padding: 8px 10px; z-index: 10; }
.legend-modes button, .legend-refresh { background: transparent; color: #9fb3c8; border: 1px solid #37474f; border-radius: 4px; margin: 2px; cursor: pointer; }
.legend-modes button.on { color: #4fc3f7; border-color: #4fc3f7; }
.legend-items { list-style: none; padding: 4px 0 0; margin: 0; }
.legend-items li { display: flex; align-items: center; gap: 6px; line-height: 1.6; }
.sw { width: 12px; height: 12px; display: inline-block; border-radius: 2px; }
.grad { display: inline-block; width: 80px; height: 10px; background: linear-gradient(90deg,#2196f3,#ffc107,#f44336); vertical-align: middle; }
.legend-ts { color: #78909c; margin-top: 4px; }
</style>
```

- [ ] **Step 2: 类型校验**

Run: `cd cp6.web && npx vue-tsc --noEmit`
Expected: 0 error。

- [ ] **Step 3: Commit**

```bash
git add cp6.web/src/views/space/viewer/StockLegend.vue
git commit -m "feat(space-07): T8 StockLegend 图例+模式切换+刷新/轮询开关"
```

---

## Task 9：InfoCard 叠库存行

**Files:**
- Modify: `cp6.web/src/views/space/viewer/InfoCard.vue`

> 由 FloorViewer 传入 `stock`（来自 overlay 缓存），InfoCard 仅展示（不自己拉库存）。

- [ ] **Step 1: 改 props + 模板**

在 `InfoCard.vue` 的 `defineProps` 改为：
```typescript
const props = defineProps<{ locationId: string | null; stock?: import('@/types/space/overlay').WmsStockDto | null }>()
```

在 `<template>` 的状态行（`detail.status` 那行）之后、`</template>` 前插入库存块：
```vue
      <template v-if="stock">
        <div class="info-card__row info-card__sep">{{ t('库存') }}</div>
        <div class="info-card__row">
          <span class="info-card__label">{{ t('库位状态') }}</span>
          <span class="info-card__value">{{ binStatusText }}</span>
        </div>
        <div class="info-card__row">
          <span class="info-card__label">{{ t('库存量') }}</span>
          <span class="info-card__value">{{ stock.qty }}<template v-if="stock.capacity"> / {{ stock.capacity }}（{{ utilPct }}%）</template></span>
        </div>
        <div class="info-card__row" v-if="stock.topMaterial">
          <span class="info-card__label">{{ t('主物料') }}</span>
          <span class="info-card__value">{{ stock.topMaterial }}</span>
        </div>
      </template>
```

- [ ] **Step 2: 加 computed（script setup 内）**

```typescript
import { locationUtilization } from '@/space-viewer/overlay/stockModel'
// ...
const binStatusText = computed(() => {
  const m = ['空', '有货', '满', '锁定', '在拣']
  return props.stock ? t(m[props.stock.binStatus] ?? '无数据') : ''
})
const utilPct = computed(() =>
  props.stock ? Math.round(locationUtilization(props.stock) * 100) : 0)
```

- [ ] **Step 3: 类型校验**

Run: `cd cp6.web && npx vue-tsc --noEmit`
Expected: 0 error。

- [ ] **Step 4: Commit**

```bash
git add cp6.web/src/views/space/viewer/InfoCard.vue
git commit -m "feat(space-07): T9 InfoCard 叠库存行(状态/量/容量/利用率/主物料)"
```

---

## Task 10：SearchBox 增"按物料"模式（物料定位）

**Files:**
- Modify: `cp6.web/src/views/space/viewer/SearchBox.vue`

> 增一个"码/料"切换；料模式 emit `locate-material`，FloorViewer（T11）调 `/stock/locate` → 逐个复用 `Locator.locate(code)`。

- [ ] **Step 1: 改 SearchBox**

在 `SearchBox.vue` 模板加模式切换 + 改 emit：
```vue
<template>
  <div class="search-box">
    <select v-model="mode" class="sb-mode">
      <option value="code">{{ t('按编码') }}</option>
      <option value="material">{{ t('按物料') }}</option>
    </select>
    <input v-model="kw" :placeholder="mode === 'code' ? t('输入库位编码') : t('输入物料号')" @keyup.enter="onEnter" />
    <button @click="onEnter">{{ t('定位') }}</button>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
const mode = ref<'code' | 'material'>('code')
const kw = ref('')
const emit = defineEmits<{ (e: 'locate', code: string): void; (e: 'locate-material', material: string): void }>()
function onEnter(): void {
  const v = kw.value.trim()
  if (!v) return
  if (mode.value === 'code') emit('locate', v)
  else emit('locate-material', v)
}
</script>
```
> 若既有 SearchBox 已有样式/结构，保留其样式 class，仅加 `select` 与第二个 emit。

- [ ] **Step 2: 类型校验**

Run: `cd cp6.web && npx vue-tsc --noEmit`
Expected: 0 error。

- [ ] **Step 3: Commit**

```bash
git add cp6.web/src/views/space/viewer/SearchBox.vue
git commit -m "feat(space-07): T10 SearchBox 按物料模式"
```

---

## Task 11：FloorViewer 集成（overlay 接线 + 图例 + 刷新 + 物料定位 + 喂 InfoCard）

**Files:**
- Modify: `cp6.web/src/views/space/viewer/FloorViewer.vue`

> 关键接线：viewer 就绪后建 `StockOverlay`；切层/onReady 后 `overlay.refresh(floorId)`；图例事件接 overlay；物料定位调 api 后复用 `locator.locate(code)`；把 `overlay.getStock(selectedId)` 传给 InfoCard。**着色刷新前先 `viewer.clearSelection?.()` 或忽略**（hover/select 竞态：刷新会重写底色，重选即恢复，详见 spec §3.6）。

- [ ] **Step 1: 引入 + 实例化**

在 `<script setup>` 顶部 imports 加：
```typescript
import { StockOverlay } from '@/space-viewer/overlay/StockOverlay'
import { stockApi } from '@/api/space/stock'
import StockLegend from './StockLegend.vue'
import type { OverlayMode, WmsStockDto } from '@/types/space/overlay'
import { ElMessage } from 'element-plus'
```
加响应式状态（与既有 refs 并列）：
```typescript
let overlay: StockOverlay | null = null
const overlayMode = ref<OverlayMode>('status')
const overlayTs = ref('')
const polling = ref(false)
const selectedStock = ref<WmsStockDto | null>(null)
```

- [ ] **Step 2: onMounted 内 viewer 建好后接 overlay**

在 `viewer = new SpaceViewer(canvas)` 之后、`viewer.onReady(...)` 回调里加刷新：
```typescript
overlay = new StockOverlay(viewer as unknown as import('@/space-viewer/api/ViewerHandle').ViewerHandle)
viewer.onReady(() => {
  loading.value = false
  void refreshStock()
})
```

- [ ] **Step 3: 加方法（script 内）**

```typescript
async function refreshStock(): Promise<void> {
  if (!overlay) return
  try {
    await overlay.refresh(currentFloorId.value)
    overlay.setMode(overlayMode.value)
    overlay.apply()
    overlayTs.value = overlay.ts
    syncSelectedStock()
  } catch {
    ElMessage.warning(t('库存数据获取失败，显示上次快照'))   // W-SPACE-701
  }
}
function onOverlayMode(m: OverlayMode): void {
  overlayMode.value = m
  overlay?.setMode(m)
  if (m === 'off') { void onSwitchFloor(currentFloorId.value) }  // 关叠加→重载回灰（简单可靠）
  else overlay?.apply()
}
function onTogglePoll(): void {
  polling.value = !polling.value
  if (polling.value) overlay?.startPolling(() => currentFloorId.value, 5000)
  else overlay?.stopPolling()
}
function syncSelectedStock(): void {
  selectedStock.value = overlay?.getStock(selectedId.value) ?? null
}
async function onLocateMaterial(material: string): Promise<void> {
  try {
    const env = await stockApi.locate({ material })
    const hits = env.data
    if (!hits.length) { ElMessage.info(t('无库位存放该物料')); return }     // I-SPACE-701
    if (hits.length > 1) ElMessage.info(t('找到 {n} 个库位，点击定位').replace('{n}', String(hits.length)))  // I-SPACE-702
    if (locator) await locator.locate(hits[0]!.locationCode)               // 复用 06 定位（首个）
  } catch {
    ElMessage.warning(t('库存数据获取失败'))
  }
}
```
在既有 `onClick`（设 `selectedId`）之后同步库存：把 `selectedId.value = viewer.select(pick)` 后补一行 `syncSelectedStock()`。

- [ ] **Step 4: 模板接线**

在 `<InfoCard :location-id="selectedId" @close="selectedId = null" />` 改为：
```vue
<InfoCard :location-id="selectedId" :stock="selectedStock" @close="selectedId = null" />
```
在 `<SearchBox class="viewer-searchbox" @locate="onLocate" />` 加物料事件：
```vue
<SearchBox class="viewer-searchbox" @locate="onLocate" @locate-material="onLocateMaterial" />
```
在 `viewer-main` 内（canvas 后）加图例：
```vue
<StockLegend :mode="overlayMode" :polling="polling" :ts="overlayTs"
  @mode="onOverlayMode" @refresh="refreshStock" @toggle-poll="onTogglePoll" />
```

- [ ] **Step 5: onBeforeUnmount 清理**

```typescript
overlay?.dispose()
overlay = null
```

- [ ] **Step 6: 三门校验**

Run: `cd cp6.web && npx vue-tsc --noEmit && npx vitest run && npm run build`
Expected: vue-tsc 0 error / vitest 全绿（既有 + 本计划新增）/ build 成功。

- [ ] **Step 7: Commit**

```bash
git add cp6.web/src/views/space/viewer/FloorViewer.vue
git commit -m "feat(space-07): T11 FloorViewer 接 overlay(图例/刷新/物料定位/喂InfoCard)"
```

---

# Part C — 演示种子 + QA

## Task 12：演示种子 + gstack 真浏览器 QA + 全回归固化

**Files:**
- Create: `docs/superpowers/qa/space-p2-07/seed-stock.sql`（演示库存种子 SQL，QA 用）
- Create: `docs/superpowers/qa/space-p2-07/README.md`（QA 记录）

> 接真后 gstack QA 需真 SQL Server 隔离库（沿用 P1 的 `CP6DB_SpaceQA` 拷贝法）+ 演示库存数据。**此 Task 由用户监督跑**（起主机/浏览器/真库），实施者产出种子脚本 + QA 清单，跑通后固化截图与记录。

- [ ] **Step 1: 写演示种子 SQL**

`docs/superpowers/qa/space-p2-07/seed-stock.sql`（对隔离库 `CP6DB_SpaceQA`，库位编码须与已发布 Space 库位对齐，TenantId 用默认租户 `00000000-0000-0000-0000-0000000000A1`）：
```sql
-- 演示：5 态各一例（库位编码替换为 QA 中实际已发布编码）
DECLARE @T uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';
-- 有货
INSERT INTO T_Stock (Id,TenantId,WarehouseCd,LocationCd,ProductCd,LotNo,PhysicalQty,AllocatedQty,AvailableQty,QcStatus,OwnerType,IsDeleted,CreateDate)
VALUES (NEWID(),@T,'W1','A-01-01-01','CARTON-A4','',5,0,5,'PENDING','SELF',0,GETDATE());
-- 满（量>=容量）
INSERT INTO T_Stock (Id,TenantId,WarehouseCd,LocationCd,ProductCd,LotNo,PhysicalQty,AllocatedQty,AvailableQty,QcStatus,OwnerType,IsDeleted,CreateDate)
VALUES (NEWID(),@T,'W1','A-01-01-02','CARTON-A4','',50,0,50,'PENDING','SELF',0,GETDATE());
-- Location 容量 + 锁定示例
UPDATE T_Location SET CapacityQty=10 WHERE LocationCd='A-01-01-01';
UPDATE T_Location SET CapacityQty=50 WHERE LocationCd='A-01-01-02';
UPDATE T_Location SET IsBlocked=1 WHERE LocationCd='A-01-01-03';   -- 锁定
-- 在拣：出库单 Picking + 明细
DECLARE @OB nvarchar(20)='OB-QA-001';
INSERT INTO T_OutboundOrder (Id,TenantId,OutboundNo,OutboundType,WarehouseCd,PlannedDate,Status,Priority,IsDeleted,CreateDate)
VALUES (NEWID(),@T,@OB,2,'W1',GETDATE(),3,1,0,GETDATE());            -- Status=3 Picking
INSERT INTO T_OutboundOrderDetail (Id,TenantId,OutboundNo,LineNo,ProductCd,RequiredQty,AllocatedQty,ShippedQty,LocationCd,IsDeleted,CreateDate)
VALUES (NEWID(),@T,@OB,1,'CARTON-A4',5,5,0,'A-01-01-04',0,GETDATE());
INSERT INTO T_Stock (Id,TenantId,WarehouseCd,LocationCd,ProductCd,LotNo,PhysicalQty,AllocatedQty,AvailableQty,QcStatus,OwnerType,IsDeleted,CreateDate)
VALUES (NEWID(),@T,'W1','A-01-01-04','CARTON-A4','',8,5,3,'PENDING','SELF',0,GETDATE());
```

- [ ] **Step 2: 后端全量回归**

Run: `dotnet test CP6.Tests`
Expected: 全绿（P1 1287 + 本计划新增 WmsStockQueryTests，零回归）。

- [ ] **Step 3: 前端三门**

Run: `cd cp6.web && npx vue-tsc --noEmit && npx vitest run && npm run build`
Expected: 0 error / 全绿 / build 成功。

- [ ] **Step 4: gstack 真浏览器 QA（用户监督）**

起隔离环境（后端读 `appsettings.Local.json` 指 `CP6DB_SpaceQA`、前端 vite proxy）→ 灌 Step 1 种子 → gstack headless：
1. 进 `/space/viewer/{siteId}?floorId={floorId}` → 默认状态模式着色：A-01-01-01 蓝(有货)/02 红(满)/03 灰(锁定)/04 黄(在拣)/其余绿(空)。
2. 切"利用率"模式 → 冷暖渐变；切"关闭"→ 回灰。
3. 点"刷新库存" → 图例数据时间更新（I-SPACE-703 语义）。
4. 点库位 → InfoCard 显库存行（状态/量/容量/利用率/主物料）。
5. SearchBox 选"按物料"输 `CARTON-A4` → 定位到命中库位（飞行+高亮，复用 06）。
6. 降级：停 WMS 数据源（或断库）→ 提示"库存数据获取失败"(W-SPACE-701)，3D 结构照常浏览。

截图视觉确认（着色/InfoCard/物料定位/降级），固化到 `docs/superpowers/qa/space-p2-07/README.md`。

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/qa/space-p2-07/
git commit -m "test(space-07): T12 演示种子 + gstack 真浏览器 QA 固化"
```

---

## 完成标准（Definition of Done）

- 后端 `dotnet test CP6.Tests` 全绿（P1 1287 + WmsStockQueryTests，04 零回归）。
- 前端 vue-tsc 0 error / vitest 全绿 / build 成功。
- gstack QA：状态/利用率着色、模式切换、刷新、InfoCard 叠库存、物料定位、降级 全过（截图固化）。
- `IWmsStockQuery` 接真（StubWmsStockQuery 退为测试替身），04 停用校验走真查。
- **遗留（出本计划，见 spec §10 / Plan 08）**：视锥精确裁剪、租户配色 UI、库容混 UOM 精确、QcStatus 入态、08 高级可视化（拣货路径/作业热图/设备）。

---

## Self-Review 记录（写计划时自检）

- **Spec 覆盖**：§3.1 契约→T1；§3.2 BinStatus 派生→T2；§3.4 物料定位→T3/T10/T11；§3.5 端点→T4；§3.6 overlay(三模式/快照/轮询/Highlighter 协同/信息卡/降级)→T6~T11;§3.7 色板→T6/T8；§5 04 回归→T1/T4；§7 测试→各 Task + T12。
- **占位扫描**：无 TBD；每代码步含完整代码。
- **类型一致**：`WmsStockDto`/`WmsLocationHit`/`StockLocateQuery` 前后端字段一致；`binStatusToHex`/`locationUtilization`/`aggregateUtilization` 跨 T6/T7/T9 同签名；DbSet 名 `Stocks`/`Locations`/`OutboundOrders`/`OutboundOrderDetails`/`Pallets` 一致;`OutboundOrderStatus.Picking=3` 一致。
