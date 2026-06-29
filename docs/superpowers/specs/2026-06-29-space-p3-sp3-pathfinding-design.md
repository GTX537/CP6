# Space P3 · SP3 拣货路径规划做真 — 设计规格

- 版本：v1.0
- 日期：2026-06-29
- 分支：`feat/space-p3-pathfinding`（worktree `D:\CP6-space-backend`，基于已落 main 的 `f2e0298`=Space 00~08 + SP1 + SP2）
- 范围：前端拣货路径 planner 做真（`cp6.web/src/space-viewer/advanced/`），**零后端 / 零 EF 迁移 / 零契约改 / 零 i18n 键新增**
- 承接：[[project_space_p1_impl]] P3·08 落的 `PickPathPlanner.ts` v1；reconcile spec `docs/superpowers/specs/2026-06-28-space-p2-p3-stock-overlay-advanced-viz-reconcile-design.md` §4

---

## §0 背景与目标

P3·08 落了拣货路径可视化 v1（`PickPathPlanner.ts` + `PathAnimator.ts` + `AdvancedPanel.vue`）。v1 三处局限（已在代码核实）：

| 局限 | 根因（`PickPathPlanner.ts`） | 后果 |
| --- | --- | --- |
| **连图只靠共端点** | `buildCenterlineGraph` 仅把每条 aisle 中心线的相邻点连边（顶点 1mm 取整去重）；不同 aisle 的中心线在中段交叉但不共享顶点时不连通 | 典型「主巷×横巷网格」中段交叉 → `pathBetween` 退化直连（`degraded`，穿过货架） |
| **按 LineNo 序访问** | `planPickRoute` 顺序拼接 `stops`（`PickStop.Seq = LineNo`），无重排/优化 | 看不到动线优化空间（拣货路径可视化的核心业务价值缺失） |
| **Dijkstra O(V²)** | `dijkstra` 简单选最小 | 交叉口图变大后无启发式加速 |

**目标**：把 planner 做真——A 真交叉口图（修正确性）+ B 重排对比（what-if 优化洞察）+ C A\*（搭 A 的 drop-in 加速）。

**关键决策（用户已拍板）**：
- **D1 范围 = A + B + C**；**D 3D 多层路由拆为独立 SP4**（无连接体数据底座，需新实体+迁移+后端+WMS 契约改+viewer 跨层，量级/风险与 A/B/C 不同）。
- **D2 B 口径 = what-if 对比**：可视化同时显示「实际 LineNo 序」路径 + 「优化序」路径 + 省距百分比；**不回写、不改 WMS 拣货顺序**（拣货序由 WMS 业务决定，有操作含义）。
- **D3 全程纯前端**：A/B/C 不碰后端/迁移/契约/i18n 键（与 SP2 同性质）。

**非目标**：3D 多层（SP4）；改后端 `IWmsPickTaskQuery`/`PickPathDto`/`PickStop` 契约；改 WMS 拣货序；近邻不相交的巷道强行连通（只认真几何交点 + 端点贴合）。

---

## §1 as-built 锚点（落码前直接引用，不重探查）

`cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`（纯逻辑，mm 数据空间，2D-XY）：

- `interface Pt { x:number; y:number }`；`PlannedRoute { points:Pt[]; totalDistance:number; degraded:boolean }`；`Graph { nodes:Map<string,Pt>; adj:Map<string,Array<{to:string;w:number}>>; segments:Array<{a:Pt;b:Pt}> }`。
- `key(p) = ` `${Math.round(p.x)},${Math.round(p.y)}` `（1mm 取整）；`dist = Math.hypot`。
- `parseCenterline(json): Pt[]`（`[[x,y],…]`，非法→`[]`）。
- `addEdge(g,a,b)`：去重无向边，push `segments`。
- `buildCenterlineGraph<T extends {centerline:string}>(aisles): Graph`：逐 aisle 连相邻点。**要升级为插交叉口**。
- `projectToSegment(p,a,b)` / `nearestAccess(g,p)`：库位投影到最近段取垂足 + 段两端。
- `dijkstra(adj,start,end): string[]|null`：O(V²)。**要替为 astar**。
- `pathBetween(g,a,b): {points:Pt[];degraded:boolean}`：a→接入 FA→巷道→接入 FB→b；不连通/无段→直连 degraded。
- `polyDist(pts)`；`planPickRoute<T>(aisles, stops:Pt[]): PlannedRoute`：相邻拣货点顺序拼接（去重接缝点）。**要补 `planPickComparison`**。

`cp6.web/src/space-viewer/advanced/PathAnimator.ts`：
- `setPath(points:Pt[])`：建路径线（`PATH_COLOR=0x00e5ff` 青）+ 小车（`CART_COLOR=0xff4081`）于 `getSceneRoot()`（数据空间 mm，线高 `GROUND_Z=200`）；`play/pause/stepNext/setSpeed/replay/clear` 自有 RAF。**要补 `setComparisonPath`**。
- 线由 `Line(BufferGeometry, LineBasicMaterial)` 组成；`_group` parent 到 `getSceneRoot()`。

`cp6.web/src/space-viewer/advanced/pathModel.ts`：`polylineLength` / `pointAtDistance`（弧长参数化）。

`AdvancedPanel.vue`：拣货路径面板（现显示「拣货路径:N点/X.X米」+ 播放控件）。`FloorViewer.vue`：advanced 接线（07/08 互斥、调 `api/space/advanced.ts` 取 pick-path）。

后端契约（**本 spec 不改**）：`api/space/advanced.ts` 调 `/api/space/floor/{id}/pick-path?taskNo=`，后端 `SpaceAdvancedController` 服务端 join `Space_Location` 补 AbsXYZ + join `Space_Aisle×Space_Zone` 打包本层 aisle 列表（含 `centerline`）。前端拿到 `{ stops:[{seq,locationCode,absX,absY,...}], aisles:[{centerline,...}] }`（VO 镜像，详见 `types/space/advanced.ts`）。

测试基建：vitest（`*.spec.ts` 同目录）；既有 `PickPathPlanner.spec.ts`/`pathModel.spec.ts`/`PathAnimator.spec.ts`。前端三门 vue-tsc / vitest / build。

---

## §2 A 真交叉口图

### §2.1 纯几何：`segmentIntersect.ts`

新增 `advanced/segmentIntersect.ts`（不引 Three/Konva）：

```ts
import type { Pt } from './PickPathPlanner'

/** 线段 [p1,p2] 与 [p3,p4] 的交点；含端点贴合（T 型）。无交点→null。 */
export function segSegIntersection(p1: Pt, p2: Pt, p3: Pt, p4: Pt, eps?: number): Pt | null

/** 把一组分割点投影/排序到段 [a,b] 上（按到 a 的参数 t∈[0,1] 升序，去重），返回有序点列（含 a、b）。 */
export function splitPointsOnSegment(a: Pt, b: Pt, cuts: Pt[], eps?: number): Pt[]
```

`segSegIntersection` 算法：
- 用参数式：`d1=p2−p1`、`d2=p4−p3`；`denom = d1×d2`（叉积）。
- `denom≈0`（平行/共线，`|denom|<eps`）→ 返 `null`（共线重叠由「端点贴合」分支兜，不在此处处理重叠合并）。
- 否则 `t = (p3−p1)×d2 / denom`、`u = (p3−p1)×d1 / denom`；若 `t∈[−εt, 1+εt]` 且 `u∈[−εt, 1+εt]`（含端点，εt 由 eps 折算）→ 交点 `p1 + t·d1`，否则 `null`。
- 端点贴合（T 型）天然被上式 `t/u` 含端点覆盖（一段端点落在另一段内部时 t 或 u 命中 [0,1] 内、另一个命中端点）。

`eps` 默认 `1`（mm，与 `key` 的 1mm 取整一致）。

### §2.2 建图升级：`buildCenterlineGraph`

`PickPathPlanner.buildCenterlineGraph` 改为两阶段：

1. **收集原始段**：所有 aisle 中心线相邻点对 → `raw: {a:Pt;b:Pt}[]`。
2. **求交并拆段**：对每段 `s`，扫描其余所有段求 `segSegIntersection`，收集落在 `s` 内的交点 `cuts`；`splitPointsOnSegment(s.a,s.b,cuts)` 得有序点列，相邻点 `addEdge`（共享 1mm 取整顶点 → 交叉口自动成为公共节点）。

结果：中段交叉/T 接的巷道连通。`segments` 仍填子边（供 `nearestAccess` 投影；交叉口拆细后投影更准）。

复杂度 O(S²)（S=原始段数，仓库巷道级，数十~数百，足够）。

### §2.3 测试（vitest）

`segmentIntersect.spec.ts`：十字相交（中点）/ T 型（端点落段内）/ 平行不交（null）/ 共线（null）/ 端点外延不交（null）/ 交点在端点。
`PickPathPlanner.spec.ts` 增：两条十字中心线 `buildCenterlineGraph` → 含中心交叉口节点 + 4 子边、且两端点经 astar 连通（v1 会 degraded）；平行两巷不连通保持。

---

## §3 C A\*（drop-in，搭 A）

`PickPathPlanner.dijkstra` 替为 `astar`，同签名 `(adj, start, end, nodePt) → string[]|null`（多传一个 `nodePt:(k:string)=>Pt` 取节点坐标算启发式；`pathBetween` 已有 `nodePt`）：

- `f = g + h`，`h(k) = dist(nodePt(k), nodePt(end))`（欧氏，admissible：欧氏 ≤ 图最短路）。
- 开集取最小 f（节点数小，O(V²) 选最小可接受；不引入堆）。
- 终点出队即停；回溯 `prev`。
- 临时接入节点 `FA/FB` 的坐标=各自 foot（`pathBetween` 已知）。

接口对 `pathBetween` 透明（只换内部调用 + 传 `nodePt`）。

测试：`astar` 与 v1 `dijkstra` 在同图上**最短距离相等**（路径长度等价；保留一个 dijkstra 参照实现或用已知图断言）；含 FA/FB 接入节点的网格图最短路正确。

---

## §4 B 重排对比（what-if）

### §4.1 纯逻辑：`routeOptimize.ts`

新增 `advanced/routeOptimize.ts`：

```ts
import type { Pt, Graph } from './PickPathPlanner'

/** 拣货点两两图最短距离矩阵（i,j → mm；不连通用直连欧氏兜底，并标记）。 */
export function distanceMatrix(g: Graph, stops: Pt[]): number[][]

/** 开放路径优化：起点固定 index 0，最近邻 seed + 2-opt 改进，返回访问序（stops 的下标排列，order[0]===0）。 */
export function optimizeOrder(matrix: number[][]): number[]
```

- `distanceMatrix`：对每对 `(i,j)` 用 `pathBetween(g, stops[i], stops[j]).` 的折线长度（`polyDist`）；degraded 段记直连欧氏（一致可比）。对称矩阵。
- `optimizeOrder`：
  - 最近邻：从 `0` 出发，每步选未访问中矩阵距离最小者。
  - 2-opt：对当前序反复尝试反转区间 `[i,j]`（保持 `order[0]` 固定）若降低总长则采纳，直到无改进（或达迭代上限，stops 级小，收敛快）。
  - 返回 `order`（下标排列，`order[0]===0`）。

### §4.2 编排：`planPickComparison`

`PickPathPlanner` 增：

```ts
export interface PickComparison {
  actual: PlannedRoute      // LineNo 序（= planPickRoute(aisles, stops)）
  optimized: PlannedRoute   // 优化序
  order: number[]           // 优化访问序（stops 下标，order[0]===0）
  actualM: number           // actual.totalDistance
  optimizedM: number        // optimized.totalDistance
  savingsPct: number        // max(0, (actualM-optimizedM)/actualM*100)；actualM=0→0
}
export function planPickComparison<T extends { centerline: string }>(aisles: T[], stops: Pt[]): PickComparison
```

- `g = buildCenterlineGraph(aisles)`（含交叉口）。
- `actual = planPickRoute(aisles, stops)`。
- `order = optimizeOrder(distanceMatrix(g, stops))`；`optimized = planPickRoute(aisles, order.map(i=>stops[i]))`。
- `savingsPct` 钳到 `≥0`（优化不应更长；浮点兜底）。

### §4.3 渲染：`PathAnimator.setComparisonPath`

`PathAnimator` 增静态对比线（无小车、不参与动画）：

```ts
setComparisonPath(points: Pt[] | null): void   // null 清除
```

- 画一条 `Line`（`COMPARE_COLOR=0x76ff03` 绿）于 `_group`，`GROUND_Z` 同高（或 +20mm 防 z-fight）；存引用以便 `setComparisonPath(null)` / `clear` 移除。
- 主路径（`setPath`，青+小车）= **实际 LineNo 序**；对比线 = 优化序。

### §4.4 接线：`AdvancedPanel.vue` + `FloorViewer.vue`

- `FloorViewer`：取到 pick-path 后调 `planPickComparison(aisles, stops)`；`animator.setPath(comparison.actual.points)`；按开关 `animator.setComparisonPath(showOptimized ? comparison.optimized.points : null)`。
- `AdvancedPanel`：显示「实际 {actualM} 米 / 优化 {optimizedM} 米 / 省 {savingsPct}%」+ `el-switch`「显示优化路径」（`t()` plain string，无新错误码）。distances 以米显示（mm/1000，1 位小数）。

---

## §5 测试与验收

### §5.1 vitest 纯逻辑

| 模块 | 用例要点 |
| --- | --- |
| `segmentIntersect` | 十字/T/平行/共线/端点外延/交点在端点 |
| `buildCenterlineGraph`（含交叉口） | 十字→中心节点+4子边+两端连通；平行不连 |
| `astar` | 与 dijkstra 最短距离等价；网格+FA/FB 接入正确 |
| `distanceMatrix` | 对称；连通段=图距离；degraded 段=欧氏 |
| `optimizeOrder` | order[0]===0；总长单调不增（≤NN seed）；2/3 点平凡 |
| `planPickComparison` | savingsPct≥0；optimized≤actual；单拣货点 / 同点退化 |

前端三门：vue-tsc 0 / vitest 全绿（既有 + 新增）/ build。

### §5.2 gstack 运行态（真浏览器）

环境沿用：后端 5177（`appsettings.Local.json`→`CP6DB_SpaceQA`）/ 前端 vite / admin·123456 / viewer 路由 `/space/viewer/{siteId}?floorId=`。坑：冷后端首调 ~5-6s JIT；数据空间 mesh parent SceneRoot 用 mm；sqlcmd 种子用 PowerShell + ASCII。

**需新 demo 种子**（当前 demo 只有 1 条 `AISLE-DEMO`，不触发交叉口）：在 `CP6DB_SpaceQA` 的演示 floor（`5C92E6A8…`）插**多巷十字网格**（≥2 主巷 + ≥1 横巷,中心线相交）+ 一张出库单带**跨多巷的多条有序明细**（拣货点分布在不同巷道）。固化种子 `seed.sql`。

验收点：
1. **A**：十字网格布局，拣货路径**沿巷道走、不穿货架**（v1 会直连穿过）；`degraded` 不再触发（中心线相交处连通）。
2. **B**：面板显示「实际/优化/省 Z%」；开「显示优化路径」→ 绿色优化线叠加显示;关→消失;动画跑实际路径。
3. **C**：路径与 dijkstra 结果一致（视觉同形 + 单测等价）。
4. 无回归：07 库存叠加 / 08 热图 / 设备占位 / 既有单巷 pick-path 正常。

固化 QA 证据至 `docs/superpowers/qa/space-p3-sp3/`（README + 截图 + seed.sql）。

---

## §6 文件清单（新增/改动）

新增：
- `cp6.web/src/space-viewer/advanced/segmentIntersect.ts` + `.spec.ts`
- `cp6.web/src/space-viewer/advanced/routeOptimize.ts` + `.spec.ts`

改动：
- `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`（`buildCenterlineGraph` 插交叉口 / `dijkstra`→`astar` / 新增 `planPickComparison`+`PickComparison`）+ `PickPathPlanner.spec.ts`
- `cp6.web/src/space-viewer/advanced/PathAnimator.ts`（`setComparisonPath`）
- `cp6.web/src/views/space/viewer/AdvancedPanel.vue`（对比统计 + 显示优化路径开关）
- `cp6.web/src/views/space/viewer/FloorViewer.vue`（接线：`planPickComparison` + 喂 actual/optimized）

无后端 / 无 EF 迁移 / 无契约改 / 无 i18n 键新增（面板文本 `t()` plain string）。

---

## §7 交付顺序（subagent-driven TDD）

1. **A-1 几何**：`segmentIntersect.ts`（`segSegIntersection`+`splitPointsOnSegment`）+ spec。
2. **A-2 建图**：`buildCenterlineGraph` 插交叉口 + spec（十字连通）。
3. **C A\***：`dijkstra`→`astar`（搭 A，等价性 spec）。
4. **B-1 优化**：`routeOptimize.ts`（`distanceMatrix`+`optimizeOrder`）+ spec。
5. **B-2 编排**：`planPickComparison` + spec。
6. **B-3 渲染**：`PathAnimator.setComparisonPath`。
7. **B-4 接线**：`AdvancedPanel` 统计+开关 + `FloorViewer` 喂数据；三门。
8. **QA**：多巷十字种子 + gstack 验收 4 点，固化证据。

每项实现→spec 审→质量审→修；纯逻辑 vitest 当场绿，画布运行态留第 8 步 gstack。

---

## §8 SP4 预告（不在本 spec）

D 3D 多层路由：新连接体实体（电梯/楼梯，跨 floor 共享节点）+ EF 迁移 + 后端跨层解析 + WMS 契约改（`PickStop` 带 floor，pick-path 支持跨层）+ viewer 跨层渲染/相机。独立 spec→plan→TDD。
