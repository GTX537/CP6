# Space 08 · 高级可视化 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

> **v1.1 评审补丁（2026-06-27 深审）**：本版按深度评审打补丁（相关处标「(v1.1评审补丁)」）——① **契约对齐 07 v1.1 跨仓维度**：`PickPathDto.items` / `WorkloadDto` 均加 `warehouseCd`，join key 为 `(WarehouseCd, LocationCode)` 复合（防多仓同名库位撞），全查询带 `tenantId`；② **拣货路径数据源 = 波次拣货 `WavePickTask`（P3 跨分支依赖）**：最佳源在 `feat/wms-wave-picking` 分支、未进 main——合并前 `IWmsPickTaskQuery` 为桩/空，合并后按字段映射表实现（§2.1.1 / §6.1）；③ **作业热图 = 扩展 07 着色管线的第 4 种模式**（07 v1.1 只有 状态/利用率热力/结构 三模式），共用 05 `setInstanceColor` 底层（§4）；④ **路径规划算法定死**：库位垂足投影到最近中心线段（点到线段最近点公式）+ 中心线图构建 + Dijkstra/A* + 不连通退化直连 W-SPACE-801 判定（§3，已确认 00 §5.2 建图信息充分）；⑤ **作业热图数据源 = WMS `StockTransaction`** 按 `TxnType`/`RelatedType` + `TxnDateTime` 时间窗聚合（§4 / §6.1）；⑥ **相机跟随巡游**明确与 06 `flyTo` 的同步机制（§2.4）；⑦ 三契约**成熟度分级**标注（§6.1）；设备联动 v1 占位保持（YAGNI 恰当）。

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-08 高级可视化 |
| 所属模块 | Space 空间数字底座 · **Part 3（P3）** |
| 里程碑 | **P3**（拣货路径 3D 动画 + 作业热图 + 设备联动占位；P1 渲染 + P2 数据之上的"演示级"可视化） |
| 技术栈 | Vue3 + Three.js（路径/动画/热图）/ .NET8（`IWmsPickTaskQuery`、`IWmsWorkloadQuery` 契约，WMS 实现） |
| 命名空间 | `cp6.web/src/space-viewer/advanced`（路径/热图/设备）/ `CP6.Core/Services/Space`（查询契约·Space 侧定义） |
| 落地决策 | 拣货路径**消费 Aisle 中心线**走巷道（不穿墙直连）/ 作业热图**扩展 [07](./07-stock-overlay.md) 着色管线（新增第 4 种「作业热图」模式·v1.1评审补丁）** + WMS 作业统计 / **设备联动 v1 仅占位**（接口预留 + 静态示意，实际联动 P3+） |
| 依赖 | [05 渲染内核](./05-viewer-core.md)（场景图、按需渲染、SceneRoot 挂点）、[06 定位](./06-camera-pick.md)（相机补间巡游 `flyTo`）、[07 叠加](./07-stock-overlay.md)（着色管线/利用率数据源）、[00](./00-data-model.md)（**Aisle 中心线 §5.2 `Centerline`**、(WarehouseCd,LocationCode) join key） |

> **题眼**：P1 看结构、P2 看库存、**P3 看"作业怎么发生"**。三件事：① **拣货路径动画**——WMS 给一串拣货库位编码，08 把它们**沿巷道中心线**（00 §5 的 Aisle `Centerline`）连成一条不穿墙的合理路径，让小车/光点按拣货顺序跑动画；② **作业热图**——把 WMS 的出入库频次着到库位/库区上，一眼看出"哪片最忙"；③ **设备联动**——AGV/堆垛机/输送线的实时位置叠加，但 **v1 只占位**（预留接口 + 静态示意，真接设备是 P3+）。**记住一句**：08 全是**只读演示增强**，数据真相在 WMS（路径序列、作业统计、设备状态都向 WMS 只读拉，沿用 07 的单向契约手法）；08 的价值是**把空间 + 数据 + 时间**三者在 3D 里讲成一个故事——这是商用底座对 WMS 客户最有"卖相"的一章。

---

## 目录
- 第1章 功能概述与定位（P3 演示级，哪些做实哪些占位）
- 第2章 拣货路径动画（消费 Aisle 中心线·路径规划·动画）
- 第3章 路径规划（库位 → 巷道中心线图 → 最短巡游）
- 第4章 作业热图（频次 → 热力，复用 07 着色）
- 第5章 设备联动（v1 占位：接口预留 + 静态示意）
- 第6章 查询契约（IWmsPickTaskQuery / IWmsWorkloadQuery）
- 第7章 性能与降级（动画按需渲染 / 大路径简化）
- 第8章 API 接口
- 第9章 消息一览
- 第10章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：在 P1/P2 之上提供"作业级"高级可视化——拣货路径 3D 动画、作业热图、设备联动占位，把仓库的**动态作业**在 3D 中讲清楚，作为商用底座的高价值演示能力。

**本章范围（08）与成熟度：**
| 能力 | v1 成熟度 | 说明 |
|---|---|---|
| 拣货路径动画 | **做实**（数据源跨分支·v1.1评审补丁） | 消费 WMS 拣货任务（最佳源 `WavePickTask` 在 `feat/wms-wave-picking` 分支、**未进 main = P3 跨分支依赖**，合并前契约桩/空，见 §2.1.1）+ Aisle 中心线，路径规划 + 动画 |
| 作业热图 | **做实** | 消费 WMS 作业频次统计（聚合 `StockTransaction`，§4.1），**扩展 07 着色管线为第 4 种模式**（v1.1评审补丁） |
| 设备联动 | **占位** | 接口/挂点预留 + 静态示意；真接 AGV/设备实时流 = P3+ |

**不含（划清边界）：**
| 能力 | 去哪 |
|---|---|
| 3D 渲染地基 / 相机 / 着色 API | [05](./05-viewer-core.md)/[06](./06-camera-pick.md)（08 复用） |
| 库存状态/利用率数据 | [07 章](./07-stock-overlay.md)（热图复用其数据源） |
| WMS 拣货/作业**业务逻辑与写入** | WMS 模块（08 只读拉数据演示） |
| 真实设备实时控制/双向联动 | P3+ / 未来（v1 仅占位） |

> **P3 的定位是"演示与洞察"，不是"作业执行"**：08 不下发拣货指令、不调度设备，只把 WMS 已有的作业数据在 3D 中**回放/呈现**。所以全章只读、可降级、失败不影响 P1/P2。

---

## 第2章 拣货路径动画

### 2.1 数据流
```
WMS 拣货任务（拣货单/波次）→ IWmsPickTaskQuery.GetPickPathAsync(tenantId, taskNo)   // ★(v1.1评审补丁) 带 tenantId
  → PickPathDto.items 有序拣货点 [{warehouseCd, locationCode, …}, …]（拣货顺序 = WMS 已优化的顺序，或 08 重排）
→ 解析每点 AbsXYZ（按 (warehouseCd, locationCode) 复合键 join Space 库位，00）  // ★(v1.1评审补丁) 对齐 07 v1.1，防多仓同名库位撞
→ 路径规划（第3章）：沿 Aisle 中心线把相邻拣货点连成不穿墙路径
→ 动画：小车/光点沿路径跑，按序点亮拣货库位 + 顺序编号牌
```
- 拣货顺序**默认采用 WMS 给的顺序**（WMS 已做拣货优化）；08 可选按"沿巷道最短"重排做演示对比（标注"仅可视化重排，不回写 WMS"）。

#### 2.1.1 数据源：波次拣货 WavePickTask（v1.1评审补丁 · P3 跨分支依赖）
> **最佳数据源是波次拣货 `WavePickTask`**（字段 `PickSeq` / `FromLocationCd` / `ProductCd` / `RequiredQty` / `SourceOutboundNo`）。但它在 **`feat/wms-wave-picking` 分支、尚未合并进 main**——**合并前** `IWmsPickTaskQuery` 实现为**桩/空**（返回空 items，08 路径动画显"暂无拣货任务"，不阻塞）；**合并后**由 WMS 侧按下表映射实现。08 只依赖契约抽象，不耦合该分支进度。

**`WavePickTask` → `PickPathDto` 字段映射：**

| `PickPathDto`（头 / items[]） | ← `WavePickTask` | 说明 |
|---|---|---|
| `taskNo`（头） | `SourceOutboundNo` | 来源出库单号（一波次可拼多单） |
| `items[].seq` | `PickSeq` | 拣货顺序号（WMS 已优化的拣货序） |
| `items[].warehouseCd` | 波次所属 `WarehouseCd` | ★join 跨仓维度，对齐 07 v1.1 |
| `items[].locationCode` | `FromLocationCd` | 拣货源库位 → join Space 库位取 AbsXYZ |
| `items[].materialNo` | `ProductCd` | 物料号（到达时可显） |
| `items[].qty` | `RequiredQty` | 应拣量（到达时显拣货量） |

### 2.2 动画呈现
| 元素 | 表现 |
|---|---|
| 路径线 | 沿巷道中心线的折线/平滑曲线，高亮色，可显总距离 |
| 移动体 | 小车/光点沿路径匀速或按节奏移动（可调速、暂停、步进） |
| 拣货点 | 到达时该库位高亮脉冲 + 顺序号牌（1,2,3…）+（接 07）显拣货量 |
| 相机 | 可选**跟随巡游**（复用 06 相机补间，跟移动体）或固定俯视看全程 |
- 播放控制：播放/暂停/步进/调速/重播；多任务可叠多条路径（不同色）。

### 2.3 与 Aisle 中心线的关系
- 路径**必须走巷道**（00 §5 Aisle 的 `Centerline`），不能两库位直连（会穿货架/墙，不真实）。这正是 00 把 Aisle 中心线作为几何存下来的下游用途之一。
- 无巷道库区（00：Aisle 可空）的库位 → 路径退化为"到该区入口直线 + 区内就近"，或标注"该区无巷道路径，近似直连"。

### 2.4 相机跟随巡游与 06 flyTo 的同步（v1.1评审补丁）
> 路径动画是**进度驱动**（0→100% 沿路径采样位置），06 `flyTo` 是**固定时长补间**（06 §5）；二者时间轴不同，须明确联动机制，避免相机与移动体脱节或双补间打架。

| 机制 | 做法 | 适用 |
|---|---|---|
| **A. 动画驱动相机 target 注入**（v1 默认） | 不调 06 `flyTo`，而是**每帧把移动体当前位置写入相机 `target`**（相机自身偏移/俯角保持不变），相机随动画进度连续跟随——**单一时间轴**（动画进度），无双补间冲突；`requestRender` 由动画循环驱动（§7） | 平滑跟车巡游 |
| **B. 分段 flyTo** | 把整条路径按拣货点切段，**每到一个拣货点触发一次 06 `flyTo`** 飞到该点视角看清库位，段间停留；到点即"暂停动画 → flyTo → 续播"，让 flyTo 固定时长与动画进度对齐 | 逐点讲解演示 |
- **暂停/调速联动**：暂停 → 机制 A 相机停在当前 target、机制 B 停在当前段；调速 → 机制 A 相机跟随速度随动画进度自动同步，机制 B 按新速度重算每段 flyTo 时长。

---

## 第3章 路径规划（中心线图）

### 3.1 构建巷道中心线图（一次，缓存）
> **建图信息来源已确认充分（v1.1评审补丁）**：00 §5.2 `Space_Aisle.Centerline` 存折线节点序列（如 `[[600,0],[600,8000]]`，mm `[x,y]`），相邻节点即一条中心线段——足以建出无向加权图 G（端点坐标 + 段连接全有；交叉点靠线段求交几何推出）。

```
把当前层所有 Aisle 的 Centerline 连成一张无向加权图 G：
  ① 段：每条 Centerline 的相邻节点对 (p_i, p_{i+1}) = 一条中心线段
  ② 节点 = 段端点 ∪ 段间交叉点 ∪ 库位接入垂足
       · 端点：每条 Centerline 折线的各节点
       · 交叉点：两段几何相交（线段求交）→ 在交点处拆段并互连（不同 Aisle 在交点连通）
       · 库位接入垂足：见 §3.1.1（库位投影到最近段的垂足）
  ③ 边 = 中心线段（权重 = 段长 mm，欧氏距离）；交叉点/垂足处拆出的子段各自计权
→ 缓存（key = FloorId；几何不变复用，改 Aisle/Rack 几何才失效重建）
```
- 节点去重容差：坐标按 mm 取整 + ε 容差合并（端点/交点/垂足重合视为同一节点），避免浮点裂点。

### 3.1.1 库位接入垂足：点到线段最近点（v1.1评审补丁）
> 库位锚点 P（AbsXYZ 的水平投影 `(px,py)`）须先"接入"中心线图——取 P 到**全层所有中心线段**的最近点（垂足）作为接入节点 P'，再以短直线段把 P↔P' 连入 G。

**点到线段 AB 最近点 Q（标准公式，逐段算取 dist 最小者）：**
```
对每条中心线段 A=(ax,ay), B=(bx,by)，库位 P=(px,py)：
  ABx=bx-ax;  ABy=by-ay
  APx=px-ax;  APy=py-ay
  L2 = ABx*ABx + ABy*ABy                       // 段长平方
  t  = (L2==0) ? 0 : (APx*ABx + APy*ABy) / L2  // 投影参数
  t  = clamp(t, 0, 1)                          // ★夹到 [0,1]：垂足落段外则取端点
  Q  = (ax + t*ABx, ay + t*ABy)                // 该段上离 P 最近的点
  dist = hypot(px-Q.x, py-Q.y)
取所有段中 dist 最小者 → 接入垂足 P'（记其所属段，用于插点拆段）
```
- 垂足落段内（0<t<1）→ 在该段插入 P' 节点（拆成两子段，权重各为子段长）；落端点（t=0 或 1）→ 直接接到该端点节点。
- 接入直线段 P↔P' 权重 = `dist`，计入路径总距离。

### 3.2 单段路径（相邻两拣货点）
```
pathBetween(codeA, codeB):
  a' = A 库位接入垂足（§3.1.1，投影到最近中心线段）
  b' = B 库位接入垂足
  在 G 上 Dijkstra（或 A*，启发函数 h = 到目标的欧氏直线距离，admissible）求 a'→b' 最短路 → 沿中心线的折线
  首尾接上 库位↔接入垂足 的短直线段（A↔a'、b'↔B）
```
- 整条拣货路径 = 依次 `pathBetween(code_i, code_{i+1})` 拼接。
- 算法在**前端**用中心线图算（数据量小：一层巷道几十段，Dijkstra 足够；A* 仅大图提速）；超大图可服务端预算。

### 3.3 不连通退化直连（W-SPACE-801·v1.1评审补丁）
> **判定条件（命中任一即该段退化为直连 + W-SPACE-801「巷道路径不连通，近似直连」，不中断动画）：**
- **① 库位无法接入任何中心线**：库位 P 到所有中心线段的最小 `dist` 超阈值（如 > 该层巷道间距经验值），即该库位附近无中心线可投影（建模未画该区巷道）→ 该点与前后点直连。
- **② 中心线断开（不连通分量）**：a'、b' 分属 G 的**不同连通分量**（Dijkstra/A* 找不到 a'→b' 路径）→ 该段直连。
- **③ Centerline 为空**：00 §5.2 该 Aisle `Centerline = "[]"` → 该区按"到区入口直连 + 区内就近"近似（§2.3）。

- 退化段在 UI 上用虚线/异色标"近似直连"；路径只为**可视化演示**、非作业指令——近似可接受，不追求作业级精确。

---

## 第4章 作业热图

### 4.1 数据与着色
```
IWmsWorkloadQuery.GetWorkloadAsync(tenantId, floorId, window) → [{warehouseCd, locationCode, opCount}]
  opCount = 该库位在时间窗内的作业次数（出入库 / 拣货）
→ 归一化 opCount → 热力色（冷→暖）→ 05 setInstanceColor
   （★v1.1评审补丁：扩展 07 着色管线为第 4 种「作业热图」模式，共用 05 setInstanceColor 底层，不重建几何）
```
- **★(v1.1评审补丁) opCount 数据源 = WMS `StockTransaction`**：按 `(WarehouseCd, LocationCd)` 分组、`TxnDateTime` 落在时间窗内聚合计数——业务事实表已具备聚合所需字段（**已确认**：`TxnType` IN/OUT/MOVE/ADJ…、`RelatedType` INBOUND/OUTBOUND/STOCKTAKE、`TxnDateTime` 发生时刻、`WarehouseCd`/`LocationCd`）。忙度口径：`opCount = count(TxnType ∈ {IN,OUT,MOVE})`；"拣货忙度"可进一步按 `RelatedType = OUTBOUND` 过滤。
- **扩展（非复用）07 着色管线**：07 v1.1 只有 **状态 / 利用率热力 / 结构** 三模式；08 新增**第 4 种「作业热图」模式**，共用 07 的 `setInstanceColor` + 快照缓存 + 可见区裁剪 + LOD 聚合底层——08 只换"数据源"（频次 vs 库存状态）和"色映射"，不另造管线。
- 时间窗 `window`（今日/本周/自定义）；聚合到货架/库区看"忙区"分布（远 LOD）。

### 4.2 与库存利用率热力（07）的区别（v1.1评审补丁）
> 二者**同为热力着色但语义正交**：07 利用率热力看**「满」**（空间占用的静态快照），08 作业热图看**「忙」**（作业频次的时间窗累计）——一个库位可以很满却不忙（呆滞），也可以不满却很忙（高周转）。

| | 07 利用率热力（第 2 模式） | 08 作业热图（**第 4 模式·新增**） |
|---|---|---|
| 数据 | 当前库存占用率（静态快照） | 作业频次（时间窗累计） |
| 回答 | "哪里**满**了" | "哪里最**忙**" |
| 数据源 | `IWmsStockQuery` | `IWmsWorkloadQuery`（聚合 `StockTransaction`） |
| 着色模式 | 07 三模式之一（利用率热力） | **扩展为第 4 种模式** |
- 二者共用同一着色底层（05 `setInstanceColor`）；08 把 07 的「状态 / 利用率热力 / 结构」三模式**扩展为加入「作业热图」的第 4 种**，模式切换即重着色（用缓存快照，不重拉）。

---

## 第5章 设备联动（v1 占位）

> v1 **仅占位**：预留接口与渲染挂点 + 静态示意，**不接真实设备实时流**（真联动 = P3+）。

### 5.1 占位内容
- **渲染挂点**：在 SceneRoot 下预留 `DeviceLayer`，可放设备图元（AGV/堆垛机/输送线，仍用 D3 参数化盒体/简单几何，零素材）。
- **静态示意**：可手工/示例数据摆几个设备图标在 3D 中（演示用），标注"示意，未接实时"。
- **接口预留**：定义 `IWmsDeviceQuery`（占位签名，第6章），WMS 暂可返回空/桩数据。

### 5.2 未来（P3+，不在 v1）
- 接 WMS/WCS 设备实时位置流（轮询或推送）→ 设备图元实时移动；
- AGV 路径与拣货路径叠加；设备状态（忙/闲/故障）着色。
- v1 把"位置"留成可注入数据点，未来换成实时源即可——架构上不返工。

> **为什么占位而非砍掉**：设备联动是商用底座的远期卖点，v1 留挂点 + 示意保证"演示时能讲到、架构上能接入"，但不投入实时集成成本（YAGNI，等有客户真需求再做）。

---

## 第6章 查询契约（Space 侧定义，WMS 实现）

沿用 07 的单向只读手法，新增两个查询契约（设备一个占位）：
```csharp
// 拣货路径
public interface IWmsPickTaskQuery {
    Task<PickPathDto> GetPickPathAsync(Guid tenantId, string taskNo, CancellationToken ct = default);  // ★(v1.1评审补丁) tenantId 必带，对齐 07 IWmsStockQuery 多租户
    // PickPathDto { taskNo, items:[{seq, warehouseCd, locationCode, qty, materialNo}] } —— 有序拣货点
    //   ★(v1.1评审补丁) items 每项加 warehouseCd：join key = (WarehouseCd, LocationCode) 复合，对齐 07 v1.1，防多仓同名库位撞
}
// 作业热图
public interface IWmsWorkloadQuery {
    Task<IReadOnlyList<WorkloadDto>> GetWorkloadAsync(
        Guid tenantId, Guid floorId, DateRange window, CancellationToken ct = default);
    // WorkloadDto { warehouseCd, locationCode, opCount }   ★(v1.1评审补丁) 加 warehouseCd（跨仓维度）
}
// 设备（v1 占位，WMS 可返回空）
public interface IWmsDeviceQuery {
    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(Guid tenantId, Guid floorId, CancellationToken ct = default);
    // DeviceDto { deviceId, type, warehouseCd, locationCode|absXYZ, status } —— v1 桩/空
}
```
- 全部**纯读、单向、join 按 (WarehouseCd, LocationCode) 复合键 + 楼层**（★v1.1评审补丁：对齐 07 v1.1 跨仓维度，防多仓同名库位撞）；WMS 实现，Space 只依赖抽象（无反向编译依赖）。
- 与 07 `IWmsStockQuery` 并列，构成 Space→WMS 的"只读查询契约族"。

### 6.1 三契约成熟度分级（v1.1评审补丁）
> 三契约**接口同期定义、实现分期落地**——成熟度不同，须按级对待：

| 契约 | 成熟度 | 数据源 / 实现状态 |
|---|---|---|
| `IWmsPickTaskQuery` | **P3 跨分支待合并** | 最佳源 `WavePickTask` 在 `feat/wms-wave-picking` 分支、未进 main；合并前实现为**桩/空**（返回空 items），合并后按 §2.1.1 映射表实现 |
| `IWmsWorkloadQuery` | **依赖 `StockTransaction` 聚合** | 数据源已在 main；实现 = 按 `(WarehouseCd, LocationCd)` + `TxnDateTime` 时间窗聚合计数（§4.1）；建议 WMS 侧补 `IX(WarehouseCd, LocationCd, TxnDateTime)` 支撑聚合 |
| `IWmsDeviceQuery` | **占位桩** | v1 不接真实设备流，WMS 返回空/示例（第 5 章 YAGNI）；真联动 P3+ |

---

## 第7章 性能与降级

| 机制 | 做法 |
|---|---|
| 动画按需渲染 | 路径动画播放时才持续 `requestRender`（05 §9.1）；暂停/无动画即停渲染 |
| 中心线图缓存 | 路径图楼层级缓存，几何不变复用（第3.1） |
| 大路径简化 | 拣货点极多（百级）→ 路径线抽稀 + 移动体跳关键点，避免每帧重算 |
| 热图复用 07 | 着色/裁剪/LOD 全复用 07，不另造管线 |
| WMS 不可用 | 路径/热图/设备查询失败 → 该高级功能不可用提示，**不影响 P1/P2**（结构 + 库存照常） |

> 同 07 降级原则：08 是**最上层增强**，任何数据源失败只让对应高级功能降级，绝不拖垮下层。

---

## 第8章 API 接口

| 端点/契约 | 类型 | 说明 |
|---|---|---|
| `IWmsPickTaskQuery.GetPickPathAsync` | C# 契约 | 拣货任务有序库位序列（第2.1） |
| `IWmsWorkloadQuery.GetWorkloadAsync` | C# 契约 | 作业频次统计（第4.1） |
| `IWmsDeviceQuery.GetDevicesAsync` | C# 契约（占位） | 设备列表（v1 桩/空，第5章） |
| `GET /api/space/floor/{id}/pick-path?taskNo=` | HTTP | 取拣货路径（服务端调 IWmsPickTaskQuery + 解析 AbsXYZ）→ 前端规划动画 |
| `GET /api/space/floor/{id}/workload?from=&to=` | HTTP | 取作业热图数据（服务端调 IWmsWorkloadQuery） |
| `GET /api/space/floor/{id}/devices` | HTTP（占位） | 取设备示意（v1 空/示例） |
- 前端经 Space 后端中转（鉴权/多租户/可见区裁剪），不直连 WMS（同 07）。
- **(v1.1评审补丁)** 契约返回均带 `warehouseCd`（join 跨仓维度，对齐 07 v1.1）；`pick-path` 在 `feat/wms-wave-picking` 合并前返回空（§6.1 成熟度）；`workload` 由 `StockTransaction` 时间窗聚合（§4.1）。

---

## 第9章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| W-SPACE-801 | Warn | 巷道路径不连通，近似直连显示 | 中心线缺失/不连通（第3.3） |
| I-SPACE-801 | Info | 拣货路径：N 个拣货点，总距离 D 米 | 路径加载完成（第2.1） |
| W-SPACE-802 | Warn | 高级可视化数据获取失败 | WMS 路径/热图查询失败，降级（第7） |
| I-SPACE-802 | Info | 作业热图（时间窗 from~to）已加载 | 热图数据加载（第4.1） |
| I-SPACE-803 | Info | 设备联动为演示示意（未接实时） | 打开设备图层（v1 占位提示，第5章） |

---

## 第10章 集成与依赖

| 关系 | 说明 |
|---|---|
| → WMS（同步只读） | 新增 `IWmsPickTaskQuery`(★P3 跨分支待合并·桩 → `WavePickTask` 合并后实现)/`IWmsWorkloadQuery`(★聚合 `StockTransaction`)/`IWmsDeviceQuery`(占位桩)；与 07 `IWmsStockQuery` 同族，单向纯读，join 按 (WarehouseCd,LocationCode)·v1.1评审补丁 |
| ← 00 数据模型 | **Aisle 中心线 §5.2 `Centerline` JSON**（→ 中心线图建图，已确认信息充分，第 3 章）；库位 AbsXYZ（路径点 / 垂足投影）；(WarehouseCd,LocationCode) join key |
| ← 05 渲染内核 | SceneRoot 挂路径/设备图元；按需渲染驱动动画；`setInstanceColor` 热图着色（共用底层） |
| ← 06 定位 | 相机补间做路径跟随巡游（★v1.1评审补丁：默认"动画驱动相机 target 注入"，可选分段 `flyTo`，§2.4） |
| ← 07 叠加 | 作业热图**扩展** 07 着色管线（共用裁剪 + LOD 聚合）；07 三模式 → **新增第 4 种"作业热图"模式**·v1.1评审补丁 |
| → PUB 权限 | 高级可视化接功能权限；按数据权限限可见库位/楼层 |
| 多租户 | 全查询带 TenantId，经 Space 后端中转 |

---

## 自检
- [ ] 拣货路径为什么必须走 Aisle 中心线、不能两库位直连？这兑现了 00 把中心线存几何的什么用途？
- [ ] 路径规划怎么把库位 + 中心线变成可走的图？相邻拣货点最短路怎么求？不连通时怎么降级？
- [ ] 作业热图与 07 利用率热力的数据源/回答的问题各是什么？为什么能共用着色管线？
- [ ] 设备联动 v1 为什么只占位？占位都预留了什么？为什么留挂点而非砍掉？
- [ ] 08 的三个查询契约与 07 的契约是什么关系？为什么都单向只读、定义在 Space 侧？
- [ ] 为什么说 08 是"只读演示增强"？任一数据源失败为什么不能影响 P1/P2？

---

*实现：新建 `cp6.web/src/space-viewer/advanced/*`（PickPathPlanner[中心线图+最短路] + PathAnimator + WorkloadHeatmap + DeviceLayer占位）+ Space 后端 `/pick-path`、`/workload`、`/devices` 中转端点 + 契约 `IWmsPickTaskQuery`/`IWmsWorkloadQuery`/`IWmsDeviceQuery`（WMS 实现/桩）。配套 xlsx（拣货路径数据流 / 中心线图构建 / 热图色映射 / 设备占位说明 / 查询契约族总表）见同名 `.xlsx`。*
