# Space 05 · 3D 渲染内核 space-viewer 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

> **v1.1 评审补丁（2026-06-27 深审应用）**：补全坐标适配双向可逆公式（dataToWorld/worldToData，§3.2.1）；明确 InstancedMesh 桶须同时初始化 instanceMatrix + instanceColor（§5.3）；新增 WebGL 上下文丢失恢复流程（§8.4）；澄清货架框几何策略（库位零额外几何 / 货架框一个共享线框几何，§4.1）；补 LOD 阈值表与标签虚拟化参数表（§6.2 / §7.2.1）；LabelVirtualizer 复用剔除结果防 per-frame O(n)（§7.2）；澄清 P1 必需 vs P2+ 性能项（§9.2）；明确前端新增依赖（§12）；落码前先做万级性能 spike（文末）。相关处标「(v1.1评审补丁)」。

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-05 3D 渲染内核 space-viewer |
| 所属模块 | Space 空间数字底座 · Part 1（P1） |
| 里程碑 | **P1**（第一次"看见 3D"；下游 [06](./06-camera-pick.md) 相机拾取、[07](./07-stock-overlay.md) 库存叠加都长在本内核上） |
| 技术栈 | Vue3 + TypeScript + **Three.js**（WebGL）；独立 `space-viewer` 组件层，与编辑器解耦 |
| 命名空间 | `cp6.web/src/space-viewer`（渲染内核，可独立测试）/ `cp6.web/src/views/space/viewer`（浏览页外壳） |
| 落地决策 | D1 3D 只读浏览（建模在 2D，01/02）/ D2 单仓 ~1 万库位 InstancedMesh 基线（Medium tier）/ D3 **纯参数化盒体·零素材** / Y-up 适配（00 §2 点名本章） |
| 依赖 | [00 数据模型](./00-data-model.md)（坐标系 §2 Z-up·mm·RotationZ、AbsXYZ 缓存 §6、参数化盒体）、[01](./01-editor-template.md)（`/floor/{id}/scene` 场景接口） |

> **题眼**：本章把 00 的几何真相**画成 3D**。三件事定成败：① **坐标系适配**——数据是 **Z-up / mm / 每 Floor 局部系**（00 §2，数据模型不迁就渲染库），Three.js 是 **Y-up / 米**，本章在 `space-viewer` 内部做唯一一次轴向 + 单位适配（数据层永不感知 Three.js）；② **万级库位靠 InstancedMesh 分桶**（D2：单仓 ~1 万库位，一个单位盒体 + 每实例矩阵，draw call 从万级压到桶级）；③ **纯参数化盒体零素材**（D3：货架/库位都是 `BoxGeometry`，无 glTF/贴图，颜色靠材质，体积/位姿靠模板参数 + AbsXYZ）。**记住一句**：05 只**只读渲染**（消费 `/scene`，不写任何几何），交互（相机/拾取/定位）在 06、状态着色（库存）在 07——本章把"场景图 + 实例化 + 剔除 + 标签虚拟化"这套**渲染地基**夯实，让后两章只管挂功能。

---

## 目录
- 第1章 功能概述与定位（与 01/02/06/07 的边界）
- 第2章 space-viewer 架构（渲染内核分层，与编辑器解耦）
- 第3章 坐标系与单位适配（Z-up/mm → Three.js Y-up/米）
- 第4章 参数化盒体（D3 零素材：货架/库位几何）
- 第5章 InstancedMesh 分桶（D2 万级库位）
- 第6章 视锥剔除与 LOD（按库区桶 / 相机距离）
- 第7章 标签虚拟化（库位编码标签按需渲染）
- 第8章 场景构建与数据加载（消费 /scene，分批建图）
- 第9章 渲染循环与性能基线（Medium tier）
- 第10章 对外 API（组件接口，供 06/07/浏览页）
- 第11章 消息一览
- 第12章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：提供一个**只读**的 Three.js 3D 渲染内核 `space-viewer`，把某 Floor 的空间几何（库区/巷道/货架/库位/打点）高效画成 3D，承载万级库位的流畅浏览，为 06（交互）/07（库存叠加）提供场景图与挂点。

**本章范围（05）：**
- 渲染内核架构（场景/相机/渲染器/资源管理，与编辑器 Konva 完全解耦）。
- 坐标系/单位适配（Z-up/mm → Y-up/米）的唯一收口点。
- 参数化盒体生成（货架框 + 库位格，纯 `BoxGeometry`）。
- InstancedMesh 分桶渲染万级库位 + 货架。
- 视锥剔除、LOD、标签虚拟化三大性能机制。
- 场景构建（消费 `/floor/{id}/scene`）与分批建图。
- 渲染循环与 Medium tier 性能基线。

**不含（划清边界）：**
| 能力 | 去哪章 |
|---|---|
| 2D 建模 / 几何编辑 / 模板生成 | [01](./01-editor-template.md)/[02](./02-free-layout.md)（Konva，非本章） |
| 相机控制 / 拾取 / 楼层切换 / 按编码定位 | [06 章](./06-camera-pick.md) |
| 实时库存状态着色 / 热力 / 轮询 | [07 章](./07-stock-overlay.md) |
| 拣货路径动画 / 作业热图 | [08 章](./08-advanced-viz.md) |

> **05 与编辑器（01/02）彻底解耦**：编辑用 **Konva 2D**（俯视建模，D1），浏览用 **Three.js 3D**（只读）。二者**不共享渲染对象**，只共享**数据源**（同一 `/scene`）。这样 3D 内核可独立演进/测试，也可被"编辑器内嵌 3D 预览面板"复用，但 3D 永不承担编辑职责（D1：3D 只读）。

> **05 是地基章**：06/07/08 都在 05 的场景图上挂功能。所以本章把"对象怎么组织、怎么实例化、怎么剔除、怎么标签"定死，后续章节只增不改这套结构。

---

## 第2章 space-viewer 架构

### 2.1 分层
```
cp6.web/src/space-viewer/
  SpaceViewer.ts          内核入口：管理 renderer/scene/camera/loop 生命周期
  core/
    Renderer.ts             WebGLRenderer 封装（尺寸/像素比/抗锯齿/上下文丢失恢复）
    SceneRoot.ts            ★坐标系适配根容器（Z-up→Y-up，第3章）
    Loop.ts                 requestAnimationFrame 循环 + 节流任务（标签/剔除）
  build/
    SceneBuilder.ts         /scene DTO → Three 对象图（分批，第8章）
    BoxFactory.ts           参数化盒体几何/材质（共享复用，第4章）
    InstancedBuckets.ts     InstancedMesh 分桶管理（第5章）
  cull/
    FrustumCuller.ts        按桶视锥剔除（第6章）
    LodController.ts         相机距离 → LOD 切换（第6章）
  labels/
    LabelVirtualizer.ts     标签按需渲染 + 对象池（第7章本文档第7章）
  api/ViewerHandle.ts      对外句柄（06/07 用：取对象、注册 hover/pick 回调挂点）
cp6.web/src/views/space/viewer/
  FloorViewer.vue           浏览页外壳（楼层选择 + 画布 + 工具条；交互逻辑在 06）
```

### 2.2 三大对象组（场景图骨架）
```
SceneRoot (Group, 已做 Z-up→Y-up 适配 + mm→米缩放)
├─ StructureGroup   库区面/巷道面/地面网格（半透明，低面数）
├─ RackGroup        货架框（InstancedMesh 或线框，千级）
├─ LocationGroup    库位格（★InstancedMesh 分桶，万级，第5章）
└─ LabelLayer       标签（CSS2D/Sprite，虚拟化，第7章）
```
- 库位是性能大头（万级），独立 `LocationGroup` 全实例化；货架（千级）、结构（百级）压力小但也尽量实例化/低面数。
- `LabelLayer` 不在 SceneRoot 的适配变换内（标签要始终正对相机，单独处理，第7章）。

---

## 第3章 坐标系与单位适配（唯一收口点）

> 00 §2 铁律：**数据模型不迁就渲染库**。数据永远 Z-up / mm / 每 Floor 局部系；适配只在 `space-viewer` 内部做一次。

### 3.1 两个差异
| 维度 | 数据（00 §2） | Three.js 默认 | 适配 |
|---|---|---|---|
| 上方向 | **Z 轴向上**（X/Y 地面、Z 高度） | **Y 轴向上** | 轴重映射 |
| 单位 | **mm**（整数，仓库尺度上万） | 米（浮点，避免大坐标精度问题） | ÷1000 缩放 |

### 3.2 适配方案：SceneRoot 容器统一变换
在 `SceneRoot`（场景根 Group）上做一次复合变换，所有子对象**直接用数据坐标（mm、Z-up）构建**，由根容器统一转换：
```ts
// SceneRoot：先缩放 mm→米，再绕 X 轴 -90° 把 Z-up 转成 Y-up
sceneRoot.scale.setScalar(0.001)            // mm → 米
sceneRoot.rotation.x = -Math.PI / 2         // data(x,y,z=up) → world(x, z=up, -y)
// 等价坐标映射：dataXYZ(X,Y,Z) → worldXYZ(X, Z, -Y)，右手系保持
```
- **唯一收口**：库位/货架对象一律用 `AbsX/AbsY/AbsZ`(mm) 和 `RotationZ`(绕数据 Z 轴) 直接建，挂到 SceneRoot 下即自动正确。建对象的代码**完全不感知** Three.js 的 Y-up。
- `RotationZ`（数据绕 Z 轴偏航）= 在数据系内的绕上轴旋转；放进 SceneRoot 后自动成为 Three 世界里的绕 Y 轴——无需在每个对象上换算。
- **大坐标精度**：mm→米缩放后，单仓坐标落在百米量级浮点，远离 Z-fighting / float32 精度悬崖。

### 3.2.1 双向可逆互转函数（dataToWorld / worldToData，v1.1评审补丁）
适配必须**显式可逆**，06 拾取链才闭环。约定：数据 `{x,y,z}` 为数据系 mm（Z-up，整数）；world 为 Three 世界 `Vector3`（米，Y-up）。复合变换为 `world = R(x,-π/2) · S(0.001) · data`，**无平移**时展开为：`world.x = data.x/1000`、`world.y = data.z/1000`（数据 Z 上 → Three Y 上）、`world.z = -data.y/1000`（rotation.x=-π/2 使 数据 Y ↔ Three -Z）。
```ts
// dataToWorld：数据 mm {x,y,z} → Three 世界 Vector3（米）
function dataToWorld(d: { x: number; y: number; z: number }): THREE.Vector3 {
  return new THREE.Vector3(d.x / 1000, d.z / 1000, -d.y / 1000)
  // 等价：sceneRoot.localToWorld(new THREE.Vector3(d.x, d.y, d.z))
}

// worldToData：Three 世界 Vector3（米）→ 数据 mm {x,y,z}（dataToWorld 的逆）
function worldToData(v: THREE.Vector3): { x: number; y: number; z: number } {
  return {
    x: Math.round(v.x * 1000),
    y: Math.round(-v.z * 1000),   // 须 Y_data = -z（对应 world.z = -data.y/1000）
    z: Math.round(v.y * 1000),    // data.z = world.y * 1000
  }
}
```
- **手写式仅适用于 SceneRoot 无平移/无嵌套变换**（本设计 SceneRoot 仅 scale + rotation，成立）。
- **平移安全等价路径**：`const local = sceneRoot.worldToLocal(v.clone())` 直接得到数据 mm（`worldToLocal` 已含逆缩放 + 逆旋转，返回值即数据坐标，**不可再 ×1000 或换轴**，否则重复变换）；`sceneRoot.localToWorld()` 为其逆。两条路径结果一致，§3.3 的 06 拾取即走 `worldToLocal`。
- `Math.round` 把浮点回数据系整数 mm，消除往返累积误差；落点再 join `AbsXYZ` / 库位编码。

### 3.3 适配的下游契约（给 06/07）
- 06 拾取得到 Three 世界坐标后，用 `sceneRoot.worldToLocal()` 反算回**数据 mm 坐标**，再 join `AbsXYZ` / 库位编码——适配可逆，拾取链清晰。
- 07 库存叠加按**库位编码/LocationId** join（不靠坐标），不受适配影响。

---

## 第4章 参数化盒体（D3 零素材）

> D3：**纯参数化盒体，零外部素材**（无 glTF/OBJ/贴图）。所有可见体都是 `BoxGeometry` + 材质色，体积/位姿由模板参数（00 §4.7）和 AbsXYZ 决定。

### 4.1 几何复用（关键省内存）
```ts
// BoxFactory：单位盒体复用，靠每实例 scale 撑到真实尺寸
const UNIT_BOX = new THREE.BoxGeometry(1, 1, 1)   // 1mm³ 单位盒，全场景共享一份
// 库位实例矩阵 = T(AbsX,AbsY,AbsZ) · Rz(RotationZ) · S(SizeW, SizeD, SizeH映射)
// 注意：数据 SizeW/H/D 对应数据轴，缩放在数据系内施加（SceneRoot 再统一转 Y-up）
```
- **库位格零额外几何**：全场景**一个 `UNIT_BOX` geometry**，库位实心/半透明盒都复用它，靠实例 `Matrix4`（含非均匀 scale）撑成各自尺寸——库位侧零额外 geometry 内存。
- **货架框另算（v1.1评审补丁）**：货架"框"是线框（`EdgesGeometry` + 线材质），**几何拓扑与实心盒不同，无法与库位共用同一 `InstancedMesh` / 同一 geometry**。故货架框单独走**一个全场景共享的货架 `EdgesGeometry` 模板 + 实例化**（每货架一个实例矩阵）。即：**库位零额外几何、货架框一个共享线框几何**——而非"全场景零额外几何"。

### 4.2 材质与配色（05 用默认色，07 再叠状态）
| 对象 | 材质 | 颜色（05 基线） |
|---|---|---|
| 库位格 | `MeshLambertMaterial`（廉价光照）/ 半透明 | 统一默认灰（07 按库存状态改 instanceColor） |
| 货架框 | 线框 / 描边盒 | 中性色 |
| 库区面 | 半透明 `MeshBasicMaterial` | 按 ZoneType 淡色（00 库区类型） |
| 巷道面 | 半透明 | 淡色 + 中心线（08 拣货路径用） |
- 05 阶段**状态色统一默认**（D：v1 固定默认色 + 字段预留，YAGNI）；07 通过 `InstancedMesh.instanceColor` 按库存状态批量改色，**不重建几何**。
- 光照：一个 `HemisphereLight` + 一个方向光足够（盒体场景无需 PBR/阴影贴图，省 GPU）。

---

## 第5章 InstancedMesh 分桶（D2 万级库位）

### 5.1 为什么必须实例化
- 万级库位若每个一个 `Mesh` → 万级 draw call，必卡。`InstancedMesh` 把同 geometry+material 的 N 个对象合并为**一次 draw call**，每实例一个 `Matrix4`（存在 `instanceMatrix`）。
- 配 §4.1 的单位盒复用：**一个 InstancedMesh 理论可容纳整层库位**，draw call ≈ 桶数（而非库位数）。

### 5.2 分桶策略（兼顾剔除与着色）
单个超大 InstancedMesh 无法被视锥**按实例**剔除（Three 对 InstancedMesh 整体剔除）。故**按空间分桶**：
```
分桶键 = ZoneId（库区）  —— 每库区一个 LocationGroup 下的 InstancedMesh
  · 视锥剔除以"库区桶"为粒度生效（第6章）——相机只看一个区时，其他区桶整体不画
  · 桶内实例数 = 该区库位数（百~千级），单桶 draw call = 1
  · 桶过大（如开放区 > N 实例）再二次细分（网格分块）
```
- **着色维度**（07）：同一桶内用 `instanceColor` 按库存状态逐实例上色，不破坏分桶（颜色不增加 draw call）。
- 桶与数据的映射：维护 `instanceId ↔ LocationId` 双向表（拾取 06 用：射线命中 `instanceId` → 反查 `LocationId` → 库位编码）。

### 5.3 实例矩阵填充
```ts
// 建桶：遍历该 Zone 的已放置库位（Placed=true, AbsXYZ 非空）
const inst = new THREE.InstancedMesh(UNIT_BOX, locMaterial, locsInZone.length)
locsInZone.forEach((loc, i) => {
  const m = new THREE.Matrix4()
    .compose(
      pos(loc.absX, loc.absY, loc.absZ),                 // 数据 mm 坐标（SceneRoot 再转）
      quatFromZ(loc.rack.rotationZ),                     // 绕数据 Z 轴偏航
      scale(loc.sizeW, loc.sizeD, loc.sizeH))            // 数据系尺寸
  inst.setMatrixAt(i, m)
  bucketIndex.set(inst.id + ':' + i, loc.locationId)     // instanceId→LocationId
})
inst.instanceMatrix.needsUpdate = true

// 桶建好后必须同时初始化 instanceColor（v1.1评审补丁）：
// 即使 05 暂不按状态着色，也要预建 instanceColor 缓冲并填默认灰 0.8，
// 否则 07 首次写 instanceColor 时 Three 会新建缓冲，冲掉 05 的预置/默认。
inst.instanceColor = new THREE.InstancedBufferAttribute(
  new Float32Array(locsInZone.length * 3), 3)
const DEFAULT_GREY = new THREE.Color(0.8, 0.8, 0.8)
for (let i = 0; i < locsInZone.length; i++) inst.setColorAt(i, DEFAULT_GREY)
inst.instanceColor.needsUpdate = true
```
- 只渲染 `Placed=true` 的库位（采纳态未放置库位无几何，00 §7.2，不进场景）。
- **instanceMatrix 与 instanceColor 必须成对初始化（v1.1评审补丁）**：每个桶建成时两者都建好缓冲（matrix 撑位姿、color 默认灰 0.8）；07 着色只 `setColorAt` + `needsUpdate`，**不新建缓冲、不重建几何**——否则会冲掉 05 的预置缓冲。
- 货架（千级）同理可实例化（货架框 InstancedMesh）；数量小，也可不分桶。

---

## 第6章 视锥剔除与 LOD

### 6.1 视锥剔除（按桶）
- Three 默认对每个对象做 frustum culling，但 InstancedMesh 是整体——所以**分桶（§5.2）= 让剔除以库区为粒度生效**：相机视锥外的库区桶整体不提交 GPU。
- 每个桶维护**包围盒**（`computeBoundingBox`）；`FrustumCuller` 每帧（节流）用相机视锥测试各桶包围盒，`bucket.visible = inFrustum`。
- 大开放区桶过大时二次细分为网格子桶，让剔除更细。

### 6.2 LOD（按相机距离）
| 距离档 | 相机距离阈值（v1.1评审补丁） | 显示 | 目的 |
|---|---|---|---|
| FAR 远（俯瞰整层） | **> 100m**（滞回带 80–120m） | 库区色块 + 货架轮廓；**库位桶隐藏或合并为货架级色块** | 远处看不清单格，省渲染 |
| MID 中 | **30–100m** | 货架框 + 库位格（实例盒） | 主浏览距离 |
| NEAR 近 | **< 30m** | 库位格 + 标签（第7章）+ 07 状态色 | 看具体库位 |
- `LodController` 按相机到楼层/桶的距离切档；档位切换有滞回（hysteresis）防抖动（如 FAR↔MID 滞回带 80–120m：升档过 120m 才转 FAR、降档低于 80m 才回 MID，避免边界反复抖档）。
- LOD 与剔除叠加：先剔除（视锥外不算），再对可见桶定 LOD。

---

## 第7章 标签虚拟化（库位编码标签）

### 7.1 问题
万级库位若每个都建一个 `CSS2DObject`/`Sprite` 标签 → DOM/精灵爆炸、文字重叠不可读。**标签必须虚拟化**。

### 7.2 策略：可见 + 近 + 不重叠，才渲染
```
每帧（节流，如每 100ms 或相机停稳时）：
  candidates = 当前视锥内 ∧ 相机距离 < labelDist 的库位
  按屏幕投影去重：网格分桶屏幕空间，每格至多 K 个标签（防重叠）
  visibleLabels = 取前 maxLabels（如 ≤ 200 个）
  用对象池（LabelPool）复用 DOM/Sprite：
     新进入的 → 从池取一个、设文本(库位编码)+屏幕位置
     离开的   → 归还池（隐藏，不销毁）
```
- **对象池**：固定上限（如 200 个标签元素）循环复用，绝不随库位数增长。
- 货架级标签（RackCode，数量小）可常显或按中 LOD 显；库位编码标签仅近 LOD + 虚拟化。
- 标签渲染用 `CSS2DRenderer`（HTML 文字，清晰可选）或 `Sprite`（纯 GPU，量大更省）——Medium tier 默认 CSS2D，超量回退 Sprite。
- **防 per-frame O(n)（v1.1评审补丁）**：候选集计算**勿每帧遍历万级库位**——直接复用 §6.1 视锥剔除已得的**可见桶**结果作为候选来源（只在可见桶内挑标签），避免 per-frame 全量 O(n) 扫描。

### 7.2.1 标签虚拟化参数表（v1.1评审补丁）
| 参数 | 取值 | 说明 |
|---|---|---|
| 对象池上限 maxLabels | **≤ 200** | 标签 DOM/Sprite 元素总数封顶，绝不随库位数增长 |
| 计算节流 | **100ms** + 相机停稳触发 | 相机连续 **3 帧位移 < 视锥的 0.5%** 视为停稳，停稳后再算一遍精确标签 |
| 候选来源 | **可见桶内库位**（复用剔除结果） | 不全量遍历，避免 per-frame O(n) |
| 候选选择 | 按**屏幕投影面积 Top-200** | 近/大的库位优先显标签 |
| 屏幕去重 | **屏幕空间网格分桶**，每格至多 K 个 | 防文字重叠，超出该格的候选丢弃 |
| 渲染后端 | CSS2D 默认；**回退 Sprite** | 触发回退：同屏标签 > 150 或 fps < 55 |

### 7.3 标签内容
- 默认显 `LocationCode`；06 选中时显完整信息卡（库位编码 + 路径），07 可叠库存数。
- 标签不进 SceneRoot 的 Z-up 适配变换（它们是屏幕空间叠加），用库位 world 坐标投影到屏幕定位。

---

## 第8章 场景构建与数据加载

### 8.1 数据源（复用 01 的 /scene）
```
GET /api/space/floor/{id}/scene
  → { floor, zones, aisles, racks, locations(Placed=true), markers }   （01 §6.2 / 00 §9）
```
- 05 与编辑器**同一接口**：渲染消费的是保存后的几何（草稿或已发布都可浏览）。
- 库位可能万级：接口支持**按 Zone 分页/分片拉取**（`?zoneId=` 或分页），配合分桶（§5.2）边拉边建。

### 8.2 分批建图（避免主线程卡顿）
```
SceneBuilder.build(sceneDto):
  1. 建结构组（zones/aisles/floor 地面）—— 轻量，同步
  2. 建货架组（racks）—— 实例化，一次或分批
  3. 建库位桶（按 Zone 分桶建 InstancedMesh）—— ★分批：每帧建 M 个桶，
        用 requestIdleCallback / 切片，避免一次建万级实例阻塞 UI
  4. 建标签池（空池，运行时填充，第7章）
  进度回调 → 浏览页显示"加载中 N/Total 区"
```
- 切换楼层（06）= 释放旧 SceneRoot 子对象（dispose geometry 复用的除外、释放 InstancedMesh + 纹理）→ 重建。注意 `UNIT_BOX` 等共享资源**不 dispose**（全局复用）。

### 8.3 资源释放
- 离开浏览页 / 切楼层：`dispose()` 释放每楼层独有的 InstancedMesh、材质实例、标签 DOM；renderer 上下文保留复用。
- WebGL 上下文丢失（`webglcontextlost`）→ 监听并重建场景（健壮性，详见 §8.4）。

### 8.4 WebGL 上下文丢失与恢复（v1.1评审补丁）
GPU 驱动重置 / 设备休眠 / 后台标签页被回收会触发上下文丢失，不处理则黑屏且永不恢复。`Renderer` 须挂两个监听：
- **`webglcontextlost`**：第一步 `event.preventDefault()`（**关键**，不阻止默认浏览器不会再发 `restored`）→ 暂停 rAF 渲染循环 → 禁用相机/拾取交互 → 提示 W-SPACE-502（渲染上下文丢失，正在恢复）。
- **`webglcontextrestored`**：此时所有 GPU 资源（buffer/texture/program）已失效 → `dispose()` 当前楼层场景（InstancedMesh / 材质 / 标签 DOM）→ 用当前 floor 的 `/scene`（缓存或重拉）**重新 `SceneBuilder.build()`** → 恢复渲染循环与交互。
- **共享资源**：`UNIT_BOX` / 货架 `EdgesGeometry` 模板的 GPU 句柄随上下文失效，CPU 端数据仍在、下一帧会自动重传；恢复走全量 rebuild 时一并重建，**不复用旧 GPU 句柄**。
- **恢复失败兜底**：`restored` 超时未到或重建再次失败 → 升级提示用户**刷新页面**（E-SPACE-501 类）。
- **优先级**：上下文丢失恢复属健壮性增强，可排到 **P2+**（见 §9.2）；P1 至少做到"丢失即提示 + 暂停，不静默黑屏"。

---

## 第9章 渲染循环与性能基线

### 9.1 循环
```
Loop（rAF）每帧：
  - 仅相机/数据变化时重渲染（按需渲染，静止不空转，省电）
  - 节流任务（非每帧）：FrustumCuller（视锥剔除）、LabelVirtualizer（标签）、LodController
  - renderer.render(scene, camera) + labelRenderer.render
```
- **按需渲染**：相机不动、无动画（08）时不重绘，CPU/GPU 归零；07 库存刷新或 06 相机动才触发重绘。

### 9.2 Medium tier 性能基线（D2）
| 指标 | 目标 |
|---|---|
| 库位规模 | 单仓 **~1 万库位**（基线），峰值留余量 |
| draw call | 主要来自库位桶（≈ 库区数，几十级）+ 货架 + 结构，总计**百级以内** |
| 帧率 | 主浏览距离 **≥ 50–60fps**（中端独显/集显） |
| 首屏建图 | 分批 ≤ 2–3s 可交互（边建边显进度） |
| 标签 | 同屏 ≤ 200 个（对象池上限），不随库位数增长 |
- 超出 Medium tier（如多仓 / 10 万级）= P2+ 优化（更激进 LOD、按可见区流式加载、Web Worker 建图），不在 v1 基线。

> **P1 必需 vs P2+（v1.1评审补丁）**：**视锥剔除（§6.1）+ InstancedMesh 分桶（§5）+ LOD 三档（§6.2）+ 标签虚拟化（§7）= P1 必需**——万级库位缺任一项都会卡，这四件是 05 能在 P1 落地的前提，**不是可延后的"性能优化"**。真正可延后到 **P2+** 的是：WebGL 上下文丢失恢复（§8.4 的全量自动重建）、多仓 / 10 万级深度优化（更激进 LOD / 流式按区加载）、Web Worker 分块建图。故"性能优化留后"指的是 P2+ 那批深度项，与"05 的四大机制放 P1"并不矛盾。

---

## 第10章 对外 API（组件接口）

`space-viewer` 对 06/07/浏览页暴露句柄 `ViewerHandle`（不直接暴露 Three 内部）：

| 方法/事件 | 说明 | 给谁 |
|---|---|---|
| `load(floorId)` / `dispose()` | 加载/释放某层场景 | 浏览页 |
| `getSceneRoot()` | 取适配根容器（挂自定义对象） | 06/07/08 |
| `worldToData(v3)` / `dataToWorld(xyz)` | 坐标适配双向可逆互转（§3.2.1） | 06 拾取 |
| `instanceToLocation(meshId, instanceId)` | 实例 → LocationId（§5.2） | 06 拾取 |
| `setInstanceColor(locationId, color)` | 按库位改实例色（不重建） | **07 库存着色** |
| `requestRender()` | 触发一次按需渲染 | 07/08 |
| `onReady / onProgress` | 建图完成/进度事件 | 浏览页 |
- **05 只提供地基与挂点**；相机控制、射线拾取、楼层切换、按编码定位的**具体交互逻辑全在 06**；状态着色策略在 07。05 保证这些挂点稳定、可实例化、可剔除。

---

## 第11章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-SPACE-501 | Error | 浏览器不支持 WebGL，无法 3D 浏览 | 初始化无 WebGL 上下文 |
| W-SPACE-501 | Warn | 场景规模较大，已启用分批加载 | 库位数超阈值（如 > 8000） |
| W-SPACE-502 | Warn | 渲染上下文丢失，正在恢复 | `webglcontextlost` |
| I-SPACE-501 | Info | 场景加载完成（N 区 / M 货架 / K 库位） | 建图完成 |
| I-SPACE-502 | Info | 加载中 N/Total 区… | 分批建图进度 |

---

## 第12章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 00 数据模型 | 坐标系 §2（Z-up/mm/局部系，本章做 Y-up 适配）；AbsXYZ §6 缓存直接喂实例矩阵；参数化盒体 D3；只渲染 Placed=true |
| ← 01 编辑器框架 | 复用 `/floor/{id}/scene` 接口（同一数据源）；与 Konva 编辑器解耦，仅共享数据 |
| → 06 相机/拾取/定位 | 提供 SceneRoot + 坐标互转 + 实例→LocationId 映射 + requestRender 挂点；交互逻辑全在 06 |
| → 07 实时叠加 | 提供 `setInstanceColor` 按库位着色（不重建几何）；07 做状态/热力着色策略 + 刷新 |
| → 08 高级可视化 | 提供场景图挂点（巷道中心线供路径动画）；08 加动画对象 |
| → PUB 权限 | 浏览页接功能权限（3D 浏览授权）；场景查询接数据权限 |
| 多租户 | 场景数据按 TenantId（经 /scene），渲染层无租户逻辑 |
| 前端新增依赖（v1.1评审补丁） | 需新增 `three`（建议固定版本，如 `^0.16x`）+ `@types/three`；`OrbitControls`（06 用）/ `CSS2DRenderer`（标签）/ `EdgesGeometry`（货架框）经 `three/examples/jsm/*` 或 `three-stdlib` 引入；锁定版本避免破坏性升级 |

---

## 自检
- [ ] 数据坐标系是什么（轴向/单位/归属）？为什么数据不迁就 Three.js？适配在哪做、怎么做（轴重映射 + 缩放）？
- [ ] 为什么万级库位必须 InstancedMesh？单位盒复用 + 实例矩阵怎么把 draw call 压到桶级？
- [ ] InstancedMesh 整体剔除的问题靠什么解决？分桶键为什么用 ZoneId？桶与剔除/LOD/着色怎么叠加？
- [ ] 标签为什么必须虚拟化？"可见+近+不重叠"策略 + 对象池怎么保证标签数不随库位数增长？
- [ ] 参数化盒体零素材怎么体现？05 的配色与 07 的状态着色如何分工（instanceColor 不重建）？
- [ ] 按需渲染省什么？Medium tier 的库位规模/draw call/帧率基线各是多少？
- [ ] 05 提供哪些挂点给 06/07？为什么相机/拾取/着色不在 05 做？

---

> **落码前先做万级性能 spike（v1.1评审补丁）**：正式开 05 之前，先合成 **1 万库位 + 50 库区**的假数据，验证「分桶 + 视锥剔除 + LOD 三档 + 标签对象池」在**远/中/近三视角均 50–60fps**、**首屏分批建图 2–3s 可交互**、**LabelVirtualizer 单帧 < 16ms**。spike 达标再正式落 `space-viewer`；不达标先调机制阈值（桶粒度 / LOD 距离 / 标签上限），别带着性能债往下写。

*实现：新建 `cp6.web/src/space-viewer/*`（SpaceViewer 内核 + core/build/cull/labels/api）+ `cp6.web/src/views/space/viewer/FloorViewer.vue`（浏览页外壳）。复用 01 的 `/floor/{id}/scene`。配套 xlsx（坐标适配映射表 / 分桶与 draw call 估算 / LOD 档位表 / 标签虚拟化参数 / 性能基线指标）见同名 `.xlsx`。*
