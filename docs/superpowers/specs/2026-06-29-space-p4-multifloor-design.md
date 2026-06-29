# Space P4 · 3D 多层路由 — 设计规格

- 版本：v1.0
- 日期：2026-06-29
- 分支：`feat/space-p4-multifloor`（worktree `D:\CP6-space-backend`，基于已落 main 的 `ecfaec6`=Space 00~08 + SP1 + SP2 + SP3 + 并发 WFS）
- 范围：跨楼层拣货路由 —— 新连接体实体 + EF 迁移 + 后端站点级 pick-path 契约 + 前端多层图/A* + 编辑器连接体放置工具 + 全站堆叠 3D viewer
- 承接：SP3 spec `docs/superpowers/specs/2026-06-29-space-p3-sp3-pathfinding-design.md`（§8 SP4 预告）；复用 SP3 的 `PickPathPlanner`/`routeOptimize`/`PathAnimator` 与 P1 viewer 的 `SceneBuilder`/`SceneRoot`

---

## §0 背景、目标、锁定决策

SP3 把单层拣货路径做真（真交叉口图 + 重排对比 + A*）。SP4 把路由扩到**跨楼层**：一张出库单的拣货点分布在多个楼层，路径需经电梯/楼梯上下层，并在 3D 中堆叠展现。

**锁定决策（brainstorming 用户拍板）**：
- **D1 连接体模型 = 父子表**：`Space_Connector`（竖井：码/类型/名）+ `Space_ConnectorStop`（每层一个落点 floorId/x/y）。一个连接体经 N 条 stop 服务任意多层；电梯（多层）/楼梯（相邻两层）统一建模。
- **D2 契约 = 新增站点级端点**：`GET /api/space/site/{siteId}/pick-path?taskNo=`；**既有 `/floor/{floorId}/pick-path` 原封不动**（08/SP3 单层零回归）。每个 stop 带 `floorId`（**Space 响应 VO 层解析，WMS `PickStop` 契约不动**——楼层是 Space 概念）。
- **D3 竖直成本 = 物理竖直距离**：连接体边权 = 两 stop 的 |Z 差|（Z=楼层 Level 累加层高）。全图单一 mm 度量，A* 3D 欧氏启发 admissible。Space_Connector **无 cost 字段**。
- **D4+D6 viewer = 全站楼层常驻堆叠 + 全几何**：站点所有楼层各按 Z 标高（Level×层高累加）渲染（复用 InstancedMesh 全几何），路径为真 3D 折线经连接体上下穿。
- **D5 连接体录入 = Konva 编辑器放置工具**：编辑器加「放置连接体」工具，逐层落 stop 点指派给连接体。

**架构铁律（承 SP3）**：**图在前端**——后端只供数据（stops+floors+aisles+connectors），前端建多层图跑 A*。SP3 的 `routeOptimize`（矩阵→顺序）楼层无关，跨层**逻辑零改**直接复用。

**非目标（YAGNI，留 SP5+）**：连接体容量/调度/实际电梯计时；连接体类型超出 电梯/楼梯/坡道；楼层显隐高级控件（仅基础 toggle）；连接体跨站点；斜行连接（stop 只在楼层局部平面，竖直边垂直）。

---

## §1 as-built 锚点（落码前直接引用，不重探查）

**`Space_Floor`**（`CP6.Entity/DomainModels/Space/Space_Floor.cs`）：`SiteId` / `Level`(int，如 1/2/-1，楼层序) / `FloorCode`(站内唯一) / `FloorName` / `Height`(层高 mm，默认 6000) / `OriginX/Y`(局部坐标原点)。每 Floor 是独立局部坐标系（mm，Z-up）。

**SP3 planner**（`cp6.web/src/space-viewer/advanced/`）：
- `PickPathPlanner.ts`：`Pt{x,y}`、`Graph{nodes,adj,segments}`、`buildCenterlineGraph<T>(aisles)`（两阶段插交叉口）、`astar(adj,start,end,nodePt)`（欧氏启发，nodePt 取节点坐标）、`pathBetween`、`distanceMatrixFromGraph(g,stops,degradedPairs?)`、`planPickComparison<T>(aisles,stops)→PickComparison{actual,optimized,order,actualMm,optimizedMm,savingsPct,degradedPairCount}`、`planPickRoute`。节点键 `key(p)=`​`${Math.round(p.x)},${Math.round(p.y)}`。
- `routeOptimize.ts`：`routeLengthByOrder(matrix,order)`、`optimizeOrder(matrix)`（NN+2opt，零依赖，**纯矩阵楼层无关**）。
- `PathAnimator.ts`：`setPath(points)`（青线+小车，`GROUND_Z=200` 常量高）、`setComparisonPath(points|null)`（绿对比线，`_clearCompareLine` dispose）、`play/pause/stepNext/setSpeed/replay/clear`、`_positionCart`。线/小车 parent 到 `viewer.getSceneRoot()`（数据空间 mm）。
- `pathModel.ts`：`polylineLength`、`pointAtDistance`。
- 契约 `types/space/advanced.ts`：`PickStopVO{seq,locationCode,qty,materialNo,absX,absY,absZ}`、`AisleCenterlineVO{aisleCode,centerline}`、`FloorPickPath{taskNo,stops,aisles}`。`api/space/advanced.ts` 调 `/api/space/floor/{id}/pick-path`。

**P1 viewer**（`cp6.web/src/space-viewer/`）：`SceneRoot.ts`（scale 0.001 + `rotation.x=-π/2`，dataToWorld/worldToData 唯一收口）、`SceneBuilder`（从 scene.racks/locations enrich 建 InstancedMesh 桶）、`Renderer`+`Loop`（按需渲染 markDirty，无逐帧回调）、`CameraController`(OrbitControls/flyTo)、`ViewerHandle`(getSceneRoot/dataToWorld/requestRender/getCurrentFloorId/getLocationCode/getLocationIdByCode)。`FloorViewer.vue`(单层场景，`/scene` 加载) + `FloorList`(切层) + `Locator`(按编码定位)。

**P1 editor**（`cp6.web/src/space-editor/`）：Konva 2D，`SceneStage.ts`(6 图层)、`InteractionManager`(工具状态机)、Command 双栈、`generate/`(模板/阵列)、绑码。路由 `/space/editor/{floorId}`。

**后端 pick-path**（**本 spec 新增站点级，不改既有**）：`SpaceAdvancedController`（`/api/space`）`/floor/{id}/pick-path` join `Space_Location`(Placed,FloorId) 补 AbsXYZ + join `Space_Aisle×Space_Zone` 取本层中心线。`IWmsPickTaskQuery.GetPickPathAsync(taskNo)`(CP6.Core，读 `OutboundOrderDetail` by `OutboundNo` 序 `[LineNo]`，返 `PickStop{Seq,LocationCode,Qty,MaterialNo}`——**楼层无关，不改**)。`Space_Location.FloorId` 是楼层归属。

**多租户**：Space 实体继承 `BaseBizEntity`（TenantId/IsDeleted/RowVersion）；`CP6Context.OnModelCreating` 反射块自动注册全局过滤 + 单列唯一索引升 `(TenantId,…)` 复合 + SaveChanges 盖章。服务构造只注 `CP6Context`，查询不写 `.Where(TenantId==)`，创建不写 `TenantId=`。错误码族 `E-SPACE-0xx`（裸码，前端 i18n；UI 文本 `t()` plain string，`missingWarn:false` 无警告零新键）。

---

## §2 数据模型

### §2.1 实体

新增 `CP6.Entity/DomainModels/Space/Space_Connector.cs`：
```csharp
[Table("Space_Connector")]
public class Space_Connector : BaseBizEntity
{
    public Guid SiteId { get; set; }                 // 所属站点
    [Required, MaxLength(50)] public string ConnectorCode { get; set; } = "";  // 站内唯一
    public int ConnectorType { get; set; }           // 1=Elevator 2=Stairs 3=Ramp
    [Required, MaxLength(100)] public string Name { get; set; } = "";
}
```
新增 `Space_ConnectorStop.cs`：
```csharp
[Table("Space_ConnectorStop")]
public class Space_ConnectorStop : BaseBizEntity
{
    public Guid ConnectorId { get; set; }            // FK → Space_Connector
    public Guid FloorId { get; set; }                // 所在楼层
    public int X { get; set; }                       // 楼层局部坐标 mm
    public int Y { get; set; }
}
```
枚举 `ConnectorType`（前端镜像；后端 int 存）：`Elevator=1, Stairs=2, Ramp=3`。

### §2.2 DbContext + 索引 + 迁移

- `CP6Context` 加 `DbSet<Space_Connector> Space_Connectors`、`DbSet<Space_ConnectorStop> Space_ConnectorStops`。
- 唯一索引：`Space_Connector` `(SiteId, ConnectorCode)`（经反射块升 `(TenantId, SiteId, ConnectorCode)`）；`Space_ConnectorStop` `(ConnectorId, FloorId)`（升 `(TenantId, ConnectorId, FloorId)`，一连接体每层至多一落点）。
- 迁移 `SpaceP4Connector`（核验：2 表 / 每表 TenantId+IsDeleted+RowVersion / 复合唯一索引 TenantId 前缀）。**纯加法，无既有表改动。**

---

## §3 后端：站点级 pick-path 契约

### §3.1 楼层 Z 标高（堆叠 + 竖直成本基准）

站点楼层按 `Level` 升序，自底向上累加层高赋 Z（mm）：最低 Level 的楼层 `Z=0`，其上每层 `Z = 前一层 Z + 前一层 Height`。负 Level（地下）自然落在序列前段（Z 更小/负）。该 Z 同时供：viewer 堆叠平移、连接体竖直边权（|ZA−ZB|）、A* 3D 启发 z 分量。

### §3.2 新端点 + 响应 VO

`SpaceAdvancedController` 增（既有 `/floor/{id}/pick-path` 不动）：
```
GET /api/space/site/{siteId:guid}/pick-path?taskNo=
```
逻辑：
1. `path = _pick.GetPickPathAsync(taskNo)`（既有 WMS 查询，楼层无关，**不改**）。
2. 取 `codes = path.Items.LocationCode`；join `Space_Location`（`SiteId 经 Floor` 关联、`Placed`、`code∈codes`）解析每 stop 的 `FloorId + AbsX/Y/Z`（库位无 Placed/不在站 → absXYZ null，前端过滤）。
3. 涉及的 `floorIds = stops.FloorId.Distinct()`；查这些 `Space_Floor`（Level/Height）→ §3.1 算 Z（注意 Z 用**全站**楼层序，不只涉及层，保证堆叠一致）。
4. 各涉及层 join `Space_Aisle×Space_Zone` 取中心线。
5. 查站点全部 `Space_Connector` + `Space_ConnectorStop`（连接体可能连未涉及层，但路由只需涉及层间——前端按需取；后端返站点全集简单）。

响应（控制器组装，`Ok2{code,message,data}`）：
```jsonc
{
  "taskNo": "…",
  "floors":   [{ "floorId","floorCode","level","height","z" }],   // 全站楼层(供堆叠) + z
  "stops":    [{ "seq","locationCode","qty","materialNo","floorId","absX","absY","absZ" }],
  "aisles":   [{ "floorId","aisleCode","centerline" }],            // 涉及层 aisles，带 floorId
  "connectors":[{ "connectorCode","type","stops":[{ "floorId","x","y" }] }]
}
```
**WMS 契约 `PickStop` / `IWmsPickTaskQuery` 不变**；`floorId` 仅在 Space VO 层。

### §3.3 测试（xUnit，InMemory）

`SpaceConnector`/site-pick-path 测：跨两层出库单 → stops 带正确 floorId+absXYZ；floors 带 Level 升序 Z 累加正确；connectors 含两层 stop；单站单层退化（floors 1 个、connectors 空）与既有不冲突；多租户隔离。

---

## §4 前端：多层图 + 3D A*

### §4.1 多层节点与 3D 点

新 `advanced/multiFloor.ts`：
```ts
export interface FloorMeta { floorId: string; z: number }   // z=堆叠标高 mm
export interface Pt3 { x: number; y: number; z: number }
/** 多层节点键：楼层命名空间 + 1mm 取整 XY。 */
export const mfKey = (floorId: string, p: { x:number;y:number }) => `${floorId}:${Math.round(p.x)},${Math.round(p.y)}`
```

### §4.2 建多层图：`buildMultiFloorGraph`

输入：`aislesByFloor`（floorId→aisles[]）、`connectors`、`floors`（含 z）。
1. 逐层 `buildCenterlineGraph`（SP3）得各层子图；把各层节点键加 `${floorId}:` 前缀合并入一张多层 `Graph`（adj/nodes/segments 同结构，节点键含 floorId）。`segments` 记 floorId（投影只在本层段上找）。
2. 每个 `connector`：对其每个 stop（floorId,x,y）→ 在该层 `nearestAccess` 投影接入本层巷道（同 SP3 临时接入逻辑，但落成固定节点 `mfKey(floorId,stop)`）。
3. 同一 connector 的 stops 按所在层 `Level` 排序，**相邻两 stop 间加无向竖直边**，权 = `|z(floorA) − z(floorB)|`。
4. `nodePt3(key)→Pt3`：解析 `floorId:x,y`，z 取该层 `z`（连接体节点同理）。

### §4.3 A* 升 3D（drop-in）

`astar` 签名不变 `(adj,start,end,nodePt)`。**关键：只动启发式，绝不动边权与 global `dist`**——
- **边权 `g`**：astar 用 `adj` 里的 `e.w`，不变。层内边的 `w` 仍由 `addEdge` 的 global `dist`（2D）给（同层 z 相同，2D≡3D 正确）；竖直连接体边的 `w` 在 `buildMultiFloorGraph` 显式置 `|Δz|`。**global `dist` 不改**（它还被 addEdge/nearestAccess 复用）。
- **启发式 `h`**：astar 内部把 `h = dist(nodePt(k), nodePt(end))` 改为 **3D-tolerant hypot** `Math.hypot(ax-bx, ay-by, (az??0)-(bz??0))`（z 缺省 0）。`nodePt` 单层返回 `{x,y}`（z=undefined→0）→ 与 2D 完全一致；多层返回 `Pt3`（z=楼层标高）→ 启发自动 3D。
- 跨层 `pathBetweenMF` 接 multi-floor 图 + `nodePt3`（z=层标高）。

> 等价性铁律：单层（nodePt 无 z → z=0）下 3D-tolerant hypot ≡ 2D hypot，且边权/global dist 零改 → SP3 既有 13 测、整套 218 前端测零回归。
> admissible：2D≤3D 欧氏 ≤ 真图最短路（必经连接体不能穿层）→ 3D 启发不高估，最短性保持。

### §4.4 跨层 `pathBetweenMF` + 距离矩阵

- `pathBetweenMF(g, stopA:{floorId,x,y}, stopB)`：各端 `nearestAccess` 到**自己楼层**的段投影接入（FA/FB 临时节点连本层段两端），astar 跑多层图（含竖直边），返回 3D 折线点 `Pt3[]`（连接体段含 z 变化）+ degraded。
- `distanceMatrixMF(g, stops)`：复用 SP3 `distanceMatrixFromGraph` 同构（改用 `pathBetweenMF` + `polyDist3`）。

---

## §5 前端：跨层重排对比 `planPickComparisonMF`

新 `planPickComparisonMF(floors, aislesByFloor, connectors, stops)`：
1. `g = buildMultiFloorGraph(...)`（单次建图）。
2. `actual = planPickRouteOnGraphMF(g, stops)`（LineNo 序，3D 折线）。
3. `matrix = distanceMatrixMF(g, stops)`；`order = optimizeOrder(matrix)` vs actualOrder baseline 兜底（**SP3 routeOptimize 零改**）；`optimized = planPickRouteOnGraphMF(g, reordered)`。
4. 返回 `PickComparison`（同 SP3 结构：actualMm/optimizedMm/savingsPct/degradedPairCount，points 为 `Pt3[]`）。

**actual 跨层口径**：stops 按 `seq`(LineNo) 升序（SP3 §4.5 铁律），跨层不重排——actual 反映真实跨层 LineNo 序；optimized 给「少跑冤枉层/巷」的 what-if。距离含竖直段（电梯上下），savingsPct 体现跨层动线优化。

---

## §6 编辑器：连接体放置工具（Konva 2D）

`space-editor` 加：
- `api/space/connector.ts` + `types/space/connector.ts`（ConnectorVO/StopVO 镜像）。
- 后端 `ConnectorController`（`/api/space/connector`）CRUD：列站点连接体（含 stops）/建连接体/加-改-删某层 stop/删连接体。`ConnectorService`（构造只注 CP6Context；建/改盖章；唯一索引撞 → `E-SPACE-5xx`）。
- 编辑器「放置连接体」工具（`space-editor/connector/`）：当前楼层点击落 stop 点（Konva 标记）→ 弹窗指派连接体（新建[码/类型/名] 或选站点已存）→ 调 API 存该层 stop。连接体面板列站点连接体 + 各层 stop（本层高亮，他层灰显「在 F2…」）。逐层放置累积。

> 编辑器仍单层（`/space/editor/{floorId}`）；连接体跨层通过「在不同楼层分别放置同一连接体的 stop」累积，面板示意它服务哪些层。

---

## §7 前端：全站堆叠 3D viewer

### §7.1 `StackedSceneRoot`

新 `space-viewer/stacked/StackedSceneRoot.ts`：站点每层（按 Level）各 `SceneBuilder` 建场景挂到一个平移至 `position.z = floor.z`（数据空间 mm，经 SceneRoot 的 scale/rotation 收口）的 group；所有层 group 挂 `StackedRoot`。复用 P1 InstancedMesh 桶/坐标适配，不重写渲染。
- 楼层场景按需取：进堆叠模式时并发拉各层 `/scene`，`requestIdleCallback` 分批 build（防一次性卡顿）。
- 楼层显隐 toggle（FloorList 项加眼睛）：隐藏层 group `.visible=false`（保留以便快速切回）。

### §7.2 相机 + 导航

- `CameraController` 复用 OrbitControls：环视整个堆叠；初始 framing 包整栈包围盒。
- `FloorList` 在堆叠模式 → 点楼层 `flyTo` 该层 Z 带中心（聚焦但不卸载其他层）。
- 单层模式（既有 FloorViewer）保留不变；堆叠为新视图（路由 `/space/stacked/{siteId}` 或 FloorViewer 加「堆叠」开关——**取路由分离**，新建 `StackedViewer.vue`，零改 FloorViewer）。

### §7.3 `PathAnimator` 升 3D

- `setPath`/`setComparisonPath` 收 `Pt3[]`：折线顶点用**逐点 z**（替 `GROUND_Z` 常量；连接体段 z 跨层变化 → 竖直/斜线）。
- `_positionCart`：`pointAtDistance3`（3D 弧长）定位小车 z。
- `pathModel.ts` 增 `polylineLength3`/`pointAtDistance3`（3D），或泛化既有（保 2D 兼容 → 单层零回归）。
- 小车/线 parent 到 `StackedSceneRoot`（数据空间 mm，z=楼层标高 + GROUND 抬升）。

---

## §8 测试与验收

### §8.1 vitest 纯逻辑
| 模块 | 用例要点 |
| --- | --- |
| `multiFloor`(mfKey/dist3) | 键含 floorId；dist3 3D 勾股；z=0 退化≡2D |
| `buildMultiFloorGraph` | 两层各子图前缀合并；connector stop 接入本层巷道；相邻层 stop 竖直边权=|Δz|；非相邻层不直连(经中间层) |
| `astar`(3D) | 单层 z=0 与 SP3 等价(13 测零回归)；跨层经连接体最短路；绕开远连接体 |
| `pathBetweenMF`/`distanceMatrixMF` | 跨层折线含 z 变化段；矩阵对称；不连通退化欧氏 |
| `planPickComparisonMF` | optimized≤actual；savingsPct≥0；含竖直段距离；单层退化≡SP3；degradedPairCount |
| `pathModel`(3D) | polylineLength3/pointAtDistance3 含 z；2D 调用兼容 |

前端三门：vue-tsc 0 / vitest 全绿（既有 218 + 新增）/ build。后端 `dotnet test` 全绿（既有 1438 + 新增 connector/site-pick-path）。EF 无 pending。

### §8.2 gstack 运行态（真库真栈）
**新多层 demo 种子**：站点 ≥2 层（F1 Level1 + F2 Level2，各有巷道网格 + 已发布库位）+ 一部电梯 `Space_Connector`(Elevator) 两 stop（F1/F2 各一落点）+ 一张出库单 stops 跨两层（含 `[LineNo]`/`QUOTED_IDENTIFIER ON`/Placed 库位带 RackId 等 SP3 种子坑）。
验收点：
1. **堆叠**：`/space/stacked/{siteId}` 渲染 F1+F2 于各自 Z（F2 在 F1 上方），全几何（rack/location）。
2. **跨层路径**：加载出库单 → 路径在 F1 段走巷道 → 经电梯**竖直上到 F2** → F2 段走巷道；小车沿 3D 路径上下。
3. **对比**：面板「实际/优化/省%」，绿优化线 3D 叠加；开关增删。
4. **编辑器**：放置连接体工具在 F1/F2 各落一 stop 指派同一电梯，面板示意服务两层。
5. **无回归**：单层 FloorViewer(08/SP3 单巷 pick-path)、07 库存、08 热图全正常。
固化 `docs/superpowers/qa/space-p4-multifloor/`（README + seed.sql + 截图）。

---

## §9 文件清单（新增/改动）

**后端新增**：`CP6.Entity/DomainModels/Space/{Space_Connector,Space_ConnectorStop}.cs`；`CP6.Core/Services/Space/{IConnectorService,ConnectorService}.cs`；`CP6.WebApi/Controllers/Space/ConnectorController.cs`；`CP6.Core/Migrations/*_SpaceP4Connector.*`；测试 `CP6.Tests/Space/{ConnectorServiceTests,SitePickPathTests}.cs`。
**后端改动**：`CP6Context`(2 DbSet+索引)；`SpaceAdvancedController`(+site-level pick-path 端点)；DI 注册 ConnectorService。
**前端新增**：`types/space/connector.ts`+`api/space/connector.ts`；`space-viewer/advanced/multiFloor.ts`(+`.spec`)；`space-viewer/advanced/planMultiFloor.ts`(`buildMultiFloorGraph`/`pathBetweenMF`/`distanceMatrixMF`/`planPickComparisonMF`,+`.spec`)；`space-viewer/stacked/StackedSceneRoot.ts`(+`.spec`)；`views/space/stacked/StackedViewer.vue`+路由；`space-editor/connector/`(放置工具+面板)。
**前端改动**：`PickPathPlanner.astar`(3D hypot via nodePt，单层兼容)；`PathAnimator`(Pt3/逐点 z)；`pathModel`(polylineLength3/pointAtDistance3)；`api/space/advanced.ts`(+sitePickPath)；`types/space/advanced.ts`(+floorId/floors/connectors VO)。
**无既有单层链改动**：FloorViewer/floor pick-path/08 热图/07 库存零改。

---

## §10 交付顺序（subagent-driven TDD，分期）

- **A 后端数据**：实体 + DbContext + 迁移（核验）。
- **B 后端契约**：ConnectorService CRUD + ConnectorController + site-level pick-path 端点 + VO + DI；xUnit。
- **C 前端多层图+A***：`multiFloor.ts` + `buildMultiFloorGraph` + astar 3D（单层等价性 spec）+ `pathBetweenMF`/`distanceMatrixMF`；vitest。
- **D 前端跨层对比**：`planPickComparisonMF`（复用 routeOptimize）+ `pathModel` 3D；vitest。
- **E 编辑器工具**：connector api/types + 放置工具 + 面板。
- **F 堆叠 viewer**：`StackedSceneRoot` + `StackedViewer.vue` + 路由 + 相机/显隐。
- **G 3D 动画 + 接线**：`PathAnimator` 3D + StackedViewer 接 site-pick-path + `planPickComparisonMF` 喂 actual/optimized；前端三门。
- **H QA**：多层 demo 种子 + gstack 验收 5 点，固化证据。

每阶段实现→spec 审→质量审→修；纯逻辑 vitest 当场绿，画布/堆叠运行态留 H gstack。

---

## §11 SP5 预告 / 遗留

连接体容量/调度/电梯计时成本（D3 之上）；连接体类型扩展；楼层显隐/剖切高级控件；堆叠性能极限调优（大楼层数 LOD/虚拟化）；编辑器连接体跨层一键复制对齐。独立增量。
