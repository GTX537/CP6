# Space SP5 连接体计时/通行成本 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把多层拣货路由的竖直边权从纯物理 `|Δz|` 升为可配计时成本（每连接体 `{WaitSec, TravelSecPerFloor}`），优化目标从距离切到时间（拣货工时），堆叠/单层面板同显 距离 + 时间双值。

**Architecture:** 后端先（实体 +2 字段/迁移回填/服务默认+Update/控制器/站点 pick-path VO），前端核心后（`cost.ts` 常量 + `astar` 加 admissibility 标定 `hScale` + `planMultiFloor.ts` 时间图重写），再 UI 接线（编辑器成本输入 + 双值对比面板），末 gstack 真栈 QA。图在前端，SP3 单层链零改（astar 缺省 `hScale=1` 一字节等价）。

**Tech Stack:** .NET 8 / EF Core（CP6.Core/Entity/WebApi/Tests + xUnit）、Vue3.5 + TS + vite + vitest（cp6.web）、Three.js viewer、SQL Server（CP6DB_SpaceQA）+ gstack。

**Spec:** `docs/superpowers/specs/2026-06-29-space-p5-traversal-cost-design.md`（§13 交付顺序即本 plan T1~T11）。

**约定（每条命令照抄）：** bash cwd 每次调用后重置回 `D:\CP6`，故每条命令带 `cd /d/CP6-space-backend && ...`（前端再进 `cp6.web`）。Edit/Read/Write 用 `D:\CP6-space-backend\...` 绝对路径。别碰 `D:\CP6`（wfs-B 工作树）、别碰 `feat/space-p1-backend`（冻结）。分支已在 `feat/space-p5-traversal-cost`。

---

## File Structure

| 文件 | 责任 | Task |
|---|---|---|
| `CP6.Entity/DomainModels/Space/Space_Connector.cs` | +WaitSec/TravelSecPerFloor 字段 | T1 |
| `CP6.Core/Migrations/*_SpaceP5ConnectorCost.cs` | 加两列 + 按类型回填 | T1 |
| `CP6.Entity/DTOs/Space/ConnectorDtos.cs` | DTO/View +字段 + ConnectorUpdateDto | T2 |
| `CP6.Core/Services/Space/IConnectorService.cs` | +UpdateAsync 签名 | T2 |
| `CP6.Core/Services/Space/ConnectorService.cs` | DefaultCost + Create 默认 + UpdateAsync + List 投影 | T2 |
| `CP6.Tests/Space/ConnectorServiceTests.cs` | 默认/显式/Update/502 测 | T2 |
| `CP6.WebApi/Controllers/Space/ConnectorController.cs` | PUT update 端点 | T3 |
| `CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs` | 站点 pick-path connectors VO +成本 | T3 |
| `cp6.web/src/space-viewer/advanced/cost.ts` | 步速常量 + mmToSec/verticalSec + 类型默认 | T4 |
| `cp6.web/src/space-viewer/advanced/cost.spec.ts` | cost 纯逻辑测 | T4 |
| `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts` | astar +hScale=1 参数 | T5 |
| `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts` | astar hScale 测（append） | T5 |
| `cp6.web/src/space-viewer/advanced/multiFloor.ts` | FloorMeta +level | T6 |
| `cp6.web/src/space-viewer/advanced/planMultiFloor.ts` | 时间边权 + Kmin + costMatrixMF + 双值 MFComparison | T6 |
| `cp6.web/src/space-viewer/advanced/planMultiFloor.spec.ts` | 时间图测（重写） | T6 |
| `cp6.web/src/types/space/connector.ts` | ConnectorVO/Create/Update +字段 | T7 |
| `cp6.web/src/types/space/advanced.ts` | SiteConnectorVO +字段 | T7 |
| `cp6.web/src/api/space/connector.ts` | create 带成本 + update | T7 |
| `cp6.web/src/views/space/editor/panels/ConnectorPanel.vue` | 成本输入 + 类型预填 + 编辑 | T8 |
| `cp6.web/src/views/space/stacked/StackedViewer.vue` | mfFloors+level/connectors+成本/双值 compareInfo | T9 |
| `cp6.web/src/views/space/viewer/FloorViewer.vue` | 单层派生时间行 | T9 |
| `docs/superpowers/qa/space-p5-traversal-cost/` | gstack QA 固化 | T11 |

---

## Task 1: 实体 +2 字段 + 迁移 + 回填

**Files:**
- Modify: `CP6.Entity/DomainModels/Space/Space_Connector.cs`
- Create: `CP6.Core/Migrations/*_SpaceP5ConnectorCost.cs`（`dotnet ef` 生成后手改 Up）

> **说明（无独立单测）：** 这是 schema/迁移使能任务；成本默认的行为测在 T2（CreateAsync）。本任务的 gate = build 通过 + `ef has-pending` 干净。

- [ ] **Step 1: 实体加两字段**

在 `Space_Connector.cs` 的 `Name` 属性之后、类闭合 `}` 之前插入：

```csharp
    /// <summary>登乘/门周期固定成本（秒）。竖直边一次性计（Space P5）。</summary>
    public int WaitSec { get; set; }

    /// <summary>每跨一层的行程成本（秒），按两 stop 的 Level 差乘（Space P5）。</summary>
    public int TravelSecPerFloor { get; set; }
```

- [ ] **Step 2: 生成迁移**

Run: `cd /d/CP6-space-backend && dotnet ef migrations add SpaceP5ConnectorCost --project CP6.Core --startup-project CP6.WebApi`
Expected: 生成 `CP6.Core/Migrations/<时间戳>_SpaceP5ConnectorCost.cs`（含两个 `AddColumn<int>` for WaitSec/TravelSecPerFloor，defaultValue 0）+ `.Designer.cs`，无报错。

- [ ] **Step 3: 在迁移 Up() 末尾追加按类型回填 SQL**

打开生成的 `CP6.Core/Migrations/<时间戳>_SpaceP5ConnectorCost.cs`，在 `Up(MigrationBuilder migrationBuilder)` 方法体**最后一行之后**（两个 AddColumn 之后）追加：

```csharp
            migrationBuilder.Sql("UPDATE [Space_Connector] SET [WaitSec]=20,[TravelSecPerFloor]=6  WHERE [ConnectorType]=1;");
            migrationBuilder.Sql("UPDATE [Space_Connector] SET [WaitSec]=0, [TravelSecPerFloor]=15 WHERE [ConnectorType]=2;");
            migrationBuilder.Sql("UPDATE [Space_Connector] SET [WaitSec]=0, [TravelSecPerFloor]=10 WHERE [ConnectorType]=3;");
```

（`Down()` 自动生成的 DropColumn 不动。）

- [ ] **Step 4: 验证 build + 模型快照同步**

Run: `cd /d/CP6-space-backend && dotnet build CP6.WebApi 2>&1 | tail -5 && dotnet ef migrations has-pending-model-changes --project CP6.Core --startup-project CP6.WebApi`
Expected: build `0 Error`；has-pending 输出 `No changes have been made ...`（模型与迁移快照一致）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add CP6.Entity/DomainModels/Space/Space_Connector.cs CP6.Core/Migrations/ && git commit -m "feat(space-p5): Space_Connector +WaitSec/TravelSecPerFloor + 迁移按类型回填"
```

---

## Task 2: 服务默认 + UpdateAsync + DTO + 接口

**Files:**
- Modify: `CP6.Entity/DTOs/Space/ConnectorDtos.cs`
- Modify: `CP6.Core/Services/Space/IConnectorService.cs`
- Modify: `CP6.Core/Services/Space/ConnectorService.cs`
- Test: `CP6.Tests/Space/ConnectorServiceTests.cs`

- [ ] **Step 1: 写失败测试**

在 `ConnectorServiceTests.cs` 的最后一个 `}`（类闭合）之前追加四个测试：

```csharp
    [Fact]
    public async Task Create_with_no_cost_applies_type_default_elevator()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "电梯" }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Equal(20, list[0].WaitSec);
        Assert.Equal(6, list[0].TravelSecPerFloor);
    }

    [Fact]
    public async Task Create_with_explicit_cost_not_overridden()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "S1", ConnectorType = 2, Name = "楼梯", WaitSec = 5, TravelSecPerFloor = 30 }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Equal(5, list[0].WaitSec);
        Assert.Equal(30, list[0].TravelSecPerFloor);
    }

    [Fact]
    public async Task Update_changes_cost_name_type()
    {
        var (_, svc) = Make();
        var site = Guid.NewGuid();
        var cid = await svc.CreateAsync(new ConnectorDto { SiteId = site, ConnectorCode = "E1", ConnectorType = 1, Name = "a" }, "u");
        await svc.UpdateAsync(cid, new ConnectorUpdateDto { Name = "b", ConnectorType = 3, WaitSec = 0, TravelSecPerFloor = 9 }, "u");
        var list = await svc.ListBySiteAsync(site);
        Assert.Equal("b", list[0].Name);
        Assert.Equal(3, list[0].ConnectorType);
        Assert.Equal(9, list[0].TravelSecPerFloor);
    }

    [Fact]
    public async Task Update_missing_throws_E502()
    {
        var (_, svc) = Make();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UpdateAsync(Guid.NewGuid(), new ConnectorUpdateDto { Name = "x", ConnectorType = 1 }, "u"));
        Assert.Equal("E-SPACE-502", ex.Message);
    }
```

- [ ] **Step 2: 运行测试，确认编译失败/红**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests --filter "FullyQualifiedName~ConnectorServiceTests" 2>&1 | tail -15`
Expected: 编译失败（`ConnectorUpdateDto` / `UpdateAsync` / `ConnectorView.WaitSec` 不存在）。

- [ ] **Step 3: DTO 加字段 + ConnectorUpdateDto**

`ConnectorDtos.cs`：给 `ConnectorDto` 在 `Name` 之后加：

```csharp
    public int WaitSec { get; set; }
    public int TravelSecPerFloor { get; set; }
```

给 `ConnectorView` 在 `Name` 之后加同样两行；并在文件末尾追加：

```csharp
public class ConnectorUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public int ConnectorType { get; set; } = 1;
    public int WaitSec { get; set; }
    public int TravelSecPerFloor { get; set; }
}
```

- [ ] **Step 4: 接口加 UpdateAsync**

`IConnectorService.cs` 在 `Task DeleteAsync(Guid id);` 之后加：

```csharp
    Task UpdateAsync(Guid id, ConnectorUpdateDto d, string? user);
```

- [ ] **Step 5: 服务实现默认 + Update + 投影**

`ConnectorService.cs`：在类内（`ConnectorService(CP6Context db)` 构造之后）加默认表：

```csharp
    private static (int wait, int perFloor) DefaultCost(int type) => type switch
    {
        1 => (20, 6),   // 电梯
        2 => (0, 15),   // 楼梯
        3 => (0, 10),   // 坡道
        _ => (0, 10),
    };
```

`ListBySiteAsync` 的投影对象（`new ConnectorView { ... }`）补两字段（紧跟 `Name = c.Name,`）：

```csharp
            WaitSec = c.WaitSec, TravelSecPerFloor = c.TravelSecPerFloor,
```

`CreateAsync` 内、构造 `e` 前插入默认逻辑，并在 `new Space_Connector { ... }` 里补两字段：

```csharp
        var (wait, perFloor) = (d.WaitSec <= 0 && d.TravelSecPerFloor <= 0)
            ? DefaultCost(d.ConnectorType) : (d.WaitSec, d.TravelSecPerFloor);
```

把 `new Space_Connector { ... Name = d.Name, Creator = user, ... }` 改为在 `Name = d.Name,` 之后加 `WaitSec = wait, TravelSecPerFloor = perFloor,`。

文件末尾、`DeleteAsync` 之后、类闭合前加 UpdateAsync：

```csharp
    public async Task UpdateAsync(Guid id, ConnectorUpdateDto d, string? user)
    {
        var e = await _db.Space_Connectors.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new InvalidOperationException("E-SPACE-502");
        e.Name = d.Name; e.ConnectorType = d.ConnectorType;
        e.WaitSec = d.WaitSec; e.TravelSecPerFloor = d.TravelSecPerFloor;
        e.Modifier = user; e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
```

- [ ] **Step 6: 运行测试，确认绿**

Run: `cd /d/CP6-space-backend && dotnet test CP6.Tests --filter "FullyQualifiedName~ConnectorServiceTests" 2>&1 | tail -8`
Expected: `Passed!` 全部（含原 3 测 + 新 4 测）。

- [ ] **Step 7: Commit**

```bash
cd /d/CP6-space-backend && git add CP6.Entity/DTOs/Space/ConnectorDtos.cs CP6.Core/Services/Space/IConnectorService.cs CP6.Core/Services/Space/ConnectorService.cs CP6.Tests/Space/ConnectorServiceTests.cs && git commit -m "feat(space-p5): 连接体成本类型默认 + UpdateAsync + DTO/View 透出（4 测）"
```

---

## Task 3: 控制器 PUT + 站点 pick-path connectors VO

**Files:**
- Modify: `CP6.WebApi/Controllers/Space/ConnectorController.cs`
- Modify: `CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs:120-124`

> **说明（无独立单测）：** 控制器 VO 是匿名投影 + 需全 DI/DB，本仓既有 `SitePickPathTests` 只测纯静态 `ComputeFloorZ`，不测匿名投影。本任务 gate = build；wire 正确性由 T2（服务往返成本）+ T11 gstack QA（§10 #1 断言 VO 带成本）闭环证。

- [ ] **Step 1: 控制器加 PUT update 端点**

`ConnectorController.cs`：在 `[HttpDelete("connector/{id:guid}")] ... Delete` 方法之前（或之后）加：

```csharp
    [HttpPut("connector/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ConnectorUpdateDto d)
    {
        try { await _svc.UpdateAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }
```

- [ ] **Step 2: 站点 pick-path connectors VO 补成本**

`SpaceAdvancedController.cs` 把 `var connectors = conns.Select(c => new { ... })`（约 L120-124）改为：

```csharp
        var connectors = conns.Select(c => new
        {
            connectorCode = c.ConnectorCode, type = c.ConnectorType,
            waitSec = c.WaitSec, travelSecPerFloor = c.TravelSecPerFloor,
            stops = connStops.Where(s => s.ConnectorId == c.Id).Select(s => new { floorId = s.FloorId, x = s.X, y = s.Y }).ToList()
        }).ToList();
```

- [ ] **Step 3: 验证 build**

Run: `cd /d/CP6-space-backend && dotnet build CP6.WebApi 2>&1 | tail -5`
Expected: `0 Error`。

- [ ] **Step 4: Commit**

```bash
cd /d/CP6-space-backend && git add CP6.WebApi/Controllers/Space/ConnectorController.cs CP6.WebApi/Controllers/Space/SpaceAdvancedController.cs && git commit -m "feat(space-p5): PUT /space/connector/{id} 更新 + 站点 pick-path connectors VO 透出成本"
```

---

## Task 4: 前端 cost.ts（常量 + 换算）

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/cost.ts`
- Test: `cp6.web/src/space-viewer/advanced/cost.spec.ts`

- [ ] **Step 1: 写失败测试**

`cost.spec.ts`：

```ts
import { describe, it, expect } from 'vitest'
import { mmToSec, verticalSec, WALK_SPEED_MMPS, TYPE_DEFAULT_COST } from './cost'

describe('cost', () => {
  it('mmToSec converts mm to seconds at walk speed', () => {
    expect(mmToSec(1200)).toBeCloseTo(1)
    expect(mmToSec(6000)).toBeCloseTo(5)
    expect(WALK_SPEED_MMPS).toBe(1200)
  })
  it('verticalSec = wait + perFloor * |span|', () => {
    expect(verticalSec(20, 6, 1)).toBe(26)
    expect(verticalSec(20, 6, 3)).toBe(38)
    expect(verticalSec(0, 15, 2)).toBe(30)
    expect(verticalSec(20, 6, -2)).toBe(32)
  })
  it('type defaults present for elevator/stairs/ramp', () => {
    expect(TYPE_DEFAULT_COST[1]).toEqual({ waitSec: 20, travelSecPerFloor: 6 })
    expect(TYPE_DEFAULT_COST[2]).toEqual({ waitSec: 0, travelSecPerFloor: 15 })
    expect(TYPE_DEFAULT_COST[3]).toEqual({ waitSec: 0, travelSecPerFloor: 10 })
  })
})
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/cost.spec.ts 2>&1 | tail -12`
Expected: FAIL（`Cannot find module './cost'`）。

- [ ] **Step 3: 实现 cost.ts**

```ts
// cp6.web/src/space-viewer/advanced/cost.ts —— 通行成本常量与换算（时间=秒，SP5）
export const WALK_SPEED_MMPS = 1200 // 水平步行/叉车混合默认 1.2 m/s

/** 水平物理距离(mm) → 时间(秒)。 */
export const mmToSec = (mm: number): number => mm / WALK_SPEED_MMPS

/** 竖直边时间(秒)：等待(每停一次门周期) + 每层行程 × 跨层数。 */
export const verticalSec = (waitSec: number, perFloorSec: number, floorsSpanned: number): number =>
  waitSec + perFloorSec * Math.abs(floorsSpanned)

/** 编辑器预填用类型默认（后端 ConnectorService.DefaultCost 为持久化权威，此处镜像供 UX）。 */
export const TYPE_DEFAULT_COST: Record<number, { waitSec: number; travelSecPerFloor: number }> = {
  1: { waitSec: 20, travelSecPerFloor: 6 },
  2: { waitSec: 0, travelSecPerFloor: 15 },
  3: { waitSec: 0, travelSecPerFloor: 10 },
}
```

- [ ] **Step 4: 运行测试，确认绿**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/cost.spec.ts 2>&1 | tail -8`
Expected: PASS（3 测）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/cost.ts cp6.web/src/space-viewer/advanced/cost.spec.ts && git commit -m "feat(space-p5): cost.ts 步速常量 + mmToSec/verticalSec + 类型默认（3 测）"
```

---

## Task 5: astar 加 admissibility 标定参数 hScale

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts:97-109`
- Test: `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts`（append）

- [ ] **Step 1: 写失败测试**

在 `PickPathPlanner.spec.ts` 文件末尾追加（若文件顶部 import 未含 `astar`，把 `astar` 加进既有 `from './PickPathPlanner'` 的 import 列表）：

```ts
import { astar } from './PickPathPlanner'

describe('astar hScale (admissibility 标定)', () => {
  const adj = new Map<string, Array<{ to: string; w: number }>>([
    ['A', [{ to: 'B', w: 10 }]],
    ['B', [{ to: 'A', w: 10 }, { to: 'C', w: 10 }]],
    ['C', [{ to: 'B', w: 10 }]],
  ])
  const pts: Record<string, { x: number; y: number }> = { A: { x: 0, y: 0 }, B: { x: 10, y: 0 }, C: { x: 20, y: 0 } }
  const nodePt = (k: string) => pts[k]!

  it('default hScale equals explicit 1 (SP3 zero-regression)', () => {
    expect(astar(adj, 'A', 'C', nodePt)).toEqual(astar(adj, 'A', 'C', nodePt, 1))
  })
  it('still finds optimal path with a tiny hScale (conservative heuristic)', () => {
    expect(astar(adj, 'A', 'C', nodePt, 0.0001)).toEqual(['A', 'B', 'C'])
  })
})
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts 2>&1 | tail -12`
Expected: 类型/调用失败（astar 第 5 参数 hScale 不存在）。

- [ ] **Step 3: astar 加可选 hScale 参数**

`PickPathPlanner.ts`：把 `astar` 签名（L97-102）改为加第 5 参数：

```ts
export function astar(
  adj: Map<string, Array<{ to: string; w: number }>>,
  start: string,
  end: string,
  nodePt: (k: string) => { x: number; y: number; z?: number },
  hScale = 1,
): string[] | null {
```

把启发式（L108-109）改为乘 `hScale`：

```ts
  const h = (a: { x: number; y: number; z?: number }, b: { x: number; y: number; z?: number }): number =>
    Math.hypot(a.x - b.x, a.y - b.y, (a.z ?? 0) - (b.z ?? 0)) * hScale
```

- [ ] **Step 4: 运行测试，确认绿（含既有 SP3 测零回归）**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts 2>&1 | tail -8`
Expected: PASS（既有 SP3 测 + 新 2 测全绿）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PickPathPlanner.ts cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts && git commit -m "feat(space-p5): astar 加可选 hScale=1（启发标定，SP3 距离图缺省零回归）"
```

---

## Task 6: 多层时间图重写（核心）

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/multiFloor.ts`
- Modify: `cp6.web/src/space-viewer/advanced/planMultiFloor.ts`（全文替换）
- Test: `cp6.web/src/space-viewer/advanced/planMultiFloor.spec.ts`（全文重写）

- [ ] **Step 1: 重写 spec（失败测试）**

把 `planMultiFloor.spec.ts` 全文替换为：

```ts
import { describe, it, expect } from 'vitest'
import { buildMultiFloorGraph, pathBetweenMF, costMatrixMF, planPickComparisonMF } from './planMultiFloor'
import { mmToSec, verticalSec, WALK_SPEED_MMPS } from './cost'

const F1 = 'F1', F2 = 'F2'
const floors = [{ floorId: F1, z: 0, level: 1 }, { floorId: F2, z: 6000, level: 2 }]
const aislesByFloor = new Map([
  [F1, [{ aisleCode: 'H1', centerline: '[[0,500],[1000,500]]' }]],
  [F2, [{ aisleCode: 'H2', centerline: '[[0,500],[1000,500]]' }]],
])
const E1 = { connectorCode: 'E1', type: 1, waitSec: 20, travelSecPerFloor: 6, stops: [{ floorId: F1, x: 500, y: 500 }, { floorId: F2, x: 500, y: 500 }] }
const connectors = [E1]

describe('buildMultiFloorGraph (time weights)', () => {
  it('vertical connector edge = verticalSec(wait,perFloor,|Δlevel|)', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const up = g.adj.get('F1:500,500')!.find((e) => e.to === 'F2:500,500')
    expect(up).toBeTruthy()
    expect(up!.w).toBeCloseTo(verticalSec(20, 6, 1)) // 26s
  })
  it('horizontal aisle edge = mmToSec(distance)', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const e = g.adj.get('F1:0,500')!.find((x) => x.to === 'F1:1000,500')
    expect(e!.w).toBeCloseTo(mmToSec(1000))
  })
  it('hScale = Kmin = global min(time/physLen); horizontal dominates here', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    expect(g.hScale).toBeCloseTo(1 / WALK_SPEED_MMPS)
  })
})

describe('pathBetweenMF (time)', () => {
  it('crosses floors via connector; z spans 0→6000; time > vertical 26s', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const r = pathBetweenMF(g, { floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 })
    expect(r.degraded).toBe(false)
    expect(Math.min(...r.points.map((p) => p.z))).toBeCloseTo(0)
    expect(Math.max(...r.points.map((p) => p.z))).toBeCloseTo(6000)
    expect(r.time).toBeGreaterThan(verticalSec(20, 6, 1))
  })
  it('costMatrixMF symmetric + includes vertical time', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const stops = [{ floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 }]
    const m = costMatrixMF(g, stops)
    expect(m[0]![1]).toBeCloseTo(m[1]![0]!)
    expect(m[0]![1]).toBeGreaterThan(verticalSec(20, 6, 1))
  })
})

describe('planPickComparisonMF (dual distance+time)', () => {
  const stops = [
    { floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 },
    { floorId: F1, x: 900, y: 520 }, { floorId: F2, x: 100, y: 520 },
  ]
  it('returns both Mm and Sec; optimizedSec ≤ actualSec; timeSavings ≥ 0; order[0]=0', () => {
    const cmp = planPickComparisonMF(floors, aislesByFloor, connectors, stops)
    expect(cmp.order[0]).toBe(0)
    expect(cmp.optimizedSec).toBeLessThanOrEqual(cmp.actualSec + 1e-6)
    expect(cmp.timeSavingsPct).toBeGreaterThanOrEqual(0)
    expect(cmp.actualMm).toBeGreaterThan(0)
    expect(cmp.actualSec).toBeGreaterThan(0)
    expect(cmp.actual.points.some((p) => p.z > 0)).toBe(true)
  })
  it('pricier elevator raises actualSec (cost wired through)', () => {
    const cheap = planPickComparisonMF(floors, aislesByFloor, [E1], stops)
    const dear = planPickComparisonMF(floors, aislesByFloor, [{ ...E1, waitSec: 120, travelSecPerFloor: 60 }], stops)
    expect(dear.actualSec).toBeGreaterThan(cheap.actualSec)
  })
  it('single stop → zero distance/time, savings 0', () => {
    const cmp = planPickComparisonMF(floors, aislesByFloor, connectors, [{ floorId: F1, x: 100, y: 520 }])
    expect(cmp.actualMm).toBe(0)
    expect(cmp.actualSec).toBe(0)
    expect(cmp.timeSavingsPct).toBe(0)
  })
})
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/planMultiFloor.spec.ts 2>&1 | tail -15`
Expected: FAIL（`costMatrixMF` 未导出、FloorMeta 无 level、cmp 无 actualSec/timeSavingsPct 等）。

- [ ] **Step 3: multiFloor.ts FloorMeta +level**

把 `multiFloor.ts` 的 `FloorMeta` 一行改为：

```ts
export interface FloorMeta { floorId: string; z: number; level: number }  // z=堆叠标高 mm；level=楼层序（算跨层数）
```

- [ ] **Step 4: 全文替换 planMultiFloor.ts**

把 `planMultiFloor.ts` 全文替换为：

```ts
// cp6.web/src/space-viewer/advanced/planMultiFloor.ts —— 多层图 + 跨层路径（承 SP4，边权=时间秒，SP5）
import { buildCenterlineGraph, key, astar, type Pt } from './PickPathPlanner'
import { mfKey, dist3, type Pt3, type FloorMeta } from './multiFloor'
import { optimizeOrder, routeLengthByOrder } from './routeOptimize'
import { mmToSec, verticalSec, WALK_SPEED_MMPS } from './cost'

export interface MFGraph {
  nodes: Map<string, Pt3>
  adj: Map<string, Array<{ to: string; w: number }>>   // w = 时间(秒)
  segments: Array<{ a: Pt; b: Pt; floorId: string }>
  floorZ: Map<string, number>
  floorLevel: Map<string, number>
  hScale: number                                        // Kmin = 全图 min(边时间/边物理长)，A* admissible 标定
}
export interface AisleVOLite { aisleCode: string; centerline: string }
export interface ConnectorPath {
  connectorCode: string; type: number
  waitSec: number; travelSecPerFloor: number
  stops: Array<{ floorId: string; x: number; y: number }>
}

function addMFEdge(g: MFGraph, ka: string, pa: Pt3, kb: string, pb: Pt3, w: number): void {
  if (ka === kb) return
  if (!g.nodes.has(ka)) g.nodes.set(ka, pa)
  if (!g.nodes.has(kb)) g.nodes.set(kb, pb)
  if (!g.adj.has(ka)) g.adj.set(ka, [])
  if (!g.adj.has(kb)) g.adj.set(kb, [])
  if (!g.adj.get(ka)!.some((e) => e.to === kb)) g.adj.get(ka)!.push({ to: kb, w })
  if (!g.adj.get(kb)!.some((e) => e.to === ka)) g.adj.get(kb)!.push({ to: ka, w })
}

/** nearestAccess 的 segments 版（投影到最近段取两端）。 */
export function nearestAccessOnSegments(segs: Array<{ a: Pt; b: Pt }>, p: Pt): { segA: Pt; segB: Pt } | null {
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

/** 合并各层 SP3 子图（边权 mm→秒）+ 连接体接入（水平秒）+ 同连接体相邻层竖直边（verticalSec）。
 *  hScale = Kmin = 全图 min(边时间/边物理长)。 */
export function buildMultiFloorGraph(
  floors: FloorMeta[],
  aislesByFloor: Map<string, AisleVOLite[]>,
  connectors: ConnectorPath[],
): MFGraph {
  const zOf = new Map(floors.map((f) => [f.floorId, f.z]))
  const levelOf = new Map(floors.map((f) => [f.floorId, f.level]))
  const g: MFGraph = { nodes: new Map(), adj: new Map(), segments: [], floorZ: zOf, floorLevel: levelOf, hScale: 1 / WALK_SPEED_MMPS }
  let minRate = 1 / WALK_SPEED_MMPS // 水平边 rate 恒定，作 Kmin 起点

  // 1) 各层 SP3 子图（mm 边权）→ 前缀合并 + mm→秒
  for (const f of floors) {
    const z = f.z
    const g2d = buildCenterlineGraph(aislesByFloor.get(f.floorId) ?? [])
    for (const [k2d, pt] of g2d.nodes) g.nodes.set(`${f.floorId}:${k2d}`, { x: pt.x, y: pt.y, z })
    for (const [k2d, list] of g2d.adj) g.adj.set(`${f.floorId}:${k2d}`, list.map((e) => ({ to: `${f.floorId}:${e.to}`, w: mmToSec(e.w) })))
    for (const s of g2d.segments) g.segments.push({ a: s.a, b: s.b, floorId: f.floorId })
  }

  // 2) 连接体：每 stop 接入本层最近巷道（水平秒）；同连接体相邻层竖直边（verticalSec）
  for (const c of connectors) {
    const placed = c.stops.filter((s) => zOf.has(s.floorId)).map((s) => ({ s, z: zOf.get(s.floorId)!, level: levelOf.get(s.floorId)! }))
    for (const { s, z } of placed) {
      const floorSegs = g.segments.filter((seg) => seg.floorId === s.floorId)
      const acc = nearestAccessOnSegments(floorSegs, { x: s.x, y: s.y })
      const nodeK = mfKey(s.floorId, s)
      const nodeP: Pt3 = { x: s.x, y: s.y, z }
      if (acc) {
        const dA = Math.hypot(s.x - acc.segA.x, s.y - acc.segA.y)
        const dB = Math.hypot(s.x - acc.segB.x, s.y - acc.segB.y)
        addMFEdge(g, nodeK, nodeP, `${s.floorId}:${key(acc.segA)}`, { x: acc.segA.x, y: acc.segA.y, z }, mmToSec(dA))
        addMFEdge(g, nodeK, nodeP, `${s.floorId}:${key(acc.segB)}`, { x: acc.segB.x, y: acc.segB.y, z }, mmToSec(dB))
      } else {
        g.nodes.set(nodeK, nodeP)
      }
    }
    const sorted = placed.slice().sort((a, b) => a.z - b.z)
    for (let i = 0; i + 1 < sorted.length; i++) {
      const a = sorted[i]!, b = sorted[i + 1]!
      const span = Math.abs(a.level - b.level)
      const w = verticalSec(c.waitSec, c.travelSecPerFloor, span)
      const physLen = Math.abs(a.z - b.z)
      if (physLen > 0) minRate = Math.min(minRate, w / physLen)
      addMFEdge(g, mfKey(a.s.floorId, a.s), { x: a.s.x, y: a.s.y, z: a.z },
                   mfKey(b.s.floorId, b.s), { x: b.s.x, y: b.s.y, z: b.z }, w)
    }
  }
  g.hScale = minRate
  return g
}

export interface MFStop { floorId: string; x: number; y: number }
export interface MFRoute { points: Pt3[]; totalDistance: number; totalTime: number; degraded: boolean }

export function polyDist3(pts: Pt3[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += dist3(pts[i - 1]!, pts[i]!)
  return d
}

/** 取某层标高（O(1) 查 g.floorZ；含无巷道/无 stop 的层，避免退化端点落 z=0）。 */
function zOfFloor(g: MFGraph, fid: string): number {
  return g.floorZ.get(fid) ?? 0
}

/** 沿 astar 返回的 key 序累计边时间（adj 含临时 FA/FB）。 */
function pathCost(adj: Map<string, Array<{ to: string; w: number }>>, keys: string[]): number {
  let c = 0
  for (let i = 0; i + 1 < keys.length; i++) {
    const e = adj.get(keys[i]!)?.find((x) => x.to === keys[i + 1]!)
    if (e) c += e.w
  }
  return c
}

/** 跨层相邻两拣货点：各端投影本层巷道接入（FA/FB），astar 跑多层时间图。不连通→直连 degraded（时间=直线÷步速）。 */
export function pathBetweenMF(g: MFGraph, a: MFStop, b: MFStop): { points: Pt3[]; time: number; degraded: boolean } {
  const za = zOfFloor(g, a.floorId), zb = zOfFloor(g, b.floorId)
  const pa: Pt3 = { x: a.x, y: a.y, z: za }, pb: Pt3 = { x: b.x, y: b.y, z: zb }

  const accA = nearestAccessOnSegments(g.segments.filter((s) => s.floorId === a.floorId), { x: a.x, y: a.y })
  const accB = nearestAccessOnSegments(g.segments.filter((s) => s.floorId === b.floorId), { x: b.x, y: b.y })
  if (!accA || !accB) return { points: [pa, pb], time: mmToSec(dist3(pa, pb)), degraded: true }

  const adj = new Map<string, Array<{ to: string; w: number }>>()
  for (const [k, list] of g.adj) adj.set(k, list.slice())
  const FA = 'FA', FB = 'FB'
  const link = (n: string, p: MFStop, segA: Pt, segB: Pt) => {
    const ka = `${p.floorId}:${key(segA)}`, kb = `${p.floorId}:${key(segB)}`
    adj.set(n, [{ to: ka, w: mmToSec(Math.hypot(p.x - segA.x, p.y - segA.y)) }, { to: kb, w: mmToSec(Math.hypot(p.x - segB.x, p.y - segB.y)) }])
    adj.get(ka)?.push({ to: n, w: mmToSec(Math.hypot(p.x - segA.x, p.y - segA.y)) })
    adj.get(kb)?.push({ to: n, w: mmToSec(Math.hypot(p.x - segB.x, p.y - segB.y)) })
  }
  link(FA, a, accA.segA, accA.segB)
  link(FB, b, accB.segA, accB.segB)

  const nodePt = (k: string): Pt3 => (k === FA ? pa : k === FB ? pb : g.nodes.get(k)!)
  const path = astar(adj, FA, FB, nodePt, g.hScale)
  if (!path) return { points: [pa, pb], time: mmToSec(dist3(pa, pb)), degraded: true }
  return { points: path.map(nodePt), time: pathCost(adj, path), degraded: false }
}

/** 拣货点两两时间矩阵（秒；degraded 段记直线÷步速）。对称。 */
export function costMatrixMF(g: MFGraph, stops: MFStop[], degradedPairs?: { count: number }): number[][] {
  const n = stops.length
  const m: number[][] = Array.from({ length: n }, () => new Array<number>(n).fill(0))
  for (let i = 0; i < n; i++) for (let j = i + 1; j < n; j++) {
    const seg = pathBetweenMF(g, stops[i]!, stops[j]!)
    m[i]![j] = seg.time; m[j]![i] = seg.time
    if (seg.degraded && degradedPairs) degradedPairs.count++
  }
  return m
}

export interface MFComparison {
  actual: MFRoute; optimized: MFRoute; order: number[]
  actualMm: number; optimizedMm: number          // 距离（几何，参考）
  actualSec: number; optimizedSec: number        // 时间（优化目标）
  timeSavingsPct: number; degradedPairCount: number
}

function planRouteOnMFGraph(g: MFGraph, stops: MFStop[]): MFRoute {
  if (stops.length < 2) {
    return { points: stops.map((s) => ({ x: s.x, y: s.y, z: zOfFloor(g, s.floorId) })), totalDistance: 0, totalTime: 0, degraded: false }
  }
  const points: Pt3[] = []
  let degraded = false, totalTime = 0
  for (let i = 0; i + 1 < stops.length; i++) {
    const seg = pathBetweenMF(g, stops[i]!, stops[i + 1]!)
    degraded = degraded || seg.degraded
    totalTime += seg.time
    const pts = i === 0 ? seg.points : seg.points.slice(1)
    points.push(...pts)
  }
  return { points, totalDistance: polyDist3(points), totalTime, degraded }
}

/** what-if 跨层重排对比：actual=LineNo 序，optimized=NN+2opt（时间矩阵，以 actual 为 baseline 兜底，强保证 ≤ actual 时间）。 */
export function planPickComparisonMF(
  floors: FloorMeta[], aislesByFloor: Map<string, AisleVOLite[]>, connectors: ConnectorPath[], stops: MFStop[],
): MFComparison {
  const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
  const actual = planRouteOnMFGraph(g, stops)
  if (stops.length < 2) {
    return { actual, optimized: actual, order: stops.map((_, i) => i), actualMm: actual.totalDistance, optimizedMm: actual.totalDistance, actualSec: actual.totalTime, optimizedSec: actual.totalTime, timeSavingsPct: 0, degradedPairCount: 0 }
  }
  const degradedPairs = { count: 0 }
  const matrix = costMatrixMF(g, stops, degradedPairs)
  const actualOrder = stops.map((_, i) => i)
  const candidate = optimizeOrder(matrix)
  const order = routeLengthByOrder(matrix, candidate) + 1e-9 < routeLengthByOrder(matrix, actualOrder) ? candidate : actualOrder
  const optimized = planRouteOnMFGraph(g, order.map((i) => stops[i]!))
  const actualSec = actual.totalTime, optimizedSec = optimized.totalTime
  const timeSavingsPct = actualSec === 0 ? 0 : Math.max(0, ((actualSec - optimizedSec) / actualSec) * 100)
  return { actual, optimized, order, actualMm: actual.totalDistance, optimizedMm: optimized.totalDistance, actualSec, optimizedSec, timeSavingsPct, degradedPairCount: degradedPairs.count }
}
```

- [ ] **Step 5: 运行测试，确认绿**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/planMultiFloor.spec.ts 2>&1 | tail -12`
Expected: PASS（8 测全绿）。

- [ ] **Step 6: 全量 vitest + type-check 确认无连带破坏**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run 2>&1 | tail -6 && npm run type-check 2>&1 | tail -8`
Expected: vitest 全绿；type-check 0 错（注意 StackedViewer.vue 仍用 `cmp.savingsPct` 会在 T9 改；若此处 type-check 报 StackedViewer `savingsPct` 不存在，属预期 —— 本步只跑 vitest 必须全绿，type-check 的 StackedViewer 报错留 T9 修。若想此步 type-check 干净，可顺手做 T9 的 StackedViewer 改动再 commit，但推荐按任务序，T9 统一收口）。

> **注：** 因 `MFComparison.savingsPct`→`timeSavingsPct` 重命名，`StackedViewer.vue:122` 会暂时 type 报错，T9 修复。vitest 不受影响（StackedViewer 无单测）。

- [ ] **Step 7: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/multiFloor.ts cp6.web/src/space-viewer/advanced/planMultiFloor.ts cp6.web/src/space-viewer/advanced/planMultiFloor.spec.ts && git commit -m "feat(space-p5): 多层时间图核心 — verticalSec 竖直边/mmToSec 水平边/Kmin admissible/costMatrixMF/双值 MFComparison（8 测）"
```

---

## Task 7: 前端类型 + api

**Files:**
- Modify: `cp6.web/src/types/space/connector.ts`
- Modify: `cp6.web/src/types/space/advanced.ts`
- Modify: `cp6.web/src/api/space/connector.ts`

> **说明：** 类型/api 定义，被 T8/T9 消费；gate = type-check（无单测）。

- [ ] **Step 1: connector 类型加字段 + Update**

`types/space/connector.ts` 全文替换为：

```ts
export interface ConnectorStopVO { floorId: string; x: number; y: number }
export interface ConnectorVO { id: string; connectorCode: string; connectorType: number; name: string; waitSec: number; travelSecPerFloor: number; stops: ConnectorStopVO[] }
export interface ConnectorCreate { siteId: string; connectorCode: string; connectorType: number; name: string; waitSec: number; travelSecPerFloor: number }
export interface ConnectorUpdate { name: string; connectorType: number; waitSec: number; travelSecPerFloor: number }
```

- [ ] **Step 2: SiteConnectorVO 加字段**

`types/space/advanced.ts` 把 `SiteConnectorVO` 一行改为：

```ts
export interface SiteConnectorVO { connectorCode: string; type: number; waitSec: number; travelSecPerFloor: number; stops: Array<{ floorId: string; x: number; y: number }> }
```

- [ ] **Step 3: api 加 update + create 带成本**

`api/space/connector.ts`：把 import 行改为含 `ConnectorUpdate`：

```ts
import type { ConnectorVO, ConnectorCreate, ConnectorStopVO, ConnectorUpdate } from '@/types/space/connector'
```

在 `create(...)` 之后加 `update`：

```ts
  update(id: string, d: ConnectorUpdate) {
    return http.put<unknown, Envelope<null>>(`/space/connector/${id}`, d)
  },
```

（`create` 签名不变，`ConnectorCreate` 现含成本字段，调用方 T8 传齐即可。）

- [ ] **Step 4: type-check**

Run: `cd /d/CP6-space-backend/cp6.web && npm run type-check 2>&1 | tail -10`
Expected: 仅剩 T6 注记的 `StackedViewer.vue` `savingsPct` 报错（T9 修）+ `ConnectorPanel.vue` 暂未传 waitSec/travelSecPerFloor 的 create 报错（T8 修）。其余 0 错。本步不强求全绿（消费方在 T8/T9）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/types/space/connector.ts cp6.web/src/types/space/advanced.ts cp6.web/src/api/space/connector.ts && git commit -m "feat(space-p5): 前端连接体类型/SiteConnectorVO +成本字段 + connectorApi.update"
```

---

## Task 8: 编辑器 ConnectorPanel 成本输入 + 类型预填 + 编辑

**Files:**
- Modify: `cp6.web/src/views/space/editor/panels/ConnectorPanel.vue`

> **说明（无 vitest，SFC）：** gate = type-check + build；交互在 T11 gstack QA（§10 #5）。

- [ ] **Step 1: script 加 import + 表单字段 + 预填 watch + 保存成本**

`ConnectorPanel.vue` `<script setup>`：

把 import 段补：

```ts
import { connectorApi } from '@/api/space/connector'
import type { ConnectorVO, ConnectorUpdate } from '@/types/space/connector'
import { TYPE_DEFAULT_COST } from '@/space-viewer/advanced/cost'
```

（`watch` 已从 vue 导入；若没有则把 `import { ref, onMounted, watch } from 'vue'` 保持。）

把 `const form = ref({ connectorCode: '', connectorType: 1, name: '' })` 改为：

```ts
const form = ref({ connectorCode: '', connectorType: 1, name: '', waitSec: 20, travelSecPerFloor: 6 })

// 选类型时预填成本默认（用户可改）
watch(() => form.value.connectorType, (tp) => {
  const d = TYPE_DEFAULT_COST[tp]
  if (d) { form.value.waitSec = d.waitSec; form.value.travelSecPerFloor = d.travelSecPerFloor }
})
```

把 `createConnector` 内的 `connectorApi.create({...})` 调用补成本字段：

```ts
    await connectorApi.create({
      siteId: props.siteId,
      connectorCode: code,
      connectorType: form.value.connectorType,
      name: form.value.name.trim(),
      waitSec: form.value.waitSec,
      travelSecPerFloor: form.value.travelSecPerFloor,
    })
```

把重置行 `form.value = { connectorCode: '', connectorType: 1, name: '' }` 改为：

```ts
    form.value = { connectorCode: '', connectorType: 1, name: '', waitSec: 20, travelSecPerFloor: 6 }
```

在 `removeConnector` 之后加保存成本方法：

```ts
async function saveCost(c: ConnectorVO): Promise<void> {
  try {
    const d: ConnectorUpdate = { name: c.name, connectorType: c.connectorType, waitSec: c.waitSec, travelSecPerFloor: c.travelSecPerFloor }
    await connectorApi.update(c.id, d)
    ElMessage.success(t('成本已保存'))
  } catch {
    ElMessage.error(t('保存成本失败'))
  }
}
```

- [ ] **Step 2: template 加成本输入（新建表单 + 列表项）**

在新建表单的 `<el-form-item :label="t('名称')">...</el-form-item>` 之后加两项：

```html
      <el-form-item :label="t('等待秒')">
        <el-input-number v-model="form.waitSec" :min="0" :step="1" controls-position="right" style="width: 100%" />
      </el-form-item>
      <el-form-item :label="t('每层秒')">
        <el-input-number v-model="form.travelSecPerFloor" :min="0" :step="1" controls-position="right" style="width: 100%" />
      </el-form-item>
```

在列表项 `conn-item` 内、`<div class="conn-code">...</div>` 之后加成本编辑行：

```html
        <div class="conn-cost">
          <span class="cost-label">{{ t('等待秒') }}</span>
          <el-input-number v-model="c.waitSec" :min="0" :step="1" size="small" controls-position="right" style="width: 96px" />
          <span class="cost-label">{{ t('每层秒') }}</span>
          <el-input-number v-model="c.travelSecPerFloor" :min="0" :step="1" size="small" controls-position="right" style="width: 96px" />
          <el-button size="small" type="primary" plain @click="saveCost(c)">{{ t('保存成本') }}</el-button>
        </div>
```

- [ ] **Step 3: style 加 conn-cost（可选美化）**

在 `<style scoped>` 内加：

```css
.conn-cost {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  margin: 6px 0;
}
.cost-label {
  font-size: 11px;
  color: #666;
}
```

- [ ] **Step 4: type-check + build**

Run: `cd /d/CP6-space-backend/cp6.web && npm run type-check 2>&1 | tail -8`
Expected: ConnectorPanel 相关 0 错（剩 StackedViewer 的 savingsPct 留 T9）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/views/space/editor/panels/ConnectorPanel.vue && git commit -m "feat(space-p5): ConnectorPanel 成本输入 + 类型预填 + 列表项编辑保存成本"
```

---

## Task 9: 对比面板双值接线（StackedViewer + FloorViewer）

**Files:**
- Modify: `cp6.web/src/views/space/stacked/StackedViewer.vue:101,107,119-122`
- Modify: `cp6.web/src/views/space/viewer/FloorViewer.vue`（import + L279-282）

> **说明（无 vitest，SFC）：** gate = type-check + build；视觉在 T11 gstack QA（§10 #2/#4）。

- [ ] **Step 1: StackedViewer mfFloors+level / connectors+成本 / 双值 compareInfo**

`StackedViewer.vue` `onLoadPath`：

把 L101 `const mfFloors = d.floors.map((f) => ({ floorId: f.floorId, z: f.z }))` 改为：

```ts
    const mfFloors = d.floors.map((f) => ({ floorId: f.floorId, z: f.z, level: f.level }))
```

把 L107 `const connectors = d.connectors.map((c) => ({ connectorCode: c.connectorCode, type: c.type, stops: c.stops }))` 改为：

```ts
    const connectors = d.connectors.map((c) => ({
      connectorCode: c.connectorCode, type: c.type,
      waitSec: c.waitSec, travelSecPerFloor: c.travelSecPerFloor,
      stops: c.stops,
    }))
```

把 L119-122 的 compareInfo 块改为双值：

```ts
    compareInfo.value = t('实际 {am} 米 / {as} 秒 ・ 优化 {om} 米 / {os} 秒 ・ 省 {p}%')
      .replace('{am}', (cmp.actualMm / 1000).toFixed(1)).replace('{as}', cmp.actualSec.toFixed(0))
      .replace('{om}', (cmp.optimizedMm / 1000).toFixed(1)).replace('{os}', cmp.optimizedSec.toFixed(0))
      .replace('{p}', cmp.timeSavingsPct.toFixed(0))
```

- [ ] **Step 2: FloorViewer 单层派生时间行**

`FloorViewer.vue`：在 `import { planPickComparison, ... } from '@/space-viewer/advanced/PickPathPlanner'`（L96）之后加：

```ts
import { mmToSec } from '@/space-viewer/advanced/cost'
```

把 L279-282 的 compareInfo 块改为双值（单层时间 = 距离÷步速派生）：

```ts
    compareInfo.value = t('实际 {am} 米 / {as} 秒 ・ 优化 {om} 米 / {os} 秒 ・ 省 {p}%')
      .replace('{am}', (cmp.actualMm / 1000).toFixed(1)).replace('{as}', mmToSec(cmp.actualMm).toFixed(0))
      .replace('{om}', (cmp.optimizedMm / 1000).toFixed(1)).replace('{os}', mmToSec(cmp.optimizedMm).toFixed(0))
      .replace('{p}', cmp.savingsPct.toFixed(0))
```

（`pathInfo` L276-278 不动；单层 `cmp` 是 SP3 `PickComparison`，仍有 `savingsPct`。）

- [ ] **Step 3: type-check 全绿**

Run: `cd /d/CP6-space-backend/cp6.web && npm run type-check 2>&1 | tail -8`
Expected: 0 错（StackedViewer/FloorViewer/ConnectorPanel 全部消解）。

- [ ] **Step 4: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/views/space/stacked/StackedViewer.vue cp6.web/src/views/space/viewer/FloorViewer.vue && git commit -m "feat(space-p5): 对比面板双值（堆叠 timeSavingsPct/单层派生时间行）"
```

---

## Task 10: 全量三门 + 后端全量（零回归）

**Files:** 无（验证 + 可能 nit 修）

- [ ] **Step 1: 前端三门**

Run: `cd /d/CP6-space-backend/cp6.web && npm run type-check 2>&1 | tail -5 && npx vitest run 2>&1 | tail -6 && npm run build 2>&1 | tail -8`
Expected: type-check 0 错；vitest 全绿（含 cost 3 + planMultiFloor 8 + astar +2 + 既有）；build 成功。

- [ ] **Step 2: 后端全量 + 迁移快照**

Run: `cd /d/CP6-space-backend && dotnet build CP6.WebApi 2>&1 | tail -3 && dotnet test CP6.Tests 2>&1 | tail -6 && dotnet ef migrations has-pending-model-changes --project CP6.Core --startup-project CP6.WebApi`
Expected: build 0 Error；`dotnet test` Passed（SP4 基线 1442 + 本 SP 新增连接体成本测，0 fail/5 skip 不变）；has-pending `No changes`。

- [ ] **Step 3: 若有 nit 修则 commit；否则跳过**

```bash
cd /d/CP6-space-backend && git status --short
# 若全量过程中触发任何编译/lint nit，最小修复后：
# git add -A && git commit -m "fix(space-p5): 全量三门 nit"
```

---

## Task 11: gstack 真栈 QA

**Files:**
- Create: `docs/superpowers/qa/space-p5-traversal-cost/`（README + seed.sql + 截图）

> **环境（复用 SP4）：** 隔离 vite **5180** → 后端 5177（读 `appsettings.Local.json`→`CP6DB_SpaceQA`，已含 SP4 多层种子 QAWH/F1/F2/电梯 E1/出库单 OB-P4-CROSS）→ admin/123456。堆叠路由 `/space/stacked/{QAWH=F31F48C2…}`。坑全记见 spec §10。

- [ ] **Step 1: 起后端（auto-migrate 落 SpaceP5ConnectorCost 两列）+ 隔离 vite**

后端（worktree appsettings.Local.json→CP6DB_SpaceQA；后台 shell `&`+disown 防 harness kill）：
`cd /d/CP6-space-backend && (dotnet run --project CP6.WebApi --urls http://localhost:5177 >/tmp/sp5-api.log 2>&1 &) ; disown` —— 等 ~6s 看 log `Now listening`，确认迁移自动应用（连接体两列入库 + 回填）。
前端：`cd /d/CP6-space-backend/cp6.web && (npx vite --port 5180 >/tmp/sp5-vite.log 2>&1 &) ; disown`

- [ ] **Step 2: 种子 — 给 E1 灌成本**

用 PowerShell + sqlcmd（ASCII，避中文乱码）对 CP6DB_SpaceQA 跑：

```sql
UPDATE Space_Connector SET WaitSec=20, TravelSecPerFloor=6 WHERE ConnectorCode='E1';
```

（如需"避电梯更省"对照，另调高成本：`UPDATE Space_Connector SET WaitSec=120,TravelSecPerFloor=60 WHERE ConnectorCode='E1';` 看 optimized 序/省% 变化，再调回。）固化到 `docs/superpowers/qa/space-p5-traversal-cost/seed.sql`。

- [ ] **Step 3: 后端契约验收（§10 #1）**

`curl` 登录拿 cookie（admin/123456，dev Csrf 关）后：
`GET http://localhost:5177/api/space/site/{QAWH}/pick-path?taskNo=OB-P4-CROSS`
Expected: 200；`data.connectors[0]` 含 `waitSec:20, travelSecPerFloor:6`；`floors` 带 level/z。

- [ ] **Step 4: 浏览器验收（gstack headless，§10 #2/#3/#4/#5）**

用 gstack/browse：
1. 登录 → `/space/stacked/{QAWH}` → 加载 OB-P4-CROSS → 面板显**双值**"实际 X 米 / Y 秒 ・ 优化 … ・ 省 Z%"，Z 时间口径（截图 `01-stacked-dual.png`）。
2. 改 E1 成本（编辑器 `/space/editor/{F1}` 连接体面板改 等待秒/每层秒→保存，或 SQL）→ 重新加载堆叠 → optimized 序/省% 随成本变（截图 `02-cost-effect.png`）。
3. 单层 `/space/viewer/{QAWH}?floorId={F1}` 加载单层拣货单 → 面板也显时间行，路径/动画零回归（截图 `03-floor-time.png`）。
4. 编辑器 ConnectorPanel 新建连接体时选类型→成本预填生效；列表项改成本→保存成功 toast。

- [ ] **Step 5: 固化 QA + README**

写 `docs/superpowers/qa/space-p5-traversal-cost/README.md`（环境/种子/5 验收点结论/截图清单/已知 headless 限制：合成 wheel 拉不到 near LOD，路径细节靠 API+单测+数学闭环证）。

- [ ] **Step 6: Commit**

```bash
cd /d/CP6-space-backend && git add docs/superpowers/qa/space-p5-traversal-cost/ && git commit -m "test(space-p5): 真库&gstack QA — 双值面板/成本影响优化序/单层时间/编辑器预填（固化截图+seed）"
```

---

## 终审 review（T6/T9 后各一次对抗式）

T6（时间图核心）实现后、T9（接线）实现后，各派一个 fresh 终审子代理对抗式 review，重点核：
- **admissibility**：`g.hScale = Kmin = min(边时间/边物理长)`，证 `h = 欧氏3D×Kmin ≤ 真实最短时间`；临时 FA/FB 水平边 rate=1/walkSpeed ≥ Kmin 不破坏下界。
- **baseline 兜底**：`planPickComparisonMF` 的 order 选择保证 `optimizedSec ≤ actualSec`、`timeSavingsPct ≥ 0`（代数核 `routeLengthByOrder` 时间一致性）。
- **零回归**：SP3 `pathBetween` 调 `astar` 不传 hScale（=1）一字节等价；单层 `PickComparison.savingsPct` 未改；`distanceMatrixMF`→`costMatrixMF` 无遗漏 consumer（仅 planMultiFloor + 其 spec）。
- **GPU/资源**：本 SP 无新 Three 资源（PathAnimator 不改），无泄漏面。
- **i18n**：新文案走 `t()` plain（本仓 `missingWarn:false`，无新键要求），与既有面板一致。

抓到 Important/Blocking 当任务修；Minor 记录。

---

## Self-Review（plan vs spec）

- **spec §3（数据/迁移）** → T1 ✓（字段 + 迁移 + 回填 SQL + has-pending）。
- **spec §3.3/§4.2（类型默认 + Update + 服务）** → T2 ✓（DefaultCost + Create 默认 + UpdateAsync + 4 测）。
- **spec §4.3/§4.4（控制器 PUT + 站点 VO）** → T3 ✓。
- **spec §5.1（cost.ts）** → T4 ✓。
- **spec §5.3（astar hScale）** → T5 ✓。
- **spec §5.2/§5.4（FloorMeta+level / 时间图 / Kmin / costMatrixMF / 双值 MFComparison）** → T6 ✓。
- **spec §7/§8.1（类型/api）** → T7 ✓。
- **spec §7（编辑器面板）** → T8 ✓。
- **spec §6/§8.2（单层时间 / 堆叠双值接线）** → T9 ✓。
- **spec §9（测试矩阵）** → T2/T4/T5/T6 单测 + T10 全量 ✓。
- **spec §10（QA）** → T11 ✓。
- **spec §11（零回归护栏）** → T5 默认 1 + T6 注记 + 终审 review ✓。

**Placeholder 扫描**：无 TBD/TODO；每代码步含完整代码。
**类型一致性**：`MFComparison{actualMm,optimizedMm,actualSec,optimizedSec,timeSavingsPct,order,degradedPairCount}`、`MFRoute{points,totalDistance,totalTime,degraded}`、`ConnectorPath{...,waitSec,travelSecPerFloor,...}`、`costMatrixMF`、`astar(...,hScale=1)`、`ConnectorUpdate{name,connectorType,waitSec,travelSecPerFloor}`、`ConnectorUpdateDto`（后端同名字段）—— 前后任务一致。
**已知跨任务暂态**：T6 后 type-check 因 `savingsPct`→`timeSavingsPct` 暂报 StackedViewer，T9 收口；T7 后 ConnectorPanel create 暂缺成本字段，T8 收口。均已在对应任务注明，vitest 不受影响。
