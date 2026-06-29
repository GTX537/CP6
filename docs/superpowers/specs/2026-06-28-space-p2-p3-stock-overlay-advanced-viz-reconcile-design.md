# Space P2/P3 · 07 实时库存叠加 + 08 高级可视化 — Reconcile/落地设计

*--- 可直接驱动实施计划的 as-built 对账版 ---*

| 属性 | 内容 |
|---|---|
| 文档ID | SPACE-P2P3-RECONCILE（07 + 08 合并落地设计） |
| 所属模块 | Space 空间数字底座 · Part 2（07）+ Part 3（08） |
| 设计源（不推倒） | 丛书 `docs/space/07-stock-overlay.md`（07 详规）/ `docs/space/08-advanced-viz.md`（08 详规）/ `docs/space/00-data-model.md`（join key、Aisle 中心线）/ `docs/space/05-viewer-core.md`、`06-camera-pick.md`（复用） |
| 本文定位 | **Reconcile/delta 设计**：把丛书 07/08 映射到 P1 as-built 代码 + 锁定本会话决策（接真/在拣派生/计划拆分）。**不复述丛书全文**，只锁"落地差异 + 可落码细节"。 |
| 技术栈 | .NET8（`IWmsStockQuery` 等只读契约 + WMS 接真实现）/ Vue3 + Three.js（overlay 着色 / advanced 路径·热图，复用 05/06） |
| 落地决策（本会话拍板） | ① **接真 + 播演示种子**（替掉 StubWmsStockQuery，读 T_Stock/T_Location/StockTransaction/OutboundOrder 真表）② **在拣派生 = join 出库单拣货中明细**（`OutboundOrder.Status=Picking` 且 `AllocatedQty>ShippedQty`）③ **交付 = 本 reconcile spec + 两份 plan（07 先、08 后）** |
| 依赖（as-built 已验） | `IWmsStockQuery`（`CP6.Core/Services/Integration/`，现单条桩）/ `ViewerHandle.setInstanceColor`+`requestRender`+`flyToData`/ `InstancedBuckets.setColor`/ `Highlighter`/ `Space_Aisle.Centerline`/ `LocationPublishService`（04 停用校验已是契约首消费者） |

---

## §0 阅读顺序与边界

1. 落码前必读丛书 `07-stock-overlay.md` 与 `08-advanced-viz.md`（设计真相在那）。本文是**它们与 as-built 的差集 + 决策固化**。
2. 与丛书冲突处，**以本文 as-built 决议为准**（丛书写于 P1 落码前，部分命名/签名按当时设想）。
3. 范围内：契约族（4 个）、WMS 接真实现、Space 后端中转端点、前端 overlay/advanced、04 回归、演示种子、测试。
4. **范围外（守丛书边界）**：3D 渲染/相机/拾取地基（05/06 已成，只复用）、WMS 库存业务写入（只读）、真实设备实时联动（08 仅占位）、租户级配色 UI（YAGNI，字段预留）、实时推送（无 SignalR，守 D5）。

---

## §1 架构 — Space→WMS 只读查询契约族

四个契约**单向、纯读、join 按 LocationCode**，定义在**消费者（Space）侧**，WMS 实现，DI 注入；Space 仅依赖抽象，无反向编译依赖。前端**不直连 WMS**，一律经 Space 后端中转端点（统一鉴权 / 多租户 / 可见区裁剪），后端再调契约。

| 契约 | 章 | 接真数据源 | 成熟度 |
|---|---|---|---|
| `IWmsStockQuery`（**扩**既有） | 07 | `T_Stock` + `T_Location`（+ `OutboundOrderDetail` 判在拣） | 做实 |
| `IWmsPickTaskQuery` | 08 | `OutboundOrder` + `OutboundOrderDetail` | 做实 |
| `IWmsWorkloadQuery` | 08 | `StockTransaction`（库位×时间窗计次） | 做实 |
| `IWmsDeviceQuery` | 08 | 桩返空（占位） | 占位 |

### 1.1 命名空间（as-built 决议）

| 物 | 丛书设想 | **本文决议（as-built）** |
|---|---|---|
| 契约位置 | `CP6.Core/Services/Space/` | **`CP6.Core/Services/Integration/`**（扩既有 `IWmsStockQuery.cs`；08 三契约也置此，归"集成契约"族，与现状一致） |
| WMS 实现 | `CP6.Core/Services/Wms/WmsStockQuery.cs` | 同（`CP6.Core/Services/Wms/` 下新建 `WmsStockQuery.cs`/`WmsPickTaskQuery.cs`/`WmsWorkloadQuery.cs`/`WmsDeviceQuery.cs`） |
| Space 中转端点 | `views/space/viewer` 调 | `CP6.WebApi/Controllers/Space/`（新 `SpaceStockController`、`SpaceAdvancedController`；或并入既有 `SpaceLocateController`，二选一由 plan 定） |
| 前端 overlay | `cp6.web/src/space-viewer/overlay` | 同 + `cp6.web/src/space-viewer/advanced`（08） |
| 着色 API | 丛书称 `setInstanceColor` | as-built = `ViewerHandle.setInstanceColor(locationId, hex)` → `InstancedBuckets.setColor`（**名字对得上**，直接复用） |

### 1.2 多租户（as-built 决议）

丛书契约签名带显式 `Guid tenantId` 参数。**as-built 决议：去掉该参数**——`Stock`/`Location`/`StockTransaction`/`OutboundOrder*` 均继承 `BaseBizEntity`，`CP6Context` 全局查询过滤按 ambient `TenantContext` 自动隔离（与 P1 服务一致：构造只注 `CP6Context`，查询不写 `.Where(TenantId==)`）。中转端点在 Space 后端，鉴权/租户由既有管线保证。

### 1.3 DI 替换

`Program.cs` 现注册 `StubWmsStockQuery`。本次：
- `IWmsStockQuery` → `WmsStockQuery`（接真）。
- 新增注册 `IWmsPickTaskQuery`/`IWmsWorkloadQuery`/`IWmsDeviceQuery`。
- 删 `StubWmsStockQuery`（或保留供测试，见 §8）。

---

## §2 数据模型映射（WMS 真表 → 契约 DTO）

| 概念 | WMS 真表/字段 | 说明 |
|---|---|---|
| join key | `Stock.LocationCd` ↔ `Space_Location.LocationCode` ↔ `Location.LocationCd` | string(≤30)，精确匹配；只叠 `Placed=true` 库位 |
| 库存量 | `Stock.PhysicalQty`（每库位多行，按 `ProductCd`+`LotNo`）→ **Σ 聚合到库位** | decimal(21,8) |
| 引当/可用 | `Stock.AllocatedQty` / `Stock.AvailableQty` | 信息卡可显 |
| 库容 | `Location.CapacityQty`（0=无限） | 利用率分母（WMS 为准） |
| 冻结锁 | `Location.IsBlocked` | → BinStatus 锁定 |
| 可拣 | `Location.IsPickable` | 信息卡可显，v1 不入 BinStatus |
| QC | `Stock.QcStatus`（PENDING/PASSED/FAILED/HOLD） | 信息卡可显；v1 不入 BinStatus（预留） |
| 在拣 | `OutboundOrderDetail.LocationCd` + `AllocatedQty>ShippedQty`，头 `OutboundOrder.Status=Picking(3)` | → BinStatus 在拣（§4.2） |
| 物料/批/容器 | `Stock.ProductCd`/`Stock.LotNo` / `Pallet.PalletNo`+`Pallet.LocationCd` | FindLocationsAsync 反查（§4.4） |
| 作业频次 | `StockTransaction`（`LocationCd`+`TxnDateTime`+`TxnType`）按窗计次 | 08 作业热图（§5.3） |
| 拣货序列 | `OutboundOrder`(taskNo=`OutboundNo`) + `OutboundOrderDetail`(LineNo 序, LocationCd, RequiredQty, ProductCd) | 08 拣货路径（§5.2） |

> **库容 UOM 注意**：`Stock.PhysicalQty` 单位=製品 UOM，`Location.CapacityQty` 注释"matches product UOM"，但单库位混多製品多 UOM 时利用率口径模糊。**v1 决议**：直接 `ΣPhysicalQty / CapacityQty`（接受模糊，信息卡注明）；`CapacityQty=0`（无限/未设）→ 回退 BinStatus 粗估（空0/有货0.5/满1，丛书 §5.1）。

---

## §3 07 实时库存叠加 — 详规

### 3.1 契约扩展（`IWmsStockQuery`）

```csharp
// CP6.Core/Services/Integration/IWmsStockQuery.cs —— 扩既有文件（消费者 Space 侧定义）
public interface IWmsStockQuery
{
    // ① 批量按库位编码查库存（叠加主力）
    Task<IReadOnlyList<WmsStockDto>> GetStockByLocationsAsync(
        IReadOnlyCollection<string> locationCodes, CancellationToken ct = default);

    // ② 按物料/批次/容器反查"哪些库位有它"（D8 P2 半，§3.4）
    Task<IReadOnlyList<WmsLocationHit>> FindLocationsAsync(
        StockLocateQuery query, CancellationToken ct = default);

    // ③ 兼容 04：单库位库存量（= ① 的单元素特例的便捷封装；保留签名以零改 04 调用方）
    Task<decimal> GetStockQtyAsync(string locationCode, CancellationToken ct = default);
}

public sealed class WmsStockDto
{
    public string  LocationCode { get; set; } = "";   // join key
    public int     BinStatus    { get; set; }         // 0空 1有货 2满 3锁定 4在拣（§3.2 派生）
    public decimal Qty          { get; set; }         // ΣPhysicalQty
    public decimal AllocatedQty { get; set; }         // ΣAllocatedQty（信息卡）
    public decimal? Capacity    { get; set; }         // Location.CapacityQty（0/未设→null）
    public string?  TopMaterial { get; set; }         // 占量最大 ProductCd（信息卡/预留）
    public int      ProductKinds{ get; set; }         // distinct ProductCd 数（信息卡）
    // 预留：温区/锁定原因/批次数 —— v1 不消费（YAGNI）
}

public sealed class StockLocateQuery
{
    public string? MaterialNo { get; set; }   // ProductCd
    public string? Lot        { get; set; }   // LotNo
    public string? Container  { get; set; }   // PalletNo
    // 三者按"非空即条件" AND 组合；全空 → 空结果
}

public sealed class WmsLocationHit
{
    public string  LocationCode { get; set; } = "";
    public decimal Qty          { get; set; }   // 该库位该物料量
    public string? Lot          { get; set; }
}
```

- **批量硬要求**：禁逐个查（万级 N 次 RPC 必崩）。批大小上限（≤1000 编码）由实现侧分批；v1 一层规模（数百~数千）一次足够。
- **去 tenantId**（§1.2）：全局过滤自动隔离。
- **04 兼容**：`GetStockQtyAsync` 保留（内部走 `GetStockByLocationsAsync([code])` 取 `Qty`），04 `LocationPublishService.DeactivateAsync` 调用零改；行为由"恒 0 桩"变"真查"（§6 回归）。

### 3.2 `WmsStockQuery` 接真实现 — BinStatus 5 态派生

WMS 无现成 BinStatus 字段，实现侧派生。**优先级（高→低，先命中即取）：锁定 > 在拣 > 满 > 有货 > 空**。

```
对一批 codes：
  S = Stock.Where(LocationCd in codes) 按 LocationCd 分组 → ΣPhysicalQty, ΣAllocatedQty, distinct ProductCd, TopMaterial
  L = Location.Where(LocationCd in codes) → CapacityQty, IsBlocked
  P = OutboundOrderDetail.Where(LocationCd in codes && AllocatedQty>ShippedQty)
        join OutboundOrder on OutboundNo where OutboundOrder.Status == OutboundOrderStatus.Picking(3)
        → 在拣库位集合 pickingSet

  对每个 code：
    qty = S[code].ΣPhysicalQty ?? 0
    cap = L[code].CapacityQty   (0 视为未设 → null)
    if L[code].IsBlocked           → BinStatus = 3 锁定
    elif code in pickingSet        → BinStatus = 4 在拣
    elif cap>0 && qty>=cap         → BinStatus = 2 满
    elif qty>0                     → BinStatus = 1 有货
    else                           → BinStatus = 0 空
```

- `OutboundOrderStatus`：Draft0/Confirmed1/Allocated2/**Picking3**/Completed4/PartialAllocated5/Cancelled9。在拣**只取 Picking(3)**（"拣货作业中"语义最准；Allocated(2) 仅引当未拣，v1 不计为在拣）。
- 锁定优先于在拣：冻结库位即使有拣货明细也显锁定（物理不可动为先）。
- WMS 未返回某 code（无 Stock 行且无 Location 行，如刚发布未建 bin）→ 该库位不在结果集 → 前端标"无数据"中性色（丛书 §4.3）。

### 3.3 库容利用率（前端聚合）

- 库位利用率 = `Qty/Capacity`（Capacity 有值）；无值按 BinStatus 粗估（空0/有货0.5/满1）。
- 货架/库区利用率 = 前端用快照 `Σ库位Qty / Σ库位Capacity` 聚合（不额外请求 WMS）。
- 热力模式把 `[0,1]` 映射冷→暖（蓝→黄→红）；与 05 LOD 联动（远聚合色块、近单格）。

### 3.4 按物料/批/容器定位（D8 P2 半）

`FindLocationsAsync(query)` 实现：
- `MaterialNo` → `Stock.Where(ProductCd==材料 && PhysicalQty>0)`；`Lot` → 加 `LotNo` 条件；`Container` → `Pallet.Where(PalletNo==容器)` 取 `LocationCd`。
- 多条件 AND；结果按 `LocationCd` distinct + 各库位量。
- 前端流程：输入 → `/stock/locate` → 命中列表 → **复用 06 `flyToData`/切层/高亮**（不重造定位）。空命中 → I-SPACE-701；多命中 → I-SPACE-702（跨层分组、巡航）；命中过多 → 限高亮 K 个 + 分页 + W-SPACE-702。

### 3.5 Space 后端中转端点（07）

| 端点 | 实现 |
|---|---|
| `GET /api/space/floor/{floorId}/stock` | 服务端枚举该层 `Placed=true` 库位编码（`Space_Location`）→ `IWmsStockQuery.GetStockByLocationsAsync` → 返回 `WmsStockDto[]` + 服务器快照时间戳 |
| `GET /api/space/stock/locate?material=&lot=&container=` | `IWmsStockQuery.FindLocationsAsync` → 命中列表（含 floorId，便于跨层定位） |

> **可见区裁剪 v1 = 楼层级**（服务端按当前楼层 Placed 编码批量）。丛书理想的"视锥精确裁剪"留增量（需 ViewerHandle 暴露 `getVisibleLocationCodes()`，P-later）。

### 3.6 前端 overlay（`space-viewer/overlay`）

- `StockOverlay`：拉快照 → 缓存（带时间戳）→ 按当前模式着色（调 `ViewerHandle.setInstanceColor`，批量后单次 `requestRender`）。
- **着色三模式**：状态色（默认，§3.7 色板）/ 利用率热力（§3.3）/ 关叠加（回 05 默认灰）。图例随模式;切模式用缓存重着色不重拉。
- **刷新（D5）**：按需快照为主（进页/切层建图完成、点"刷新库存"、相机停稳可选）；**可选轮询默认关**（间隔下限如 5s、相机移动暂停、离页停）；**无推送**（不接 SignalR）。
- 与 **Highlighter 协同**（reconcile 要点）：状态色是"底色"，Highlighter `_readColor` 存/复原能正确回到状态色；**"hover/选中期间快照刷新"小竞态** → 刷新前先 `Highlighter.clear()` 或刷新后重应用高亮（plan 落细）。
- 信息卡（接 06 §4.2）叠库存行：状态/量/容量/利用率/主物料 + **数据时间戳**。
- **降级**：`IWmsStockQuery` 超时/异常 → 留上次快照 + 标 W-SPACE-701，**绝不拖垮 05/06 结构浏览**。

### 3.7 状态色板（v1 固定，字段预留）

| BinStatus | 含义 | 默认色 |
|---|---|---|
| 0 空 | 无货 | 绿 |
| 1 有货 | 部分占用 | 蓝 |
| 2 满 | 占满 | 红 |
| 3 锁定 | 冻结/盘点锁 | 灰 |
| 4 在拣 | 拣货作业中 | 黄 |
| — | 无数据 | 中性灰（区别于锁定灰） |

> 色板写死前端常量；留 `colorScheme` 配置位，v1 不做配置 UI（YAGNI）。

---

## §4 08 高级可视化 — 详规

### 4.1 契约（08 三个）

```csharp
// CP6.Core/Services/Integration/ —— 与 IWmsStockQuery 同族，纯读单向，去 tenantId
public interface IWmsPickTaskQuery {
    Task<PickPathDto> GetPickPathAsync(string taskNo, CancellationToken ct = default);
}
public sealed class PickPathDto {
    public string TaskNo { get; set; } = "";
    public IReadOnlyList<PickStop> Items { get; set; } = [];  // 有序拣货点
}
public sealed class PickStop {
    public int Seq { get; set; } public string LocationCode { get; set; } = "";
    public decimal Qty { get; set; } public string? MaterialNo { get; set; }
}

public interface IWmsWorkloadQuery {
    Task<IReadOnlyList<WorkloadDto>> GetWorkloadAsync(
        Guid floorId, DateTime from, DateTime to, CancellationToken ct = default);
}
public sealed class WorkloadDto { public string LocationCode { get; set; } = ""; public int OpCount { get; set; } }

public interface IWmsDeviceQuery {   // v1 占位
    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(Guid floorId, CancellationToken ct = default);
}
public sealed class DeviceDto {
    public string DeviceId { get; set; } = ""; public string Type { get; set; } = "";
    public string? LocationCode { get; set; } public int Status { get; set; }
}
```

### 4.2 拣货路径动画（做实）

- **数据源接真**：`GetPickPathAsync(taskNo)` 实现 = `OutboundOrder.Where(OutboundNo==taskNo)` + `OutboundOrderDetail.Where(OutboundNo==taskNo && LocationCd!=null)` 按 `LineNo` 序 → `PickStop[]`（Seq=LineNo, Qty=RequiredQty, MaterialNo=ProductCd）。（无专门 PickTask/Wave 实体；波次拣货在另一未合并分支，未来可替换源，契约不变。）
- **路径规划（前端）**：解析每 code 的 AbsXYZ（Space 自有）→ 用 `Space_Aisle.Centerline` 建当前层中心线图（节点=端点/交叉/库位接入垂足，边权=段长）→ 相邻拣货点 Dijkstra/A* 最短路 → 折线；图楼层级缓存。
- **动画**：小车/光点沿路径跑（播放/暂停/步进/调速/重播）+ 到达脉冲 + 顺序号牌 +（接 07）显拣量；相机可选跟随（复用 06 补间）。
- **降级**：中心线缺失/不连通 → 该段退化直连 + W-SPACE-801，不中断；I-SPACE-801 报"N 点/总距 D 米"。
- **按需渲染**：仅动画播放时持续 `requestRender`（05 §9.1），暂停即停。

### 4.3 作业热图（做实）

- **数据源接真**：`GetWorkloadAsync(floorId, from, to)` 实现 = `StockTransaction.Where(TxnDateTime in [from,to] && LocationCd in 该层Placed编码)` 按 `LocationCd` 计数 → `WorkloadDto[]`（OpCount）。可选只计 OUT/PICK 类（口径由 plan 定，默认全 TxnType 计次）。
- **着色**：归一化 OpCount → 冷→暖，**复用 07 着色管线**（在 §3.6 着色模式里加"作业热图"模式，共用 `setInstanceColor`+快照缓存+裁剪+LOD 聚合）。时间窗（今日/本周/自定义）；I-SPACE-802 报已加载。
- 与 07 利用率热力区别：07="哪里满了"（库存快照）/ 08="哪里最忙"（作业累计），同管线不同数据源+色映射。

### 4.4 设备联动（v1 占位）

- `IWmsDeviceQuery` → `WmsDeviceQuery` 桩返空（或示例数据）。
- 前端 `advanced/DeviceLayer`：SceneRoot 下预留 `DeviceLayer` 挂点 + 静态示意图元（参数化盒体，零素材）+ I-SPACE-803"演示示意，未接实时"。
- 真接 AGV/WCS 实时流 = P3+（架构留可注入数据点，未来换源不返工）。

### 4.5 Space 后端中转端点（08）

| 端点 | 实现 |
|---|---|
| `GET /api/space/floor/{floorId}/pick-path?taskNo=` | `IWmsPickTaskQuery` + 解析 AbsXYZ → 前端规划动画 |
| `GET /api/space/floor/{floorId}/workload?from=&to=` | `IWmsWorkloadQuery` |
| `GET /api/space/floor/{floorId}/devices` | `IWmsDeviceQuery`（v1 空/示例） |

---

## §5 04 停用校验回归（stub→真查）

`LocationPublishService.DeactivateAsync` 经 `IWmsStockQuery.GetStockQtyAsync` 做"停用前置 0 库存"校验。当前桩恒返 0 → 任何库位都可停用。接真后：
- 行为变正确（有库存的库位停用被拦，E-SPACE-401 已有）。
- **既有 04 测试若依赖"恒 0"假设**（停用任意库位成功）→ 需补种库存使其真实，或在测试用 `StubWmsStockQuery`/伪实现注入（见 §7）。plan 必跑 `--filter ...Space` 全回归确认零破坏。

---

## §6 多租户 / 权限

- 所有查询经全局过滤按 ambient `TenantContext` 隔离（§1.2）；中转端点在 Space 后端，沿用既有鉴权。
- 库存查看/高级可视化接 PUB 功能权限；可见库位接数据权限（无权库位不查/不显）——v1 至少接功能权限菜单，数据权限按既有 Space 端点惯例。

---

## §7 测试与 QA

- **单测（xUnit, InMemory）**：
  - `WmsStockQuery`：BinStatus 5 态派生（每态一例 + 优先级 锁定>在拣>满>有货>空）、批量聚合、FindLocations（料/批/容器/空命中/多命中）、04 兼容 `GetStockQtyAsync`。
  - `WmsPickTaskQuery`：按 OutboundNo 取序列、LineNo 序、空。
  - `WmsWorkloadQuery`：时间窗计次、按库位聚合。
  - 多租户：跨租户不串（全局过滤）。
- **测试替身**：保留 `StubWmsStockQuery`（或新增伪实现）供 04/上层测试注入，避免接真表耦合。
- **前端（vitest 纯逻辑）**：BinStatus→色映射、利用率聚合、中心线图构建 + 最短路、热图归一化、bindMatch 式纯函数。
- **gstack 真浏览器 QA（接真种子，收尾）**：灌演示库存（Stock 多态：空/有货/满/锁定 IsBlocked/在拣 OutboundOrder Picking）+ 出库单（拣货路径）+ StockTransaction（热图）→ 验：状态着色/利用率热力/物料定位飞行/拣货路径动画/作业热图/降级（停 WMS 数据看是否留快照不崩）。

> Konva/WebGL 真交互（动画手感/相机跟随/热图视觉）属运行态，gstack 截图视觉确认 + 纯逻辑单测兜底（同 P1 手段）。

---

## §8 交付物拆分（已定）

- **本 reconcile spec**（双格式 .md+.docx），单独 commit。
- **Plan 07**（数据底座，先）：契约扩展 + `WmsStockQuery`/`FindLocations` 接真 + 04 回归 + DI 替换 + Space `/stock`、`/stock/locate` 端点 + 前端 `overlay`（三模式着色/快照缓存/轮询/楼层裁剪/图例/信息卡叠库存/物料定位复用 06）+ 演示种子 + 单测 + gstack。
- **Plan 08**（复用 07，后）：`IWmsPickTaskQuery`/`IWmsWorkloadQuery`/`IWmsDeviceQuery` 契约 + 接真实现（出库单/流水）+ 设备桩 + Space `/pick-path`、`/workload`、`/devices` 端点 + 前端 `advanced`（中心线图+最短路 PickPathPlanner / PathAnimator / WorkloadHeatmap 复用 07 管线 / DeviceLayer 占位）+ 单测 + gstack。
- 两份 plan 均走 **subagent-driven TDD**，末尾 gstack 接真种子 QA；每 Task 本地 commit（push 由用户自跑）。

---

## §9 错误码 / 消息一览

| ID | 种别 | 内容 | 章 |
|---|---|---|---|
| W-SPACE-701 | Warn | 库存数据获取失败，显示上次快照（可能陈旧） | 07 降级 |
| I-SPACE-701 | Info | 无库位存放该物料/批次/容器 | 07 物料定位空命中 |
| I-SPACE-702 | Info | 找到 N 个库位（M 层），点击定位 | 07 多命中 |
| I-SPACE-703 | Info | 库存已刷新（数据时间 hh:mm:ss） | 07 快照完成 |
| W-SPACE-702 | Warn | 命中库位过多，仅高亮前 K 个 | 07 大命中集 |
| W-SPACE-801 | Warn | 巷道路径不连通，近似直连显示 | 08 路径降级 |
| I-SPACE-801 | Info | 拣货路径：N 个拣货点，总距离 D 米 | 08 路径加载 |
| W-SPACE-802 | Warn | 高级可视化数据获取失败 | 08 降级 |
| I-SPACE-802 | Info | 作业热图（时间窗 from~to）已加载 | 08 热图 |
| I-SPACE-803 | Info | 设备联动为演示示意（未接实时） | 08 设备占位 |

> 后端裸码（`E-SPACE-4xx`/中文消息后缀沿用既有风格），前端按需 i18n；`W-/I-SPACE-7xx/8xx` 多为前端展示信息。

---

## §10 未决 / 推迟（YAGNI）

- 视锥精确可见区裁剪（v1 楼层级足够）。
- 租户级配色 UI（字段预留，v1 固定色板）。
- 库存实时推送 SignalR（守 D5 无推送）。
- 真实设备实时联动（08 仅占位）。
- 波次拣货作为拣货路径源（待 `feat/wms-wave-picking` 合并；契约不变可替换源）。
- 库容混 UOM 精确口径（v1 接受 ΣQty/Capacity 模糊 + 信息卡注明）。
- QcStatus / IsPickable 进 BinStatus（v1 信息卡显，不入着色态）。

---

## 自检

- [ ] 契约族为何全部单向纯读、定义在 Space 侧、去 tenantId？04 为何是 `IWmsStockQuery` 首消费者，接真后行为怎么变、怎么回归？
- [ ] BinStatus 5 态各自的真表来源是什么？优先级为何是 锁定>在拣>满>有货>空？"在拣"为何只取 `OutboundOrder.Status=Picking(3)` 且 `AllocatedQty>ShippedQty`？
- [ ] 07 刷新 D5 三红线（无推送/按需快照/轮询下限+楼层裁剪）各解决什么？状态色作底色与 Highlighter 复原的竞态怎么处理？
- [ ] 库容利用率怎么算/聚合？混 UOM 怎么降级？无库容怎么粗估？
- [ ] 08 拣货路径为何必须走 Aisle 中心线？源为何用 OutboundOrder（而非 PickTask）？不连通怎么降级？
- [ ] 08 作业热图怎么从 StockTransaction 派生、为何能复用 07 着色管线？设备为何只占位？
- [ ] 任一 WMS 数据源失败，07/08 为何不能拖垮 05/06 结构浏览？

---

*实现：扩 `CP6.Core/Services/Integration/IWmsStockQuery.cs` + 新增 08 三契约 + WMS 侧 `WmsStockQuery`/`WmsPickTaskQuery`/`WmsWorkloadQuery`/`WmsDeviceQuery`（接真/占位）+ Space 后端 `Controllers/Space/` 中转端点 + 前端 `space-viewer/overlay`（07）/`space-viewer/advanced`（08）+ 演示种子 + 单测 + gstack QA。设计真相见丛书 `docs/space/07-stock-overlay.md`、`08-advanced-viz.md`；本文为 as-built 落地对账。*
