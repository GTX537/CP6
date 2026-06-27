# Space 06 · 相机 / 拾取 / 导航 / 定位 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

> **v1.1 评审补丁（2026-06-27 深审应用）**：钉死 06↔05 依赖句柄（拾取粗筛用 05 新补 `getBucketBoundingBox`、hover 复原用 05 新补 `locationToInstance`，§3.1/§4.1/§10）；明确坐标互转约定（`worldToData` 入参 Three 世界米、返回数据 mm，**不再手工 ×1000**，对齐 05 v1.1 §3.2.1，§3.3）；补拾取粗筛伪码闭环（候选 Zone Box3 求交砍桶 → 桶内 raycaster.instanceId 精拾，§3.1）；跨层定位改 async/await（`await viewer.load(floorId)` 等建图完成再 flyTo，对接 05 §8.2 / onReady，§6.1）；补 flyTo 机位算法 + 高亮闪烁参数（§6.1/§7）；hover 与 07 状态色协调协议（存原色、复原回原色而非默认灰，§4.1）；定位查询自动带租户（继承 BaseBizEntity 全局过滤，无需手写 .Where，§6/§8）；YAGNI 标注 P2 项（GPU 拾取 B 方案 / 等轴正视预设 / 多层堆叠概览，§3.1/§2.3/§5.2）。相关处标「(v1.1评审补丁)」。

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-06 相机 / 拾取 / 导航 / 定位 |
| 所属模块 | Space 空间数字底座 · Part 1（P1） |
| 里程碑 | **P1 收官**（写完 P1 闭环达成：建仓→生成→编码→3D 浏览→**按编码定位**→发布 WMS） |
| 技术栈 | Vue3 + TypeScript + Three.js（Raycaster / GPU 拾取 / 相机补间）；建在 [05](./05-viewer-core.md) `ViewerHandle` 上 |
| 命名空间 | `cp6.web/src/space-viewer/navigate`（相机/拾取/定位逻辑）/ `cp6.web/src/views/space/viewer`（搜索框/信息卡/楼层切换 UI） |
| 落地决策 | D1 3D 只读（拾取只读不改几何）/ D8 物料定位**P1 半=按库位编码定位**（按物料/批次 P2 在 07）/ 包围盒粗筛 + GPU/射线精拾 |
| 依赖 | [05 渲染内核](./05-viewer-core.md)（SceneRoot、`getBucketBoundingBox`〔粗筛〕、`instanceToLocation`〔正向〕、`locationToInstance`〔反向，hover 复原〕、`worldToData`/`dataToWorld`、`setInstanceColor`、`load→Promise`/`onReady`、`requestRender`、分桶包围盒）、[00](./00-data-model.md)（AbsXYZ、FloorId、库位编码 join key） |

> **题眼**：05 把场景画出来，06 让用户**在场景里走动、点中、找到东西**。三件事定体验：① **相机**——轨道控制（旋转/平移/缩放）+ 视角预设 + 平滑补间，让浏览顺手；② **拾取**——鼠标点到的像素 → **库位编码**，关键链路是 `射线/GPU 命中 → instanceId →（05 双向表）→ LocationId → LocationCode`，配 05 分桶做"包围盒粗筛 + 桶内精拾"扛万级；③ **定位**（D8 的 P1 半）——输入一个**库位编码**，相机自动飞过去并高亮（不在当前层就先切层）。**记住一句**：06 全程**只读**（点中、高亮、飞行，绝不改几何/编码）；按**物料/批次/容器**找货（D8 的 P2 半）要 WMS 库存数据，归 [07](./07-stock-overlay.md)——本章只做"按编码/坐标"的纯空间定位。

---

## 目录
- 第1章 功能概述与定位（与 05/07 的边界）
- 第2章 相机控制（轨道 / 投影 / 视角预设 / 补间）
- 第3章 拾取链路（包围盒粗筛 + 射线/GPU 精拾 → 库位编码）
- 第4章 hover 高亮与 click 选中（信息卡）
- 第5章 楼层切换与多层导航
- 第6章 按库位编码定位（D8 的 P1 半）
- 第7章 视角辅助（聚焦选中 / 复位 / 框选概览）
- 第8章 API 接口（定位查询，增量）
- 第9章 消息一览
- 第10章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：在 05 渲染内核之上，提供完整的 3D 浏览交互——相机操控、对象拾取（→库位编码）、楼层切换、按库位编码飞行定位，让仓库 3D 从"能看"变成"好用"。

**本章范围（06）：**
- 相机：`OrbitControls` 轨道控制、透视/正交切换、视角预设（俯视/等轴/正视）、平滑补间。
- 拾取：包围盒粗筛 + 桶内射线/GPU 精拾，命中 → `LocationId/LocationCode`；hover 高亮、click 选中。
- 选中信息卡：显示库位编码 + 变长层级路径（区/巷/架/层/列）。
- 楼层切换：楼层列表、切层（调 05 `load/dispose`）、跨层状态保持。
- **按库位编码定位**：搜索框输入编码 → 查位置 → 切层（如需）→ 相机飞行 + 高亮（D8 P1）。
- 视角辅助：聚焦选中、视角复位、整层概览。

**不含（划清边界）：**
| 能力 | 去哪章 |
|---|---|
| 场景渲染 / 实例化 / 剔除 / 标签机制 | [05 章](./05-viewer-core.md)（06 只用其挂点） |
| **按物料/批次/容器定位、库存状态着色、热力** | [07 章](./07-stock-overlay.md)（D8 P2 半，需 WMS 数据） |
| 几何编辑 / 拖拽（3D 只读，不编辑） | [01](./01-editor-template.md)/[02](./02-free-layout.md)（Konva 2D） |
| 拣货路径动画 | [08 章](./08-advanced-viz.md) |

> **06 与 07 的定位分工（D8 切两半）**：06 = **按空间标识找**（库位编码、坐标）——纯几何，P1 即可（不依赖 WMS）；07 = **按业务标识找**（物料号、批次、容器）——要查 WMS 库存才知道"哪个库位有这个料"，P2 才有数据。两者共用 06 的"飞行+高亮"基础设施，07 只是把"输入编码"换成"输入物料→经 WMS 反查出一批库位编码→复用 06 定位"。

---

## 第2章 相机控制

### 2.1 轨道控制（OrbitControls）
- 基于 Three.js `OrbitControls`：左键旋转、右键/中键平移、滚轮缩放；带**阻尼**（damping）让操作顺滑。
- 约束：俯仰角限制（不翻到楼层下方）、缩放距离上下限（防穿模/飞太远）、平移边界（限制在楼层包围盒附近）。
- 目标点（target）= 当前楼层包围盒中心或选中对象，旋转绕它。

### 2.2 投影：透视 / 正交
| 模式 | 用途 | 默认 |
|---|---|---|
| 透视 `PerspectiveCamera` | 沉浸式 3D 浏览（有近大远小） | ✓ 默认 |
| 正交 `OrthographicCamera` | 俯视/正视看布局（无透视畸变，便于对位） | 切换 |
- 透视↔正交切换时保持视线方向与大致取景，补间过渡（避免跳变）。

### 2.3 视角预设
| 预设 | 视角 | 阶段（v1.1评审补丁） |
|---|---|---|
| 俯视 Top | 正上方俯瞰整层（接近 2D 平面图） | **P1** |
| 复位 Home | 回整层概览默认机位 | **P1** |
| 等轴 Iso | 45° 斜俯（最常用浏览角） | P2 |
| 正视 Front/Side | 沿 X/Y 轴正看（看货架立面分层） | P2 |
- 点预设 = 相机**补间飞行**到目标机位（§2.4），不瞬移。
- **YAGNI（v1.1评审补丁）**：P1 视角预设只做**俯视 Top + 复位 Home**（够 demo 与定位飞行）；**等轴 Iso / 正视 Front/Side 预设留 P2**。注意"等轴方向"仍用于定位飞行机位算法（§6.1，那是计算 camPos 的方向向量，不是预设按钮）——预设按钮与机位算法是两回事。

### 2.4 相机补间（平滑飞行）
```ts
// 相机位置 + target 同时 lerp/缓动到目标，期间持续 requestRender（05）
flyTo(camPos, target, duration=600ms, easing=easeInOutCubic):
  每帧插值 camera.position 与 controls.target → controls.update() → viewer.requestRender()
  到达后回调（定位高亮等，第6章）
```
- 飞行是定位（第6章）、视角预设、聚焦选中的共用基础设施。
- 飞行中暂停轨道输入，结束恢复；可被新飞行/用户操作打断。

---

## 第3章 拾取链路（→ 库位编码）

> 拾取的终点是**库位编码**（join key）。链路：屏幕坐标 → 命中实例 → `instanceId` →（05 §5.2 双向表）→ `LocationId` → `LocationCode`。万级实例靠"包围盒粗筛 + 桶内精拾"。

### 3.1 两级拾取

> **🔴依赖句柄（v1.1评审补丁）**：粗筛用 05 新补的 `getBucketBoundingBox(zoneId): THREE.Box3`（05 §6.1/§10 已暴露各 Zone 桶世界包围盒）；精拾命中 `instanceId` 经 05 `instanceToLocation(meshId,instanceId)` 反查 `LocationId`。06 自身不维护桶包围盒/映射表，全部取自 05 句柄。

```ts
// 拾取粗筛 + 精拾闭环（A 方案默认，v1.1评审补丁）
pick(mouseNdc): PickResult | null {
  raycaster.setFromCamera(mouseNdc, camera)
  // ① 包围盒粗筛（桶级，复用 05 分桶 + getBucketBoundingBox）：
  const candidates = []
  for (const zoneId of viewer.getVisibleZoneIds()) {       // 仅取可见桶（视锥剔除后）
    const box: THREE.Box3 = viewer.getBucketBoundingBox(zoneId)   // 05 §6.1/§10
    if (raycaster.ray.intersectsBox(box)) candidates.push(zoneId) // 射线/视锥求交砍候选桶
  }
  if (candidates.length === 0) return null                  // 远处桶/视锥外桶已被排除
  // 候选桶按沿射线距离近→远排序，命中即止（近桶遮挡远桶）
  candidates.sort(byRayDistance(raycaster.ray))
  // ② 桶内精拾（A 射线精拾，默认）：
  for (const zoneId of candidates) {
    const mesh = viewer.getBucketMesh(zoneId)               // 候选桶 InstancedMesh
    const hit = raycaster.intersectObject(mesh, false)[0]   // → intersection.instanceId
    if (hit?.instanceId != null) {
      const loc = viewer.instanceToLocation(mesh.id, hit.instanceId) // 05 §5.2 正向表
      return buildPickResult(loc, hit.point)               // → LocationId → 查 LocationCode
    }
  }
  return null
}
```
- **句柄出处说明（v1.1评审补丁）**：上伪码 `getBucketBoundingBox` / `instanceToLocation` 是 05 显式句柄（§10）；`getVisibleZoneIds()`（可见桶集）与候选桶 `mesh`（按 zoneId 取 InstancedMesh）由 06 经 05 `getSceneRoot()` 遍历 `LocationGroup` + 复用 05 §6.1 视锥剔除可见结果得到，**不新增 05 句柄**。
- **粗筛的价值**：万级实例不全量射线测试，先用桶 Box3 砍到候选桶（O(桶数)），再只对候选桶精拾——拾取恒定快。
- **A vs B 选择**：默认 **A 射线精拾**（实现简单，候选桶实例数已被粗筛压到百~千级，足够快）——**P1 只做 A**。
- **🟢B 方案 = P2（YAGNI，v1.1评审补丁）**：GPU 拾取（把候选桶实例编码成唯一颜色渲到离屏 RT、读鼠标像素解码 instanceId，O(1) 像素读取精确到像素）留 **P2**，仅当超大/高密场景或射线在密集实例下误差大时再启用，P1 不实现、不留入口。

### 3.2 拾取对象优先级
- 优先命中**库位格**（最常交互）；库位被遮挡/远 LOD 隐藏时退而命中**货架**；再退命中**库区面/打点**。
- LOD 远档库位桶隐藏（05 §6.2）时，拾取自然落到货架级——与可视一致（看不见的拾不到）。

### 3.3 拾取结果对象
```ts
interface PickResult {
  kind: 'location'|'rack'|'zone'|'marker'
  locationId?: string; locationCode?: string         // kind=location
  rackId?: string; rackCode?: string
  zoneId?: string
  worldPoint: Vector3                                 // Three 世界命中点
  dataPoint: {x,y,z}                                  // 经 05 worldToData 反算回 mm 数据坐标
}
```
- `worldToData`（05 §3.3）把命中点反算回数据 mm 坐标——用于显示坐标、或与 AbsXYZ 校验。

> **🔴坐标互转约定（v1.1评审补丁，对齐 05 v1.1 §3.2.1）**：06 全程**只调 05 两个句柄做互转，自己绝不手工换轴/缩放**——
> - `worldToData(v3)`：**入参是 Three 世界坐标 `Vector3`（米，Y-up）**，**返回数据 mm `{x,y,z}`（Z-up，整数）**。返回值**已是 mm，不再手工 ×1000、不再换轴**（05 §3.2.1 已把缩放+轴重映射收口在 `sceneRoot.worldToLocal` 内，外面再 ×1000 就是重复变换 → 坐标放大千倍的经典 bug）。
> - `dataToWorld({x,y,z}mm)`：**入参数据 mm**，**返回 Three 世界 `Vector3`（米）**，供定位飞行/高亮（§6.1）直接喂相机 target。
> - 一句话：**米 ↔ mm 的换算只发生在 05 句柄内部一次**；06 拿到 `worldToData` 的结果即 mm、拿到 `dataToWorld` 的结果即米，两端都不再做任何二次缩放。

---

## 第4章 hover 高亮与 click 选中

### 4.1 hover（悬停高亮）
- 鼠标移动（节流，如每 50ms 或鼠标停顿）做一次拾取；命中库位 → **临时高亮**该实例（改 instanceColor 为高亮色，05 `setInstanceColor`；移出复原）。
- hover 不开信息卡，仅高亮 + 浮动 tooltip（显库位编码），轻量。

> **🟡hover 与 07 状态色协调协议（v1.1评审补丁）**：高亮**复原必须回原色，而非默认灰**——因为 07 接入后该库位的 instanceColor 可能是**库存状态色**（空/满/锁定），复原回默认灰会冲掉 07 的着色。协议：
> ```ts
> onHoverEnter(locationId):
>   // 进入：先存原色再改高亮色
>   const orig = viewer.getInstanceColor?.(locationId)        // 优先取 05 句柄当前色（可能是 07 库存色）
>             ?? hoverColorCache.get(locationId)              // 05 若未暴露 getInstanceColor，则查本地缓存
>             ?? DEFAULT_GREY                                  // 都没有才退默认灰
>   hoverColorCache.set(locationId, orig)                     // 记下原色
>   viewer.setInstanceColor(locationId, HOVER_COLOR)
> onHoverLeave(locationId):
>   // 离开：恢复成进入前记下的原色（库存色/默认色），不是默认灰
>   viewer.setInstanceColor(locationId, hoverColorCache.get(locationId) ?? DEFAULT_GREY)
> ```
> - 优先用 05 `getInstanceColor(locationId)` 读真实当前色；**05 v1.1 §10 暂未暴露 getInstanceColor 时，06 自维护 `hoverColorCache` 本地缓存**（在写高亮色前记下原色），unhover 用缓存复原。
> - 与 click 选中态、定位高亮闪烁（§6.1）共用同一"存原色/复原"基础设施，避免互相冲掉颜色。

### 4.2 click（选中）
- 单击命中 → 设为**选中态**：高亮（描边/亮色，区别于 hover）+ 弹**信息卡**。
- 选中信息卡内容：
```
库位编码：A-03-02-05
层级路径：站点WH1 / 1层 / 库区A /（无巷道）/ 货架R03 / 第2列 第2层 第1深
状态：已发布（Status=1）   来源：引擎生成（CodeOrigin=1）
坐标：(absX, absY, absZ) mm
[ 07 接入后此处叠：库存量 / 库位状态（空/满/锁定）]
```
- 路径来自 06 拾取出的 LocationId → 查库位详情（含层级链，变长，无巷道则跳过巷道行）。
- 选中态唯一（再点别处换选，点空白清选）；选中驱动"聚焦选中"（第7章）。

### 4.3 只读保证
- hover/click/选中**全程只读**：只改 instanceColor（视觉高亮）、显信息卡，**绝不改几何/编码/Status**（D1：3D 只读）。高亮色是渲染态，不入任何持久化。

---

## 第5章 楼层切换与多层导航

### 5.1 楼层列表与切换
- 浏览页侧栏列当前 Site 的楼层（Floor.Level 排序）；点某层 → `viewer.dispose()` 旧层 → `viewer.load(floorId)` 新层（05 §8.3）。
- 切层是**重建场景**（每 Floor 局部坐标系独立，00 §2），相机复位到新层概览机位。
- 切层中显加载进度（05 `onProgress`）；切换防抖（连点只切最后一个）。

### 5.2 跨层状态保持
- 选中态/搜索词跨层处理：切层默认清选中（对象属上一层）；若定位（第6章）目标在别层，**先切层再飞行**（第6.2）。
- **🟢多层堆叠概览 = P2（YAGNI，v1.1评审补丁）**：楼层作为堆叠层片俯视预览、点层片进入该层——**留 P2**。**P1 只做单层**（侧栏楼层列表 + 切层重建场景，§5.1），跨层定位靠"先切层再飞行"（§6.2）即可闭环，无需堆叠预览。

---

## 第6章 按库位编码定位（D8 的 P1 半）

> D8 决策：物料定位搜索，**按库位编码 = P1**（纯空间，不依赖 WMS）、按物料/批次 = P2（07）。本章实现 P1 半。

### 6.1 定位流程（async/await，v1.1评审补丁）

> **🟡跨层定位异步（v1.1评审补丁）**：切层是**异步重建场景**（05 §8.2 分批建图，requestIdleCallback 切片，非同步完成），故 locate **必须 `await viewer.load(floorId)` 等建图完成再 flyTo**——否则会对"还没建出来的库位"飞行，target 为空。`viewer.load(floorId)` **返回 `Promise<void>`，在 05 `onReady`（建图完成事件，05 §10）触发时 resolve**；同层定位（无需切层）则跳过 await 直接飞。

```ts
async locate(code: string): Promise<void> {
  // 1. 查位置（接口自动带租户过滤，见下）
  const r = await api.get('/api/space/location/locate', { code })
  //     → { locationId, floorId, absX, absY, absZ, placed, status } | 404
  // 2. 未找到 / 未放置 → 前端处理（见本节末"未放置库位前端处理"）
  if (!r) return toast.error('E-SPACE-601')                 // 无此库位编码
  if (!r.placed) return toast.warn('W-SPACE-601')           // 采纳态无几何，不可定位
  // 3. 目标在别层 → await 切层，等建图完成（Promise 在 05 onReady resolve）
  if (r.floorId !== viewer.currentFloorId) {
    toast.info('W-SPACE-602')                               // 切换楼层中，请稍候
    await viewer.load(r.floorId)                            // ★等 05 分批建图完成
  }
  // 4. 算机位 → flyTo（§2.4 补间）
  const target = viewer.dataToWorld({ x: r.absX, y: r.absY, z: r.absZ }) // 05，返回米 Vector3
  const { camPos, dist } = computeFlyPose(target, r.locationId)          // 见下机位算法
  await viewer.flyTo(camPos, target)                        // flyTo 返回 Promise，到位 resolve
  // 5. 到位：选中 + 高亮闪烁 + 信息卡
  select(r.locationId)                                       // §4.2 选中态
  blinkHighlight(r.locationId)                               // 高亮闪烁，见下参数
  showInfoCard(r.locationId)                                 // §4.2
  toast.info('I-SPACE-601')                                  // 已定位到库位
}
```

**flyTo 机位算法（v1.1评审补丁）**：
```ts
function computeFlyPose(target: THREE.Vector3, locationId: string) {
  // 距离按"库位/所属货架包围盒尺寸 × 系数"自适应（小库位飞近、大货架飞远）
  const box = viewer.getBucketBoundingBox(zoneOf(locationId)) // 或库位/货架级 bbox
  const size = box.getSize(new THREE.Vector3()).length()     // 包围盒对角线长
  const dist = THREE.MathUtils.clamp(size * FIT_FACTOR, MIN_DIST, MAX_DIST) // 系数 FIT_FACTOR≈1.8
  // 机位 = target + 等轴斜俯方向 × 距离（等轴方向是机位算法的方向向量，非§2.3 预设按钮）
  const isoDir = new THREE.Vector3(1, 1, 1).normalize()      // 右-上-前 45° 斜俯
  const camPos = target.clone().addScaledVector(isoDir, dist)
  return { camPos, dist }
}
```
- `target` = 库位 `dataToWorld(absXYZ)`（米）；`camPos` = `target + 等轴方向 × dist`；距离随包围盒尺寸缩放，保证库位在视口内"不糊不远"。

**高亮闪烁参数（v1.1评审补丁）**：
| 参数 | 取值 | 说明 |
|---|---|---|
| 脉冲次数 pulses | **3 次** | 闪 3 下引导视线，不无限闪 |
| 单脉冲周期 period | **~400ms**（亮↔原色一来回） | 总时长 ≈ 1.2s |
| 闪烁色 BLINK_COLOR | **醒目强调色（如亮橙/亮黄）** | 与默认灰、07 库存色、hover 色都不同 |
| 闪完落色 SELECTED_COLOR | **"选中态高亮"色（描边/亮色）** | 闪完**停在选中态高亮色**，区别于默认灰/库存色/hover 色 |
- 闪烁是临时脉冲（在原色 ↔ BLINK_COLOR 间补间 `pulses` 次），**闪完恢复到"选中态高亮"而非默认色**；原色经 §4.1 的"存原色"机制保留（可能是 07 库存色），清选中时再复原。

- 输入支持**精确编码**（唯一）→ 直接定位；支持**前缀/模糊**→ 返回候选列表（如 `A-03-*` 列出该货架所有库位），选一个再定位。
- 扫码枪/手输同一入口（库位编码是 join key，扫码即输码）。
- **🟡未放置库位前端处理（v1.1评审补丁）**：`locate` 命中但 `Placed=false`（采纳态无几何）→ **toast W-SPACE-601 + 不飞行**；`search` 前缀结果里的未放置库位 → **列出但标注 `[无几何]` 灰显、点击不可定位**（让用户知道有这个编码、只是还没放）。
- **🟡查询自动带租户（v1.1评审补丁，对齐 00/09 v1.1）**：`locate`/`search`/`detail` 后端查询的实体**继承 `BaseBizEntity` 全局查询过滤（按 TenantId）**，**无需在 Service/Controller 手写 `.Where(x => x.TenantId == ...)`**——租户隔离由 EF 全局过滤器自动施加，只能定位本租户库位。

### 6.2 跨层定位
- 目标库位 floorId ≠ 当前层 → 自动切层（第5章）再飞行；切层失败/无权限 → 提示。
- 这让"我要找 A-03-02-05"无论它在哪层都能一步到位——P1 的核心卖点之一。

### 6.3 为什么 P1 只做按编码
- 按库位编码定位**只需 Space 自有数据**（库位表有 code + AbsXYZ + floorId），P1 即可闭环。
- 按物料/批次/容器找货 = 先问 WMS"哪些库位有这个料"（`IWmsStockQuery`），拿到一批库位编码后**复用本章 locate 基础设施**逐个/批量定位——所以 07 不重造定位，只在前面接一层 WMS 反查（D8 P2 半）。

---

## 第7章 视角辅助

| 功能 | 行为 |
|---|---|
| 聚焦选中 Focus | 相机 flyTo 选中对象，target 设为它，缩放到合适距离（按对象包围盒算） |
| 视角复位 Home | flyTo 整层概览默认机位（第2.3） |
| 整层概览 Overview | 正交俯视铺满整层（接近 2D 平面图，便于看全局布局） |
| 双击聚焦 | 双击任意对象 = 聚焦它（= click 选中 + Focus） |
| 高亮闪烁 | 定位/搜索命中时脉冲高亮数次引导视线，闪完落"选中态高亮"色（**参数表见 §6.1：脉冲 3 次 / 周期 ~400ms / 醒目闪烁色，与默认灰/库存色/hover 色区分，v1.1评审补丁**） |

- 所有视角切换走第2.4 补间，不瞬移；用户操作随时打断飞行。

---

## 第8章 API 接口（增量）

06 主要是前端交互，仅新增**定位查询**接口（其余复用 05/00）：

| 端点 | 方法 | 说明 |
|---|---|---|
| `/location/locate?code=` | GET | 按库位编码精确定位 → `{locationId, floorId, absX/Y/Z, placed, status}`（第6.1） |
| `/location/search?prefix=&floorId=` | GET | 编码前缀/模糊搜索 → 候选库位列表（编码 + 路径摘要，第6.1 模糊分支） |
| `/location/{id}/detail` | GET | 选中信息卡详情（库位编码 + 变长层级路径 + Status + CodeOrigin + 坐标，第4.2） |
| `/floor/{id}/scene` | GET | 场景数据（复用 05/01，切层加载用） |

> 定位/搜索接口按库位编码（join key）查，命中即返回 floorId + AbsXYZ，足以驱动"切层 + 飞行"。模糊搜索按编码前缀走 `LIKE`（编码有序，前缀=同货架/同库区）。
> **租户隔离（v1.1评审补丁，对齐 00/09 v1.1）**：以上三个查询端点的实体均**继承 `BaseBizEntity` 全局查询过滤（TenantId）**，Service/Controller **无需手写 `.Where(x => x.TenantId == ...)`**——EF 全局过滤器自动只返回本租户库位。`locate` 命中但 `Placed=false`（采纳态无几何）时仍返回该行（带 `placed:false`），由前端 §6.1 处理（toast W-SPACE-601 / search 结果标 `[无几何]` 不可点）。

---

## 第9章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-SPACE-601 | Error | 无此库位编码 | locate 未命中（第6.1） |
| W-SPACE-601 | Warn | 该库位尚未放置，无法在 3D 中定位 | 命中库位 Placed=false（采纳态无几何，第6.1） |
| I-SPACE-601 | Info | 已定位到库位 {code}（{floor} 层） | 定位飞行完成 |
| I-SPACE-602 | Info | 找到 N 个匹配库位，请选择 | 前缀/模糊搜索多命中（第6.1） |
| W-SPACE-602 | Warn | 切换楼层中，请稍候 | 跨层定位触发切层（第6.2） |

---

## 第10章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 05 渲染内核（v1.1评审补丁补全句柄） | 用 ViewerHandle：SceneRoot、**`getBucketBoundingBox(zoneId)`（拾取粗筛取桶 Box3，05 §6.1 新补）**、`instanceToLocation`（拾取链正向）、**`locationToInstance(locationId)`（hover 复原 / 按 LocationId 反查实例，05 §5.2 新补）**、`worldToData`/`dataToWorld`（坐标互转，米↔mm 只在 05 内换一次，§3.3）、`setInstanceColor`（高亮，复原回原色见 §4.1）、`load(floorId)→Promise`/`onReady`（跨层定位 await 建图完成，§6.1）、`requestRender`（飞行/高亮触发重绘） |
| ← 00 数据模型 | 库位编码 join key、AbsXYZ 缓存（定位坐标）、FloorId（跨层）、Placed（未放置不可定位）、变长路径（信息卡） |
| → 07 实时叠加 | 07 复用本章"飞行+高亮+定位"基础设施；把"输入编码"前置一层"物料→WMS 反查编码"（D8 P2 半）；hover/选中信息卡叠库存数据 |
| → 08 高级可视化 | 路径动画复用相机补间（巡游视角） |
| → PUB 权限 | 浏览/定位接功能权限；跨层定位受数据权限（无权层不可达） |
| 多租户 | 定位/搜索按 TenantId 过滤（经接口），只能定位本租户库位 |

> **P1 闭环达成**：00 建模 → 01/02 建几何 → 03 生成编码 → 04 发布 WMS → 05 渲染 → **06 浏览+按编码定位**。一条从无到有、可演示、可发布给 WMS 的完整空间数字底座 P1 链路在此收口。

---

## 自检
- [ ] 拾取的终点是什么？完整链路（屏幕→…→库位编码）怎么走？"包围盒粗筛 + 桶内精拾"为什么扛得住万级？
- [ ] 射线精拾 vs GPU 拾取各适合什么场景？默认用哪个？
- [ ] hover 与 click 各做什么？高亮复原为什么要记原色（联系 07 状态色）？为什么说 06 全程只读？
- [ ] 切层为什么是"重建场景"而非"移动相机"？（联系 00 每 Floor 局部坐标系）
- [ ] 按库位编码定位的完整流程？目标在别层怎么办？未放置（采纳态）库位为什么定位不了？
- [ ] D8 为什么把定位切成两半？06（按编码 P1）和 07（按物料 P2）怎么共用定位基础设施？
- [ ] 相机补间（flyTo）被哪些功能复用？为什么所有视角切换都补间不瞬移？

---

*实现：新建 `cp6.web/src/space-viewer/navigate/*`（CameraController + Picker[包围盒粗筛+射线/GPU] + Locator + Highlighter）+ `cp6.web/src/views/space/viewer/*`（搜索框 + 信息卡 + 楼层侧栏）。后端新增 `/location/locate`、`/location/search`、`/location/{id}/detail`（CP6.Core/Services/Space）。配套 xlsx（相机预设/补间参数 / 拾取两级链路时序 / 信息卡字段 / 定位流程[含跨层] / 快捷键）见同名 `.xlsx`。*
