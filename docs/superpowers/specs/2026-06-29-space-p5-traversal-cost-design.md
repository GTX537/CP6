# Space SP5 — 连接体计时/通行成本（Traversal Cost）设计

- **日期**：2026-06-29
- **分支**：`feat/space-p5-traversal-cost`（基于已落 main 的 `e715106` = Space 00~08 + SP1~SP4）
- **worktree**：`D:\CP6-space-backend`
- **状态**：spec v1.0（待用户审）
- **承接**：SP4 3D 多层路由（连接体父子表 + 站点级 pick-path + 多层图/3D A\* + 全站堆叠 viewer + 编辑器放置工具，已上 main）。本 spec 只深化"竖直移动的代价"，不推翻 SP1~SP4 任一已落码。

---

## §0 背景与承接

SP4 已让多层拣货路径"几何上"成立：跨层路径经连接体（电梯/楼梯/坡道）的相邻 stop 竖直边，**边权 = 纯物理 `|Δz|`（mm）**（`planMultiFloor.ts:73-78`）。优化序（NN+2opt）也在"距离矩阵"上跑。

**问题**：现实里一次竖直移动 ≠ 它的物理高度。电梯要等待 + 慢速行程，楼梯每层费力，坡道长但叉车可走。SP4 把电梯当作"`|Δz|` 米"会**低估竖直成本** → 算出的"优化序"在实际作业里可能是错的（少绕几米水平却多坐一趟慢电梯）。

**SP5 = 把竖直边权从"物理距离"升为"可配计时成本"，优化目标从"距离"切到"时间"（拣货工时 = 人力成本），UI 同显 距离 + 时间双值。** 这也是未来 SP6「连接体容量/电梯排队调度」的天然地基（排队本质是时间维度）。

### 0.1 As-built 锚点（SP5 直接复用/扩展，逐一核过）

| 关注点 | as-built 位置 | SP5 动作 |
|---|---|---|
| 连接体实体 | `CP6.Entity/DomainModels/Space/Space_Connector.cs`（SiteId/ConnectorCode/ConnectorType/Name） | **+2 字段** WaitSec/TravelSecPerFloor |
| 连接体落点 | `Space_ConnectorStop.cs`（ConnectorId/FloorId/X/Y） | 不动 |
| 连接体服务 | `CP6.Core/Services/Space/ConnectorService.cs`（CreateAsync 去 502/UpsertStopAsync/Delete） | Create 灌类型默认 + **新增 UpdateAsync** |
| 连接体 DTO | `CP6.Entity/DTOs/Space/ConnectorDtos.cs`（ConnectorDto/ConnectorView/…） | +2 字段（含 ConnectorView 透出） |
| 控制器 | `CP6.WebApi/Controllers/Space/ConnectorController.cs` | **新增 PUT `/space/connector/{id}`** |
| 站点 pick-path | `SpaceAdvancedController.cs:120-127` 的 `connectors` VO（connectorCode/type/stops）、`floors` VO 已含 `level`/`height` | connectors VO **+waitSec/travelSecPerFloor** |
| 多层图基元 | `cp6.web/src/space-viewer/advanced/multiFloor.ts`（Pt3/FloorMeta `{floorId,z}`/dist3） | **FloorMeta +level** |
| 多层图+路由 | `cp6.web/src/space-viewer/advanced/planMultiFloor.ts`（buildMultiFloorGraph/pathBetweenMF/distanceMatrixMF/planPickComparisonMF/MFGraph/MFComparison） | **核心改写：时间边权 + Kmin + 时间矩阵 + 双值对比** |
| A\* | `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts:97` `astar(adj,start,end,nodePt)` 欧氏启发（SP3/SP4 共用） | **加可选 `hScale=1` 参数**（admissibility 标定，SP3 缺省 1 零回归） |
| 优化器 | `routeOptimize.ts`（optimizeOrder/routeLengthByOrder，纯矩阵零依赖） | **零改**（矩阵换成时间即可，楼层/单位无关） |
| 单层链 | `PickPathPlanner.ts` `planPickComparison` + `FloorViewer.vue:270-282` | **核心零改**；面板派生时间显示（§6） |
| 堆叠对比面板 | `StackedViewer.vue:96-127`（onLoadPath 建 mfFloors/connectors/调 planPickComparisonMF/compareInfo） | 接线扩字段 + 双值 compareInfo |
| 多层对比面板 UI | `views/space/viewer/AdvancedPanel.vue`（compareInfo 文本行） | compareInfo 文案双值（无结构改动） |
| 编辑器面板 | `views/space/editor/panels/ConnectorPanel.vue`（create 表单 code/type/name） | +WaitSec/TravelSecPerFloor 输入 + 类型预填 |
| 前端类型/api | `types/space/advanced.ts`（SiteConnectorVO）、`types/space/connector.ts`（ConnectorVO/Create）、`api/space/connector.ts` | +字段 + update 调用 |
| DI | `Program.cs:385` IConnectorService 已注册 | 不动 |

---

## §1 范围与边界

**In（SP5 做）**：
1. `Space_Connector` 加 WaitSec/TravelSecPerFloor（迁移 + 类型默认 + 回填）。
2. 连接体 CRUD 透出/可编辑成本（服务 UpdateAsync + PUT 端点 + 编辑器面板 + api）。
3. 站点 pick-path connectors VO 透出成本。
4. 前端多层图：竖直边权 = 计时成本、水平边权 = 距离÷步速；A\* 启发标定保持 admissible；时间矩阵优化；`planPickComparisonMF` 返回 距离 + 时间 双值。
5. 多层对比面板：双值文案"实际 X 米 / Y 秒 ・ 优化 X 米 / Y 秒 ・ 省 Z%（时间）"。
6. 单层面板：派生时间显示（同一全局步速换算；SP3 优化核心不动）。

**Out（YAGNI / 留后续）**：
- 连接体容量/电梯排队调度（SP6，时间动态/队列模型，需本 SP 的时间基地）。
- per-site 可配步速（v1 全局常量）。
- 上下行非对称成本（v1 对称）。
- 连接体类型扩展（仍 1 电梯/2 楼梯/3 坡道）。
- 堆叠剖切/LOD/性能调优（独立方向）。

---

## §2 决策记录（brainstorming 锁定）

| # | 决策 | 取舍 |
|---|---|---|
| D1 | **成本单位 = 时间（秒）；UI 双显 距离 + 时间** | 优化拣货工时（人力成本）；距离作参考。距离从几何 `polyDist3` 免费得，仅路由权与优化矩阵换时间。 |
| D2 | **成本参数 = 每连接体 `{WaitSec, TravelSecPerFloor}`** | 真实且边界清；电梯=等待大/每层小、楼梯=等待0/每层大。比单标量真实、比类型默认表少一层覆盖逻辑。 |
| D3 | **创建按类型给默认，存的是每连接体值** | 电梯`{20,6}`/楼梯`{0,15}`/坡道`{0,10}`（秒）。默认是兜底（迁移回填 + create 缺值），可逐连接体改。 |
| D4 | **水平步速 = 全局常量** | `WALK_SPEED_MMPS = 1200`（1.2 m/s 步行/叉车混合默认）。v1 不做 per-site。 |
| D5 | **headline 省% = 时间维度** | 与优化目标一致；距离两值并显作参考。 |
| D6 | **单层也显时间** | 单层无连接体，时间=距离÷步速线性缩放，优化序与距离等价 → SP3 核心零改，仅面板派生时间行。 |
| D7 | **A\* 启发保持 admissible** | `h = 欧氏3D × Kmin`，`Kmin`=全图 `min(边时间/边物理长)` → 证明性下界。 |
| D8 | **WaitSec 按竖直边计（= 每 stop 一次门周期）** | 已知简化：多 stop 同连接体连乘会多计 wait，但每停一次门周期本就是真实停顿；多数仓库每连接体 2 stop，影响微。文档记录。 |

---

## §3 数据模型与迁移

### 3.1 实体 `Space_Connector`（+2 字段）

```csharp
/// <summary>登乘/门周期固定成本（秒）。竖直边一次性计。</summary>
public int WaitSec { get; set; }
/// <summary>每跨一层的行程成本（秒），按两 stop 的 Level 差乘。</summary>
public int TravelSecPerFloor { get; set; }
```

### 3.2 迁移 `SpaceP5ConnectorCost`

```
dotnet ef migrations add SpaceP5ConnectorCost --project CP6.Core --startup-project CP6.WebApi
```

- 加两列（int，默认 0）。
- **回填既有行**（在 `Up()` 末尾 `migrationBuilder.Sql(...)`，按类型）：

```sql
UPDATE [Space_Connector] SET [WaitSec]=20,[TravelSecPerFloor]=6  WHERE [ConnectorType]=1;
UPDATE [Space_Connector] SET [WaitSec]=0, [TravelSecPerFloor]=15 WHERE [ConnectorType]=2;
UPDATE [Space_Connector] SET [WaitSec]=0, [TravelSecPerFloor]=10 WHERE [ConnectorType]=3;
```

- 仅加列 + 数据回填，不改既有表结构 / 不碰其他表 / 无新索引。

### 3.3 类型默认表（后端权威）

`ConnectorService` 内静态：

```csharp
private static (int wait, int perFloor) DefaultCost(int type) => type switch
{
    1 => (20, 6),   // 电梯：等待大、每层小
    2 => (0, 15),   // 楼梯：无等待、每层费力
    3 => (0, 10),   // 坡道：无等待、居中（叉车可走、长）
    _ => (0, 10),
};
```

`CreateAsync`：当 DTO 的 `WaitSec<=0 && TravelSecPerFloor<=0` 时灌 `DefaultCost(type)`；否则用显式值。（编辑器会预填并显式发送，故默认主要服务于回填 + API 省略场景。）

---

## §4 后端面

### 4.1 DTO（`ConnectorDtos.cs`）

- `ConnectorDto` +`WaitSec`/`TravelSecPerFloor`（create 入参，可省→走默认）。
- `ConnectorView` +`WaitSec`/`TravelSecPerFloor`（list 透出，供编辑器显示/编辑）。
- 新增 `ConnectorUpdateDto { string Name; int ConnectorType; int WaitSec; int TravelSecPerFloor }`（不改 code/site/stops）。

### 4.2 服务（`ConnectorService.cs`）

- `CreateAsync`：按 §3.3 灌默认；`ListBySiteAsync` 投影补两字段。
- **新增** `UpdateAsync(Guid id, ConnectorUpdateDto d, string? user)`：先查（无→`E-SPACE-502`），改 Name/ConnectorType/WaitSec/TravelSecPerFloor + Modifier/ModifyDate，`SaveChangesAsync`。不改 ConnectorCode（站内唯一键，避免撞 501）。

### 4.3 控制器（`ConnectorController.cs`）

- **新增** `PUT /space/connector/{id}` → `UpdateAsync`；`catch InvalidOperationException → BadRequest`，`Ok2`。

### 4.4 站点 pick-path（`SpaceAdvancedController.cs:120-124`）

connectors VO 补两字段：

```csharp
var connectors = conns.Select(c => new
{
    connectorCode = c.ConnectorCode, type = c.ConnectorType,
    waitSec = c.WaitSec, travelSecPerFloor = c.TravelSecPerFloor,   // ← 新增
    stops = connStops.Where(s => s.ConnectorId == c.Id)
                     .Select(s => new { floorId = s.FloorId, x = s.X, y = s.Y }).ToList()
}).ToList();
```

`floors` VO 已含 `level`（`SpaceAdvancedController.cs:126`）→ 前端按层 Level 算 floors-spanned，无需再改后端。

---

## §5 前端计时图与路由核心（主战场）

### 5.1 新增 `advanced/cost.ts`（全局成本常量 + 换算）

```ts
export const WALK_SPEED_MMPS = 1200          // 水平步行/叉车混合默认 1.2 m/s
export const mmToSec = (mm: number): number => mm / WALK_SPEED_MMPS
// 竖直边时间：等待（每停一次门周期）+ 每层行程 × 跨层数
export const verticalSec = (waitSec: number, perFloorSec: number, floorsSpanned: number): number =>
  waitSec + perFloorSec * Math.abs(floorsSpanned)
```

> 编辑器预填用的类型默认（`{1:[20,6],2:[0,15],3:[0,10]}`）镜像放本文件常量，供 UX；后端 §3.3 为持久化权威。重复刻意（UX 预填 vs 持久化权威），文档记录。

### 5.2 `multiFloor.ts`：`FloorMeta` 加 `level`

```ts
export interface FloorMeta { floorId: string; z: number; level: number }  // ← +level
```

### 5.3 `astar` 加 admissibility 标定参数（`PickPathPlanner.ts`）

```ts
export function astar(
  adj: Map<string, Array<{ to: string; w: number }>>,
  start: string, end: string,
  nodePt: (k: string) => { x: number; y: number; z?: number },
  hScale = 1,                                   // ← 新增；启发缩放（时间图传 Kmin，距离图缺省 1）
): string[] | null {
  ...
  const h = (a, b) => Math.hypot(a.x-b.x, a.y-b.y, (a.z??0)-(b.z??0)) * hScale   // ← ×hScale
  ...
}
```

- **正确性证明**：边 `e` 物理长 `d_e`、时间 `w_e`，定义 `Kmin = min_e (w_e/d_e)`。任意路径时间 `Σw_e = Σ(w_e/d_e)·d_e ≥ Kmin·Σd_e ≥ Kmin·直线(端点)`。故 `h = 直线3D × Kmin ≤ 真实最短时间`，admissible。
- **SP3 零回归**：单层 `pathBetween` 调 `astar(adj,FA,FB,nodePt)`（不传 hScale → 1）；其距离图 `w_e=d_e`→`Kmin=1`→ `h` 与现状一字节等价。

### 5.4 `planMultiFloor.ts`：时间边权 + Kmin + 时间矩阵 + 双值对比

**MFGraph 扩 `hScale`**：

```ts
export interface MFGraph {
  nodes: Map<string, Pt3>
  adj: Map<string, Array<{ to: string; w: number }>>   // w = 时间（秒）
  segments: Array<{ a: Pt; b: Pt; floorId: string }>
  floorZ: Map<string, number>
  floorLevel: Map<string, number>                      // ← floorId→Level（算 floors-spanned）
  hScale: number                                       // ← Kmin（建图时全图 min(w/物理长)）
}
export interface ConnectorPath {
  connectorCode: string; type: number
  waitSec: number; travelSecPerFloor: number           // ← 新增
  stops: Array<{ floorId: string; x: number; y: number }>
}
```

**`buildMultiFloorGraph(floors, aislesByFloor, connectors)`**（边权全部换时间，建图时累计 Kmin）：

- 水平子图（各层 `buildCenterlineGraph` 返回的 mm 边权）合并时 **`w_time = mmToSec(w_mm)`**；该边物理长 = `w_mm`，`w_time/w_mm = 1/WALK_SPEED_MMPS`（恒定，参与 Kmin）。
- 连接体 stop 接入本层最近巷道两端：水平接入边 `w_time = mmToSec(欧氏)`。
- 同连接体相邻（按 z 排序）两 stop 竖直边：
  - `floorsSpanned = |level_a − level_b|`（用 `g.floorLevel`）
  - `w_time = verticalSec(c.waitSec, c.travelSecPerFloor, floorsSpanned)`
  - 物理长 = `|z_a − z_b|`；`w_time/物理长` 参与 Kmin。
- `hScale = Kmin`（全图最小 时间/物理长；空图兜底 `1/WALK_SPEED_MMPS`）。

**`pathBetweenMF(g, a, b)`** 返回 `{ points: Pt3[]; time: number; degraded: boolean }`：
- 临时 FA/FB 接入边为水平 → `mmToSec(欧氏)`。
- `astar(adj, FA, FB, nodePt, g.hScale)`。
- `time = pathCostMF(adj, pathKeys)`（沿返回 key 序累计 adj 边权 = 总秒）；几何 `points` 仍供 `polyDist3` 算距离。
- degraded（任一端无接入/不连通）：`points=[pa,pb]`，`time = mmToSec(dist3(pa,pb))`（直线按步行兜底，与距离-degraded 直连欧氏一致）。

**`costMatrixMF(g, stops, degradedPairs?)`**（替 `distanceMatrixMF`，矩阵存**时间**供优化器）：两两 `pathBetweenMF(...).time`，对称，degraded 计数照旧。

**`planRouteOnMFGraph(g, stops)`** 返回 `{ points; totalDistance; totalTime; degraded }`：拼接相邻 `pathBetweenMF`（去重接缝点），`totalDistance = polyDist3(points)`（几何 mm，SP4 算法不变），`totalTime = Σ 段 time`。

**`MFComparison` 新形**：

```ts
export interface MFRoute { points: Pt3[]; totalDistance: number; totalTime: number; degraded: boolean }
export interface MFComparison {
  actual: MFRoute; optimized: MFRoute; order: number[]
  actualMm: number; optimizedMm: number              // 距离（几何，参考）
  actualSec: number; optimizedSec: number            // 时间（优化目标）
  timeSavingsPct: number                             // (actualSec-optimizedSec)/actualSec*100，钳≥0
  degradedPairCount: number
}
```

**`planPickComparisonMF(floors, aislesByFloor, connectors, stops)`**：
- `g = buildMultiFloorGraph(...)`；`actual = planRouteOnMFGraph(g, stops)`（LineNo 序）。
- `matrix = costMatrixMF(g, stops)`（时间）；`candidate = optimizeOrder(matrix)`；
- baseline 兜底（**时间维度**）：`order = routeLengthByOrder(matrix,candidate)+1e-9 < routeLengthByOrder(matrix,actualOrder) ? candidate : actualOrder` → 强保证 `optimizedSec ≤ actualSec`、`timeSavingsPct ≥ 0`。
- `optimized = planRouteOnMFGraph(g, orderedStops)`。
- 距离/时间各取两路由的 totalDistance/totalTime；`timeSavingsPct` 按时间算。

> `routeOptimize.ts` 零改：它只认"矩阵→开放路径序"，矩阵是距离还是时间无关。

---

## §6 单层时间显示（SP3 核心零改）

`FloorViewer.vue:270-282`：`planPickComparison`（SP3，距离图）**不动**。面板文案派生时间（距离÷步速线性）：

```ts
import { mmToSec } from '@/space-viewer/advanced/cost'
// cmp: PickComparison（actualMm/optimizedMm/savingsPct 不变）
compareInfo.value = t('实际 {am} 米 / {as} 秒 ・ 优化 {om} 米 / {os} 秒 ・ 省 {p}%')
  .replace('{am}', (cmp.actualMm/1000).toFixed(1)).replace('{as}', mmToSec(cmp.actualMm).toFixed(0))
  .replace('{om}', (cmp.optimizedMm/1000).toFixed(1)).replace('{os}', mmToSec(cmp.optimizedMm).toFixed(0))
  .replace('{p}', cmp.savingsPct.toFixed(0))   // 单层 距离%==时间%（线性），headline 仍是 time 口径
```

单层 `savingsPct`（距离）数值上 == 时间% → headline 口径一致，无歧义。

---

## §7 编辑器（ConnectorPanel.vue）

- create 表单加两 `el-input-number`：`WaitSec`、`TravelSecPerFloor`；选类型时 `watch(form.connectorType)` 预填 `cost.ts` 类型默认（用户可改）。
- 列表项每连接体显示成本 + "编辑"入口（`ElMessageBox` 或内联）→ `connectorApi.update(id, { name, connectorType, waitSec, travelSecPerFloor })`。
- `types/space/connector.ts`：`ConnectorVO`/`ConnectorCreate` +`waitSec`/`travelSecPerFloor`；新增 `ConnectorUpdate`。
- `api/space/connector.ts`：`create` 带成本；**新增** `update(id, d)` → `PUT /space/connector/{id}`。

---

## §8 对比面板 UI 接线

### 8.1 `types/space/advanced.ts`

`SiteConnectorVO` +`waitSec`/`travelSecPerFloor`。

### 8.2 `StackedViewer.vue:101,107,113-122`

```ts
const mfFloors = d.floors.map((f) => ({ floorId: f.floorId, z: f.z, level: f.level }))   // +level
const connectors = d.connectors.map((c) => ({
  connectorCode: c.connectorCode, type: c.type,
  waitSec: c.waitSec, travelSecPerFloor: c.travelSecPerFloor,                              // +成本
  stops: c.stops,
}))
const cmp = planPickComparisonMF(mfFloors, aislesByFloor, connectors, stops)
compareInfo.value = t('实际 {am} 米 / {as} 秒 ・ 优化 {om} 米 / {os} 秒 ・ 省 {p}%')
  .replace('{am}', (cmp.actualMm/1000).toFixed(1)).replace('{as}', cmp.actualSec.toFixed(0))
  .replace('{om}', (cmp.optimizedMm/1000).toFixed(1)).replace('{os}', cmp.optimizedSec.toFixed(0))
  .replace('{p}', cmp.timeSavingsPct.toFixed(0))   // ← headline = 时间省%
```

`AdvancedPanel.vue` compareInfo 是纯文本行（`v-if="pathLoaded && compareInfo"`），**无结构改动**，只是文案变长。

---

## §9 测试矩阵

### 9.1 前端 vitest（新增/改）

- `cost.spec.ts`：`mmToSec`/`verticalSec`（wait+perFloor×span）。
- `planMultiFloor.spec.ts`（改）：
  - 竖直边权 = `verticalSec`（非 `|Δz|`）；水平边权 = `mmToSec`。
  - **Kmin admissibility 不变性**：构造混合图，断言 `hScale === min(w/物理长)`、且 `h(任意,goal) ≤ 真实最短时间`（用一条已知最短路验证 astar 找到它）。
  - 时间矩阵优化：构造"水平多绕一点但避开慢电梯"的布局，断言 optimized 选避电梯序、`timeSavingsPct>0`。
  - baseline 兜底：劣布局下 `optimizedSec ≤ actualSec`、`timeSavingsPct≥0`。
  - degraded 时间 = 直线÷步速。
  - 双值字段齐（actualMm/optimizedMm/actualSec/optimizedSec/timeSavingsPct/order/degradedPairCount）。
- `PickPathPlanner.spec.ts`（加）：`astar` 不传 hScale 与传 1 等价；距离图 hScale=1 与历史路径一致（零回归断言）。
- `routeOptimize.spec.ts`：零改（既有绿）。

### 9.2 后端测试

- `ConnectorServiceTests`（加）：CreateAsync 按类型灌默认；显式值不被覆盖；UpdateAsync 改成本 + 502 路径；ListBySite 透出成本。
- `SitePickPathTests`（加）：connectors VO 含 waitSec/travelSecPerFloor。
- 迁移：`ef migrations has-pending-model-changes` = 无（加列后快照同步）。

### 9.3 零回归全量

- 前端 `vue-tsc 0 / vitest 全绿 / build`。
- 后端 `dotnet build 0 / dotnet test`（SP4 基线 1442；SP5 +连接体成本测，单层/SP3 测一字节不动）。

---

## §10 QA 计划（gstack 真栈）

复用 SP4 多层种子（CP6DB_SpaceQA：QAWH 站、F1/F2、电梯 E1[F1/F2 stop]、出库单 OB-P4-CROSS）。隔离 vite **5180** → 后端 5177（`appsettings.Local.json`→CP6DB_SpaceQA）→ admin/123456。堆叠路由 `/space/stacked/{QAWH}`。

**种子增量**：给 E1 灌成本（`UPDATE Space_Connector SET WaitSec=20,TravelSecPerFloor=6 WHERE ConnectorCode='E1'`）；如需"避电梯更省"对照，再加一条出库单/或调成本看序变化。

**验收 5 点**：
1. 站点 pick-path 200，connectors VO 带 waitSec/travelSecPerFloor。
2. 堆叠面板显**双值**"实际 X 米 / Y 秒 ・ 优化 … ・ 省 Z%"，Z 为时间口径。
3. 改 E1 成本（编辑器或 SQL）→ 重新加载，optimized 序/省% 随之变（电梯越贵越倾向少坐）。
4. 单层 `/space/viewer` 面板也显时间行，零回归（路径/动画/省%）。
5. 编辑器 ConnectorPanel 可建/改连接体成本，类型预填生效。

**坑（沿用 SP3/SP4）**：迁移须后端 auto-migrate 后再跑 seed；冷后端首调 ~5-6s；`dotnet run` 后台用 shell `&`+disown；sqlcmd 种子 PowerShell+ASCII、`[LineNo]`、`QUOTED_IDENTIFIER ON`、Polygon/Centerline NOT NULL、Placed 须 RackId；el-input 登录 click+type 非 fill；截图读 `C:\Users\tt\AppData\Local\Temp\*.png` 绝对路径；headless 合成 wheel 拉不到 near LOD（路径细节靠 API + 单测 + 数学闭环证，不靠像素）。

---

## §11 零回归护栏（汇总）

| 担心点 | 护栏 |
|---|---|
| SP3 单层路由变 | `planPickComparison`/`pathBetween`/`buildCenterlineGraph` 不碰；astar 加默认 1 参数（不传=历史等价）。 |
| 既有 SP4 距离断言碎 | 保留 `actualMm/optimizedMm`（几何算法不变）；优化序断言因改为时间优化而**主动更新**（预期内）。`savingsPct`→`timeSavingsPct` 重命名，改 `StackedViewer.vue` 与 spec 引用处。 |
| 迁移破坏现有库 | 仅加两列 + 回填，无既有列/索引/表改动。 |
| optimizeOrder 改坏 | 零改（矩阵单位无关）。 |
| admissibility 错 → 路径非最优 | §5.3 证明 + vitest 不变性断言；Kmin 退化（h 偏小）只是慢一点仍正确。 |

---

## §12 风险与简化记录

- **D8 WaitSec 按竖直边计**：多 stop 同连接体连乘多计 wait；现实每停一次门周期可接受；多数仓库 2 stop/连接体影响微。
- **步速全局常量**：v1 不可配；若客户要 per-site，后续加 Site 字段（独立小增量）。
- **degraded 时间按步行兜底**：跨层 degraded 直线含竖直却按步速折算，低估；degraded 本就是近似，文档标注。
- **类型默认双源**（后端权威 + 前端预填镜像）：刻意，UX vs 持久化分离。

---

## §13 交付顺序（writing-plans 输入）

后端先（数据底座）→ 前端核心（计时图）→ UI 接线 → QA：

1. **T1** 实体 +2 字段 + 迁移 + 回填（`ef migrations add`，has-pending 验证）。
2. **T2** 服务：CreateAsync 类型默认 + UpdateAsync；DTO/ConnectorView +字段；`ConnectorServiceTests`。
3. **T3** 控制器 PUT update + 站点 pick-path connectors VO +字段；`SitePickPathTests`。
4. **T4** 前端 `cost.ts`（常量/换算）+ vitest。
5. **T5** `astar` 加 hScale=1（SP3 等价断言）。
6. **T6** `multiFloor.ts` FloorMeta+level；`planMultiFloor.ts` 时间边权 + Kmin + costMatrixMF + planRouteOnMFGraph(双值) + planPickComparisonMF(双值 MFComparison)；`planMultiFloor.spec.ts` 改。
7. **T7** 类型/api：`types/space/{connector,advanced}.ts` +字段、`api/space/connector.ts` +update/带成本。
8. **T8** `ConnectorPanel.vue` 成本输入 + 类型预填 + 编辑。
9. **T9** `StackedViewer.vue`（mfFloors+level/connectors+成本/双值 compareInfo）+ `FloorViewer.vue`（单层派生时间行）。
10. **T10** 全量三门（前端 vue-tsc/vitest/build；后端 build/test/ef has-pending）。
11. **T11** gstack 真栈 QA（§10）+ 固化 `docs/superpowers/qa/space-p5-traversal-cost/`。

每 task：fresh subagent 实现 → 核 diff（spec 符合 + 质量）；T6/T9 后终审对抗 review（admissibility/baseline/零回归）。

---

## §14 SP6 预告（解锁）

SP5 落地后，连接体边权已是时间维度 → **SP6 连接体容量/电梯排队调度**可在此之上做：stop 加吞吐/容量、按并发拣货流估排队等待加到 WaitSec、或时间窗冲突惩罚。本 SP 不做，仅确认地基对齐。
