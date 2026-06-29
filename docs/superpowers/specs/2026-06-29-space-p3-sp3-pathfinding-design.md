# Space P3 · SP3 拣货路径规划做真 — 设计规格

- 版本：v1.1
- 日期：2026-06-29
- 分支：`feat/space-p3-pathfinding`（worktree `D:\CP6-space-backend`，基于已落 main 的 `f2e0298`=Space 00~08 + SP1 + SP2）
- 范围：前端拣货路径 planner 做真（`cp6.web/src/space-viewer/advanced/`），**零后端 / 零 EF 迁移 / 零契约改 / 零 i18n 键新增**
- 承接：[[project_space_p1_impl]] P3·08 落的 `PickPathPlanner.ts` v1；reconcile spec `docs/superpowers/specs/2026-06-28-space-p2-p3-stock-overlay-advanced-viz-reconcile-design.md` §4

**v1.1 修订（用户评审 8 点 + 补测试 + §4.5 兜底规则）**：① `actualMm/optimizedMm` 命名消歧（底层 mm，UI 层 `/1000` 显示米）；② 优化序以 actual 为 baseline 兜底（`optimized ≤ actual` 强保证、`savingsPct≥0`）；③ `routeOptimize.ts` 只做矩阵→顺序（不依赖 planner 运行时函数，避免循环依赖；`distanceMatrix` 留在 `PickPathPlanner` 内，因它需要 `Graph`/`pathBetween`）；④ 线段相交 `eps` 维度修正（`denom` 单位 mm²，按段长折算平行判据与 t/u 容差）；⑤ 共线端点贴合显式处理（`pointOnSegment` 兜 T 型/端点连接）；⑥ `AdvancedPanel`↔`FloorViewer` 显隐状态接线契约写清（props/emits + 切层/清空复位规则）；⑦ 沿用本仓 i18n 房规——`t()` plain Chinese 是房规（`i18n/index.ts` `missingWarn:false`，**实测无 missing-key 警告**，且全面板既有标签皆走 `t()`），**不引 `el-switch active-text`**，开关用与本面板既有 toggle 一致的 `.ap-check` 复选框；⑧ 抽 `planPickRouteOnGraph(g,stops)`/`distanceMatrixFromGraph(g,stops)` 内部函数避免重复建图，对外 `planPickRoute(aisles,stops)` 旧签名不变（包一层）。

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

/** 点 p 是否落在段 [a,b] 上（含端点；距离 ≤ eps）。 */
export function pointOnSegment(p: Pt, a: Pt, b: Pt, eps?: number): boolean

/** 线段 [p1,p2] 与 [p3,p4] 的交点；含端点贴合（T 型）+ 共线端点贴合。无交点→null。 */
export function segSegIntersection(p1: Pt, p2: Pt, p3: Pt, p4: Pt, eps?: number): Pt | null

/** 把一组分割点投影/排序到段 [a,b] 上（按到 a 的参数 t∈[0,1] 升序，去重），返回有序点列（含 a、b）。 */
export function splitPointsOnSegment(a: Pt, b: Pt, cuts: Pt[], eps?: number): Pt[]
```

**`eps` 默认 `1`（mm，与 `key` 的 1mm 取整一致）。**

**⚠️ 维度修正（v1.1 点 4）**：`denom = d1×d2`（叉积）单位是 **mm²**，而 `eps` 是 **mm**——`|denom| < eps` 维度不一致，长/短/近平行段判定不稳。改为按段长折算：

`pointOnSegment(p,a,b)` 算法：
- `len2 = (b−a)·(b−a)`；`len2 < eps²` → 退化点段，仅判 `dist(p,a) ≤ eps`。
- 否则投影参数 `t = clamp(((p−a)·(b−a))/len2, 0, 1)`；垂足 `foot = a + t·(b−a)`；返回 `dist(p, foot) ≤ eps`（钳制保证只认段内 + 端点，不认延长线）。

`segSegIntersection` 算法：
- `len1 = dist(p1,p2)`、`len2 = dist(p3,p4)`。**零长度段**（`len1 < eps || len2 < eps`）→ 退化，先用 `pointOnSegment` 判端点贴合（见下），否则 `null`。
- `d1=p2−p1`、`d2=p4−p3`；`denom = d1×d2`（叉积，mm²）。
- **平行/共线判据（按段长折算）**：`parallel = |denom| ≤ eps · max(len1, len2)`（等价于 `sinθ` 小，量纲一致）。`parallel` 为真时**不直接 null**，先做共线端点贴合（点 5）：
  - `if (pointOnSegment(p1, p3, p4)) return p1`
  - `if (pointOnSegment(p2, p3, p4)) return p2`
  - `if (pointOnSegment(p3, p1, p2)) return p3`
  - `if (pointOnSegment(p4, p1, p2)) return p4`
  - 否则 `return null`（共线重叠整段不合并——非目标）。
- 否则（非平行）：`t = (p3−p1)×d2 / denom`、`u = (p3−p1)×d1 / denom`；**容差按各自段长折算**：`tEps = eps / max(len1, eps)`、`uEps = eps / max(len2, eps)`（1mm 在长巷与短段上语义一致）；若 `t∈[−tEps, 1+tEps]` 且 `u∈[−uEps, 1+uEps]`（含端点）→ 交点 `p1 + t·d1`，否则 `null`。
- 端点贴合（T 型）：非共线时由上式 `t/u` 含端点天然覆盖；共线端点连接由 `parallel` 分支的 `pointOnSegment` 兜（点 5）。

### §2.2 建图升级：`buildCenterlineGraph`

`PickPathPlanner.buildCenterlineGraph` 改为两阶段：

1. **收集原始段**：所有 aisle 中心线相邻点对 → `raw: {a:Pt;b:Pt}[]`。
2. **求交并拆段**：对每段 `s`，扫描其余所有段求 `segSegIntersection`，收集落在 `s` 内的交点 `cuts`；`splitPointsOnSegment(s.a,s.b,cuts)` 得有序点列，相邻点 `addEdge`（共享 1mm 取整顶点 → 交叉口自动成为公共节点）。

结果：中段交叉/T 接的巷道连通。`segments` 仍填子边（供 `nearestAccess` 投影；交叉口拆细后投影更准）。

复杂度 O(S²)（S=原始段数，仓库巷道级，数十~数百，足够）。

### §2.3 测试（vitest）

`segmentIntersect.spec.ts`：
- 十字相交（中点）/ T 型（端点落段内）/ 平行不交（null）/ 共线不重叠不贴合（null）/ 端点外延不交（null）/ 交点在端点。
- **零长度段**（`a==b`）→ `null`，不崩溃（点 4）。
- **共线端点贴合** A-B 与 B-C（共享 B）→ 识别返回 `B`（点 5）。
- **近似端点贴合**（端点相差 0.4mm < eps）→ 按 eps 合并，命中该端点（点 5）。
- **很长线段近似平行**（如 10m 巷道，夹角极小）→ 不误判相交（点 4，段长折算后 `parallel` 正确判真）。
- `pointOnSegment`：端点/中点真、延长线假、垂距 >eps 假、退化点段（a==b）判 `dist≤eps`。

`PickPathPlanner.spec.ts` 增：两条十字中心线 `buildCenterlineGraph` → 含中心交叉口节点 + 4 子边、且两端点经 astar 连通（v1 会 degraded）；平行两巷不连通保持。

---

## §3 C A\*（drop-in，搭 A）

`PickPathPlanner.dijkstra`（as-built 为 **3 参** `(adj, start, end)`）替为 `astar`，**新增第 4 参** `nodePt:(k:string)=>Pt`（取节点坐标算启发式）：签名 `astar(adj, start, end, nodePt) → string[]|null`。`pathBetween` 内已构造 `nodePt`（L130，临时节点 `FA/FB`→各自 foot，其余→`g.nodes.get(k)`），直接传入：

- `f = g + h`，`h(k) = dist(nodePt(k), nodePt(end))`（欧氏，admissible：欧氏 ≤ 图最短路）。
- 开集取最小 f（节点数小，O(V²) 选最小可接受；不引入堆）。
- 终点出队即停；回溯 `prev`。
- 临时接入节点 `FA/FB` 的坐标=各自 foot（`pathBetween` 已知）。

接口对 `pathBetween` 透明（只换内部调用 + 传 `nodePt`）。

测试：`astar` 与 v1 `dijkstra` 在同图上**最短距离相等**（路径长度等价；保留一个 dijkstra 参照实现或用已知图断言）；含 FA/FB 接入节点的网格图最短路正确。

---

## §4 B 重排对比（what-if）

### §4.1 纯逻辑：`routeOptimize.ts`（只吃矩阵，不依赖 planner —— 点 3 方案 A）

**循环依赖规避（点 3）**：`routeOptimize.ts` **只负责「给距离矩阵 → 出顺序」**，**不 import** `PickPathPlanner` 的运行时函数（`pathBetween`/`polyDist`）；`distanceMatrix`（需 `Graph`/`pathBetween`/`polyDist`）**留在 `PickPathPlanner.ts` 内**（见 §4.2，命名 `distanceMatrixFromGraph`）。`routeOptimize.ts` 仅从 `PickPathPlanner` `import type`（纯类型，编译期擦除，不构成运行时循环）。

```ts
// routeOptimize.ts —— 无运行时依赖 PickPathPlanner
/** 按给定访问序计算开放路径总长（相邻项矩阵距离之和；order 为下标排列）。 */
export function routeLengthByOrder(matrix: number[][], order: number[]): number

/** 开放路径优化：起点固定 index 0，最近邻 seed + 2-opt 改进，返回访问序（stops 的下标排列，order[0]===0）。 */
export function optimizeOrder(matrix: number[][]): number[]
```

- `routeLengthByOrder`：`Σ matrix[order[i]][order[i+1]]`，`i=0..len-2`（开放路径，无回程；空/单点 → 0）。
- `optimizeOrder`：
  - **边界**：`matrix` 空（0 行）→ `[]`；单点 → `[0]`；两点 → `[0,1]`。
  - 最近邻 seed：从 `0` 出发，每步选未访问中矩阵距离最小者。
  - 2-opt：对当前序反复尝试反转区间 `[i,j]`（`i≥1` 保持 `order[0]` 固定；**开放路径**：反转后只需重算受影响边 `[i-1,i]` 与 `[j,j+1]`，`j` 为末项时 `[j,j+1]` 不存在——边界正确处理），若 `routeLengthByOrder` 降低则采纳，直到无改进（或达迭代上限；stops 级小，收敛快）。
  - 返回 `order`（下标排列，`order[0]===0`）。
  - **注**：`optimizeOrder` 返回的是「算法尝试序」，**不保证 ≤ actual（LineNo 原序）**——最终的「优化序」由 §4.2 `planPickComparison` 以 actual 为 baseline 兜底（点 2）。

### §4.2 编排：`planPickComparison`（含 baseline 兜底 + 单次建图）

`PickPathPlanner` 增 `distanceMatrixFromGraph`（点 3：留在 planner，因需 `Graph`/`pathBetween`/`polyDist`）+ `planPickRouteOnGraph`（点 8：内部按图规划，避免重复建图）+ `planPickComparison`：

```ts
import { optimizeOrder, routeLengthByOrder } from './routeOptimize'

/** 拣货点两两图最短距离矩阵（i,j → mm；degraded 段记直连欧氏，一致可比）。对称。 */
export function distanceMatrixFromGraph(g: Graph, stops: Pt[]): number[][]

/** 按已建图规划整条路径（内部函数，避免重复 buildCenterlineGraph）。 */
function planPickRouteOnGraph(g: Graph, stops: Pt[]): PlannedRoute

export interface PickComparison {
  actual: PlannedRoute       // LineNo 序
  optimized: PlannedRoute    // 优化序（已兜底 ≤ actual）
  order: number[]            // 优化访问序（stops 下标，order[0]===0；回退时 = [0,1,2,…]）
  actualMm: number           // actual.totalDistance（mm —— 底层数据空间即 mm）
  optimizedMm: number        // optimized.totalDistance（mm）
  savingsPct: number         // (actualMm-optimizedMm)/actualMm*100；actualMm=0→0；钳 ≥0
  degradedPairCount: number  // distanceMatrix 中退化（直连欧氏）的点对数（点 5：UI/QA 可判）
}
export function planPickComparison<T extends { centerline: string }>(aisles: T[], stops: Pt[]): PickComparison
```

- **单次建图（点 8）**：`const g = buildCenterlineGraph(aisles)`（含交叉口）；`actual`、`matrix`、`optimized` 全用同一 `g`：
  - `const actual = planPickRouteOnGraph(g, stops)`
  - `const matrix = distanceMatrixFromGraph(g, stops)`（顺带统计 `degradedPairCount`）
  - 对外旧签名 `planPickRoute(aisles, stops)` 保留为 `planPickRouteOnGraph(buildCenterlineGraph(aisles), stops)` 的包装（兼容 §4.4 既有调用点不破）。
- **baseline 兜底（点 2，关键）**——保证 `optimized ≤ actual`、`savingsPct≥0`：
  ```ts
  const actualOrder = stops.map((_, i) => i)        // LineNo 原序
  const candidateOrder = optimizeOrder(matrix)      // NN+2opt 尝试序
  const actualLen = routeLengthByOrder(matrix, actualOrder)
  const candidateLen = routeLengthByOrder(matrix, candidateOrder)
  const order = candidateLen < actualLen ? candidateOrder : actualOrder
  const optimized = planPickRouteOnGraph(g, order.map(i => stops[i]!))
  ```
  原序本就最优时，`order` 回退 actualOrder（`optimized` 折线长 == actual，`savingsPct=0`）。
- **`savingsPct`**：`actualMm===0 ? 0 : Math.max(0, (actualMm - optimizedMm) / actualMm * 100)`（兜底用 matrix 选序保证 `optimizedMm ≤ actualMm`；`Math.max(0,…)` 兜浮点）。
- **`degradedPairCount`**：`distanceMatrixFromGraph` 计算时累计 `pathBetween` 返回 `degraded:true` 的点对数（i<j 计一次），透出供 UI/QA 判断巷道是否仍有不连通（理想情况下 A 真交叉口图后应为 0）。

### §4.3 渲染：`PathAnimator.setComparisonPath`

`PathAnimator` 增静态对比线（无小车、不参与动画）：

```ts
setComparisonPath(points: Pt[] | null): void   // null 清除
```

- 画一条 `Line`（`COMPARE_COLOR=0x76ff03` 绿）于 `_group`，`GROUND_Z` 同高（或 +20mm 防 z-fight）；存引用以便 `setComparisonPath(null)` / `clear` 移除。
- 主路径（`setPath`，青+小车）= **实际 LineNo 序**；对比线 = 优化序。

### §4.4 接线：`AdvancedPanel.vue` + `FloorViewer.vue`（状态契约 —— 点 6 + 点 7）

**as-built 模式（已核实）**：`FloorViewer`↔`AdvancedPanel` 为 **props-down + emits-up**；面板既有 toggle（热图/设备）用 `.ap-check` 复选框 `<input type="checkbox" :checked @change="$emit(...)">`，**非 `el-switch`**；面板**全部**标签走 `t()` plain Chinese。`FloorViewer.onLoadPath`（L251~270）现调 `planPickRoute`→改 `planPickComparison`；`loadFloor`（L138~147）已统一复位 path/device/workload 态。

**点 7 —— i18n 房规（不引 el-switch active-text）**：本仓 `i18n/index.ts` 配 `missingWarn:false`+`fallbackWarn:false`，`t('中文')` 对未注册键**实测无 missing-key 警告**（房规：UI 文本一律 `t()` plain Chinese，缺失回退渲染原文）。`t()` 是运行期查找回退，**不注册新键** → 仍满足「零 i18n 键新增」。故新开关沿用 `.ap-check` 复选框 + `t('显示优化路径')`，与本面板既有 toggle/标签**一致**；**不**用 `<el-switch active-text="显示优化路径">`（既偏离 `t()` 房规，又与本面板 toggle 样式不一致）。

**点 6 —— 显隐状态接线契约**：

`AdvancedPanel`（新增，挂在拣货路径 section、`v-if="pathLoaded"`）：
- props 增：`compareInfo: string`（「实际 X.X 米 / 优化 Y.Y 米 / 省 Z%」整串，由 FloorViewer 组装）、`showOptimized: boolean`。
- emits 增：`(e:'toggle-optimized'): void`。
- 模板增：`<div class="ap-info" v-if="pathLoaded">{{ compareInfo }}</div>` + `<label class="ap-check" v-if="pathLoaded"><input type="checkbox" :checked="showOptimized" @change="$emit('toggle-optimized')" />{{ t('显示优化路径') }}</label>`。

`FloorViewer` state（新增）：
- `const comparison = ref<PickComparison | null>(null)`、`const showOptimized = ref(false)`、`const compareInfo = ref('')`。
- `onLoadPath`：先 `data.stops` **按 `seq` 升序排序**（点 §4.5，防 API 返回序影响 LineNo 语义）再 filter/map；`const cmp = planPickComparison(data.aisles, stopPts)`；`comparison.value = cmp`；`pathAnimator.setPath(cmp.actual.points)`（actual = 青线 + 小车）；**复位** `showOptimized.value=false` + `pathAnimator.setComparisonPath(null)`；`compareInfo.value = t('实际 {a} 米 / 优化 {o} 米 / 省 {p}%')`（`.replace` 填 `(actualMm/1000).toFixed(1)`、`(optimizedMm/1000).toFixed(1)`、`savingsPct.toFixed(0)`）；degraded 提示沿用 `cmp.actual.degraded`（或 `cmp.degradedPairCount>0`）。
- `onToggleOptimized()`：`showOptimized.value = !showOptimized.value; pathAnimator?.setComparisonPath(showOptimized.value ? (comparison.value?.optimized.points ?? null) : null)`。
- **复位规则（点 6，关键——防绿线残留）**：在 `loadFloor`（切层）头部、清空路径处统一加 `comparison.value=null; showOptimized.value=false; compareInfo.value=''`（`pathAnimator?.clear()` 已移除主线 + 对比线；显式复位 ref 保证面板开关回弹）。
- distances 一律以米显示（mm/1000，1 位小数；savingsPct 整数百分比）。

### §4.5 稳定性与兜底规则（v1.1 汇总，落码须遵守）

- **单位**：`actualMm / optimizedMm` 统一用 **mm**（底层数据空间即 mm）；UI 层自行 `/1000` 显示米——**不在 planner 层换算**，避免二次换算/漏换算。
- **优化序 baseline**：优化序必须以 actual（LineNo 原序）为 baseline——若 NN+2opt 后路径长度 `≥ actual`，则 `order` 回退 actualOrder，`optimized` 折线长 == actual，`savingsPct=0`。**强保证 `optimizedMm ≤ actualMm`**（用同一 `matrix` 比较选序）。
- **无循环依赖**：`routeOptimize.ts` 不 import `PickPathPlanner` 的运行时函数（只 `import type`）；它只处理「矩阵 → 顺序」。`distanceMatrixFromGraph` 留在 `PickPathPlanner`（依赖 `Graph`/`pathBetween`）。
- **degraded 透出**：`distanceMatrixFromGraph` 若发现退化点对（直连欧氏），在 `PickComparison.degradedPairCount` 暴露计数，便于 UI/QA 判断巷道连通性（A 真交叉口图后理想为 0）。
- **stops 排序**：`FloorViewer` 必须按 `seq` **升序**生成 actual stops，避免 API 返回顺序影响 LineNo 语义（actual = LineNo 原序的前提）。
- **单次建图**：`planPickComparison` 内只 `buildCenterlineGraph` 一次，`actual`/`matrix`/`optimized` 复用同图（点 8）。

---

## §5 测试与验收

### §5.1 vitest 纯逻辑

| 模块 | 用例要点 |
| --- | --- |
| `segmentIntersect` | 十字/T/平行/共线不交/端点外延/交点在端点；**零长度段→null**；**共线端点贴合 A-B·B-C→B**；**近似贴合 0.4mm→合并**；**长线近平行不误判**；`pointOnSegment` 端点/中点/延长线/退化点段 |
| `buildCenterlineGraph`（含交叉口） | 十字→中心节点+4子边+两端连通；平行不连 |
| `astar` | 与 dijkstra 最短距离等价；网格+FA/FB 接入正确 |
| `distanceMatrixFromGraph` | 对称；连通段=图距离；degraded 段=欧氏 + `degradedPairCount` 计数正确 |
| `routeLengthByOrder` | 空→0；单点→0；多点=相邻矩阵和；开放路径无回程 |
| `optimizeOrder` | `order[0]===0`；2-opt 后 ≤ NN seed；**stops=[]→[]**；**stops=[p]→[0]**；两点→[0,1]；**open path 末段边界正确**（反转含末项不越界） |
| `planPickComparison` | **`actualMm/optimizedMm` 单位=mm**；`savingsPct≥0`；**`optimizedMm ≤ actualMm` 强保证**；**原序已最优→order 回退 [0,1,2,…]、savingsPct=0**；**NN seed 比 actual 差→最终不劣于 actual**；单拣货点 / 同点退化；`degradedPairCount` 透出 |

前端三门：vue-tsc 0 / vitest 全绿（既有 + 新增）/ build。

### §5.2 gstack 运行态（真浏览器）

环境沿用：后端 5177（`appsettings.Local.json`→`CP6DB_SpaceQA`）/ 前端 vite / admin·123456 / viewer 路由 `/space/viewer/{siteId}?floorId=`。坑：冷后端首调 ~5-6s JIT；数据空间 mesh parent SceneRoot 用 mm；sqlcmd 种子用 PowerShell + ASCII。

**需新 demo 种子**（当前 demo 只有 1 条 `AISLE-DEMO`，不触发交叉口）：在 `CP6DB_SpaceQA` 的演示 floor（`5C92E6A8…`）插**多巷十字网格**（≥2 主巷 + ≥1 横巷,中心线相交）+ 一张出库单带**跨多巷的多条有序明细**（拣货点分布在不同巷道）。固化种子 `seed.sql`。

**⚠️ 种子须构造「原始 LineNo 顺序明显绕路」**（否则随机明细恰好顺序合理 → B 优化价值不明显，截图证据不足）。例：实际 LineNo 序 `左上 → 右下 → 左下 → 右上`（来回横跨），优化序 `左上 → 左下 → 右下 → 右上`（蛇形）。这样验收必能看到：实际路径更长 / 优化路径更短 / 省距 % > 0。

验收点：
1. **A**：十字网格布局，拣货路径**沿巷道走、不穿货架**（v1 会直连穿过）；`degraded` 不再触发（中心线相交处连通）。
2. **B**：面板显示「实际/优化/省 Z%」；开「显示优化路径」→ 绿色优化线叠加显示;关→消失;动画跑实际路径。
3. **C**：路径与 dijkstra 结果一致（视觉同形 + 单测等价）。
4. 无回归：07 库存叠加 / 08 热图 / 设备占位 / 既有单巷 pick-path 正常。

固化 QA 证据至 `docs/superpowers/qa/space-p3-sp3/`（README + 截图 + seed.sql）。

---

## §6 文件清单（新增/改动）

新增：
- `cp6.web/src/space-viewer/advanced/segmentIntersect.ts`（`pointOnSegment`+`segSegIntersection`+`splitPointsOnSegment`）+ `.spec.ts`
- `cp6.web/src/space-viewer/advanced/routeOptimize.ts`（**仅矩阵→顺序**：`optimizeOrder`+`routeLengthByOrder`，不依赖 planner 运行时）+ `.spec.ts`

改动：
- `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`（`buildCenterlineGraph` 插交叉口 / `dijkstra`→`astar(adj,start,end,nodePt)` / 新增 `distanceMatrixFromGraph` + 内部 `planPickRouteOnGraph` + `planPickComparison`+`PickComparison`；`planPickRoute` 改为包装不破旧签名）+ `PickPathPlanner.spec.ts`
- `cp6.web/src/space-viewer/advanced/PathAnimator.ts`（`setComparisonPath` + `clear` 同步移除对比线）
- `cp6.web/src/views/space/viewer/AdvancedPanel.vue`（`compareInfo` 统计串 + `showOptimized` 开关，`.ap-check` 复选框 + `t()` plain string）
- `cp6.web/src/views/space/viewer/FloorViewer.vue`（接线：stops 按 seq 排序 → `planPickComparison` → 喂 actual/optimized；`comparison`/`showOptimized`/`compareInfo` 状态 + 切层/清空复位）

无后端 / 无 EF 迁移 / 无契约改 / 无 i18n 键新增（面板文本 `t()` plain string，`missingWarn:false` 无警告）。

---

## §7 交付顺序（subagent-driven TDD）

1. **A-1 几何**：`segmentIntersect.ts`（`pointOnSegment`+`segSegIntersection`[eps 段长折算 + 共线端点贴合]+`splitPointsOnSegment`）+ spec。
2. **A-2 建图**：`buildCenterlineGraph` 插交叉口 + spec（十字连通）。
3. **C A\***：`dijkstra`→`astar(adj,start,end,nodePt)`（搭 A，与 dijkstra 等价性 spec；保留 dijkstra 参照或已知图断言）。
4. **B-1 优化**：`routeOptimize.ts`（`optimizeOrder`+`routeLengthByOrder`，**仅矩阵**）+ spec（边界 []/[p]/两点/末段）。
5. **B-2 编排**：`PickPathPlanner` 增 `distanceMatrixFromGraph`+`planPickRouteOnGraph`+`planPickComparison`（单次建图 + baseline 兜底 + `actualMm/optimizedMm`/`degradedPairCount`）+ spec。
6. **B-3 渲染**：`PathAnimator.setComparisonPath`（+ `clear` 移除对比线）。
7. **B-4 接线**：`AdvancedPanel` `compareInfo`+`showOptimized` 开关 + `FloorViewer` stops 排序/喂数据/复位；三门。
8. **QA**：多巷十字 + 「明显绕路」种子 + gstack 验收 4 点，固化证据。

每项实现→spec 审→质量审→修；纯逻辑 vitest 当场绿，画布运行态留第 8 步 gstack。

---

## §8 SP4 预告（不在本 spec）

D 3D 多层路由：新连接体实体（电梯/楼梯，跨 floor 共享节点）+ EF 迁移 + 后端跨层解析 + WMS 契约改（`PickStop` 带 floor，pick-path 支持跨层）+ viewer 跨层渲染/相机。独立 spec→plan→TDD。
