# Space P1 Viewer（05+06）Implementation Plan（v1.1 同步）

> **v1.1 同步（2026-06-27）**：本计划初稿（2026-06-13）写于「后端地基待建」假设。**现 Space P1 后端地基（00/03/04，Phase A→D）+ 编辑器（01/02）均已全栈落地**（分支 `feat/space-p1-backend`，后端 1283 测 + 前端 vitest 90 绿）。viewer 是 P1 收官，按三点同步：
>
> **① 多租户＝复用真基建（同后端 v1.1）**：CP6 已有真·多租户（`CP6Context` 反射全局过滤/盖章 + `ITenantContext`）。配套后端 `SpaceLocateService` **构造只注入 `CP6Context`**（`public SpaceLocateService(CP6Context db)`），不注入租户上下文，查询不写 `.Where(TenantId==)`（全局过滤自动加）。下文代码/测试样例：`new SpaceLocateService(db, new DefaultSpaceTenantContext())`→`new SpaceLocateService(db)`、`DefaultSpaceTenantContext.DefaultTenant`→`TenantContext.DefaultTenant`（`using CP6.Core.Services.Common;`；实体 init 的 `TenantId = t` 可留）。**v1.1 风格锚点＝ as-built `CodeEngineService.cs`/`SpaceLocateService` 兄弟服务**。
>
> **② 对齐 as-built 后端（关键 reconciliation）**：
> - **GET `/floor/{id}/scene` 已实现**（`SpaceMasterService.GetSceneAsync`→`SceneDto`）。但 **`SceneLocationDto` 无 `zoneId` 也无 `rotationZ`**——而 K-2 `InstancedBuckets` 按 `zoneId` 分桶、实例矩阵需 `rotationZ`。**解法＝前端 enrich，不改后端**：`SceneDto.Racks`（RackDto）**已含 `zoneId` + `rotationZ`**，viewer 的 `SceneBuilder`（K-3）先从 `scene.racks` 建 `Map<rackId,{zoneId,rotationZ}>`，再给每个 location 补 `zoneId`（=其 rack 的）+ `rotationZ`（=其 rack 的）。**K-2 测试样例里 loc 上的 `zoneId`/`rotationZ` 即 enrich 后的形状**，InstancedBuckets 接收已 enrich 的 loc 不变。
> - **locate/search/detail 是本计划新增后端**（N-2）。`DetailAsync` 组变长 path（Rack→Zone→Floor→Site 跳 null Aisle）可仿 as-built `LocationPublishService.BuildItemAsync` 的 path 组装思路独立查；`LocationDetail.path` 字段与 as-built `LocationPath`（SiteCode/FloorLevel/ZoneCode/AisleCode?/RackCode/Col/Level/Depth）对齐。
> - 落点＝同一 worktree `D:\CP6-space-backend`（含后端 + 编辑器），后端服务加 `CP6.Core/Services/Space/`、控制器新建 `SpaceLocateController` 或扩既有。
>
> **③ 前端依赖**：**`three` 仍需新增**（`npm i three` + `npm i -D @types/three`；OrbitControls/CSS2DRenderer 走 `three/examples/jsm`，vite ESM 正常解析）。**`vitest ^4.1.9` + `@vue/test-utils` + `jsdom` 已由编辑器引入**（V-D5/J-1 的「若先于编辑器落地补装 vitest」已 moot，直接用）。**konva 已装（编辑器）但 viewer 不用**——viewer 是独立 Three.js 内核，与编辑器前端零共享对象，仅共享 `/scene` 数据源。
>
> **工作流**：本计划继续在 `feat/space-p1-backend` worktree 落码。Konva/Three 真实渲染/相机/拾取的浏览器行为留 Playwright/gstack 运行态 QA；可保证＝纯逻辑 vitest（SceneRoot 坐标适配/InstancedBuckets 映射/LOD/标签去重/flyTo 缓动）+ vue-tsc + build。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流**：**这是 P1 第三份（收官）计划**（后端地基 + 编辑器**均已落地**）。依赖**已落地的** GET `/floor/{id}/scene`（库位有 AbsXYZ/FloorId/LocationCode，scene.racks 含 zoneId/rotationZ 供前端 enrich）。与编辑器**无前端共享对象**——viewer 是独立 Three.js 内核，仅共享 `/scene` 数据源。详见顶部 v1.1 同步注记。

**Goal:** 落地 Space P1 的 **3D 浏览**——05 章只读 Three.js 渲染内核 `space-viewer`（坐标系适配 + 参数化盒体 + InstancedMesh 分桶 + 视锥剔除/LOD + 标签虚拟化 + 按需渲染），06 章相机控制 + 拾取（→库位编码）+ hover/click 信息卡 + 楼层切换 + **按库位编码定位（D8 P1 半）**，以及 06 配套后端定位查询（locate/search/detail）。**P1 闭环在此收口**：建模→生成→编码→发布→渲染→浏览+定位。

**Architecture:** `cp6.web/src/space-viewer/`（与视图解耦的 Three.js 渲染内核，可独立测试）+ `cp6.web/src/views/space/viewer/`（浏览页外壳）。**坐标系适配是唯一收口点**：数据 Z-up/mm/每 Floor 局部系（00 §2）→ Three Y-up/米，靠 `SceneRoot` 根容器一次复合变换（`scale 0.001` + `rotation.x = -π/2`），所有对象用数据 AbsXYZ 直接建、不感知 Three。万级库位靠**单位盒 `UNIT_BOX` 复用 + InstancedMesh 按 ZoneId 分桶**（draw call 压到桶级）。05 只提供 `ViewerHandle` 挂点（getSceneRoot/worldToData/instanceToLocation/setInstanceColor/requestRender），相机/拾取/定位逻辑全在 06。

**Tech Stack:** Vue 3.5 + TypeScript + **Three.js（新增依赖）** + Pinia + element-plus / 后端 .NET 8 + EF Core（06 新增 3 个只读查询）。源文档：`docs/space/05-viewer-core.md`、`docs/space/06-camera-pick.md`（引用 00 §2 坐标系、§6 AbsXYZ、§9 scene）。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状 | **建议值** |
|---|---|---|---|
| **V-D1** | **Three.js 依赖** | 无 three | 新增 `three ^0.16x` + `-D @types/three`；用 `OrbitControls`、`CSS2DRenderer`（均来自 `three/examples/jsm`，需 vite 正常解析 ESM） |
| **V-D2** | **拾取方式** | 06 §3.1 给 A 射线 / B GPU 两案 | v1 默认 **A 射线精拾**（粗筛后候选桶实例数已压到百~千级，够快）；B GPU 拾取留配置项不实现（YAGNI） |
| **V-D3** | **标签渲染** | 05 §7.2 CSS2D / Sprite 两案 | Medium tier 默认 **CSS2DRenderer**（HTML 文字清晰）；超量回退 Sprite 留接口不实现 |
| **V-D4** | **坐标适配方式** | 05 §3.2 | 用 **SceneRoot 容器复合变换**（`scale 0.001` + `rotation.x=-π/2`），对象用数据 mm 直接建；反算用 `sceneRoot.worldToLocal` 除以缩放还原 mm（V-D 注意：worldToLocal 已含逆缩放，得到的是数据系坐标） |
| **V-D5** | **单测范围** | viewer 重渲染、难测 | 纯逻辑单测（坐标适配数学、instanceId↔LocationId 映射、LOD 档位判定、标签去重）走 **vitest**（编辑器计划已引入）；渲染/相机/拾取走 **Playwright e2e** + 手测 |

> **继承（v1.1）**：vitest + @vue/test-utils + jsdom **已由编辑器落地引入**（直接用，无需补装）；konva 已装但 viewer 不用。多租户复用真基建，前端不感知（后端全局过滤自动隔离）。three 仍需新增（V-D1）。

---

## File Structure

### 渲染内核（`cp6.web/src/space-viewer/`，与视图解耦、可单测）
- `SpaceViewer.ts` — 内核入口：renderer/scene/camera/loop 生命周期 + `ViewerHandle` 实现
- `core/Renderer.ts` — WebGLRenderer 封装（尺寸/像素比/抗锯齿/上下文丢失恢复）
- `core/SceneRoot.ts` — ★坐标系适配根容器（Z-up→Y-up + mm→米）+ `worldToData`/`dataToWorld`
- `core/Loop.ts` — rAF 循环 + 按需渲染 + 节流任务（剔除/标签/LOD）
- `build/BoxFactory.ts` — 参数化盒体（共享 `UNIT_BOX` + 材质，D3 零素材）
- `build/InstancedBuckets.ts` — InstancedMesh 按 ZoneId 分桶 + `instanceId↔LocationId` 双向表
- `build/SceneBuilder.ts` — `/scene` DTO → Three 对象图（分批建图，requestIdleCallback）
- `cull/FrustumCuller.ts` — 按桶包围盒视锥剔除
- `cull/LodController.ts` — 相机距离 → LOD 档（滞回）
- `labels/LabelVirtualizer.ts` — 标签可见+近+去重 + 对象池（≤200）
- `api/ViewerHandle.ts` — 对外句柄类型
- `navigate/CameraController.ts` — OrbitControls + 投影切换 + 视角预设 + flyTo 补间（06 §2）
- `navigate/Picker.ts` — 包围盒粗筛 + 射线精拾 → PickResult（06 §3）
- `navigate/Highlighter.ts` — hover/选中高亮（改 instanceColor，记原色）（06 §4）
- `navigate/Locator.ts` — 按编码定位（切层 + flyTo + 闪烁）（06 §6）

### 视图 / 状态 / 类型 / API
- `cp6.web/src/views/space/viewer/FloorViewer.vue` — 浏览页外壳（楼层侧栏 + 画布 + 工具条 + 搜索框）
- `cp6.web/src/views/space/viewer/{InfoCard,FloorList,SearchBox}.vue`
- `cp6.web/src/types/space/viewer.ts` — `PickResult`、`LocateResult`、`LocationDetail`
- `cp6.web/src/api/space/locate.ts` — locate/search/detail（axios 信封）
- `cp6.web/src/router/` — 新增 viewer 路由

### 配套后端（06 §8，扩展 `CP6.Core/Services/Space/`）
- `ISpaceLocateService.cs` / `SpaceLocateService.cs` — `LocateAsync`(code) / `SearchAsync`(prefix,floorId) / `DetailAsync`(id)
- `CP6.WebApi/Controllers/Space/SpaceMasterController.cs`（或新 `SpaceLocateController`）扩 `/location/locate`、`/location/search`、`/location/{id}/detail`
- DTO：`CP6.Entity/DTOs/Space/LocateDtos.cs`

### 测试
- 前端（vitest）：`SceneRoot.spec.ts`（坐标适配往返）、`InstancedBuckets.spec.ts`（映射）、`LodController.spec.ts`（档位+滞回）、`LabelVirtualizer.spec.ts`（去重+池上限）
- 前端（Playwright e2e）：`space-viewer.e2e.ts`（加载→旋转→点中库位→信息卡→搜索定位）
- 后端（xUnit+InMemory）：`SpaceLocateServiceTests.cs`（locate 命中/未命中/未放置、search 前缀、detail 变长路径）

---

## 实施分五阶段

- **Phase J**（J-1..J-3）：内核地基——依赖 + SpaceViewer + SceneRoot 坐标适配 + Renderer + Loop
- **Phase K**（K-1..K-3）：建图——BoxFactory + InstancedBuckets 分桶 + SceneBuilder（消费 /scene）
- **Phase L**（L-1..L-3）：性能——视锥剔除 + LOD + 标签虚拟化 + 按需渲染基线
- **Phase M**（M-1..M-3）：交互——相机控制 + 拾取链路 + hover/click 信息卡
- **Phase N**（N-1..N-4）：导航——楼层切换 + 后端定位查询 + 按编码定位 + 视角辅助 → **P1 闭环**

---

# Phase J — 内核地基

## Task J-1: 依赖 + 类型 + ViewerHandle + 路由

**Files:** Modify `package.json`; Create `src/types/space/viewer.ts`, `src/space-viewer/api/ViewerHandle.ts`; Modify router

- [ ] **Step 1: 装依赖**

Run: `cd cp6.web && npm i three && npm i -D @types/three`
（若 vitest 未装[编辑器计划已装]：`npm i -D vitest @vue/test-utils jsdom` + vitest.config.ts。）

- [ ] **Step 2: 写类型 + ViewerHandle 接口**

```ts
// src/types/space/viewer.ts
import type { Vector3 } from 'three'
export interface PickResult { kind:'location'|'rack'|'zone'|'marker'; locationId?:string; locationCode?:string;
  rackId?:string; rackCode?:string; zoneId?:string; worldPoint:Vector3; dataPoint:{x:number;y:number;z:number} }
export interface LocateResult { locationId:string; floorId:string; absX:number; absY:number; absZ:number; placed:boolean; status:number }
export interface LocationDetail { locationId:string; locationCode:string; path:{ siteCode?:string; floorLevel:number; zoneCode?:string; aisleCode?:string|null; rackCode?:string; col:number; level:number; depth:number };
  status:number; codeOrigin:number; absX:number; absY:number; absZ:number }
```
```ts
// src/space-viewer/api/ViewerHandle.ts
import type { Group, Vector3 } from 'three'
export interface ViewerHandle {
  load(floorId: string): Promise<void>; dispose(): void
  getSceneRoot(): Group
  worldToData(v: Vector3): { x:number; y:number; z:number }
  dataToWorld(p: { x:number; y:number; z:number }): Vector3
  instanceToLocation(meshId: number, instanceId: number): string | null
  setInstanceColor(locationId: string, hex: number): void
  requestRender(): void
  onReady(cb: () => void): void; onProgress(cb: (done:number, total:number) => void): void
}
```

- [ ] **Step 3: 加 viewer 路由 + 提交**

```ts
{ path: '/space/viewer/:siteId', name: 'space-viewer', component: () => import('@/views/space/viewer/FloorViewer.vue') }
```
```bash
git commit -m "feat(space-viewer): deps(three) + viewer types + ViewerHandle + route"
```

---

## Task J-2: SceneRoot 坐标适配（05 §3，唯一收口，先做+单测）

**Files:** Create `src/space-viewer/core/SceneRoot.ts`; Test `core/SceneRoot.spec.ts`

- [ ] **Step 1: 失败测试**（数据 mm/Z-up → world 米/Y-up：dataToWorld 后 +Z 落到 world +Y；worldToData 可逆还原 mm）

```ts
import { describe, it, expect } from 'vitest'
import { Vector3 } from 'three'
import { SceneRoot } from './SceneRoot'

it('dataToWorld maps Z-up/mm to Y-up/meters', () => {
  const root = new SceneRoot()
  const w = root.dataToWorld({ x:1000, y:2000, z:3000 })   // mm
  // scale .001 + rotation.x -90°: data(x,y,z) → world(x*.001, z*.001, -y*.001)
  expect(w.x).toBeCloseTo(1.0); expect(w.y).toBeCloseTo(3.0); expect(w.z).toBeCloseTo(-2.0)
})
it('worldToData is inverse', () => {
  const root = new SceneRoot()
  const d0 = { x:3456, y:7890, z:1234 }
  const d1 = root.worldToData(root.dataToWorld(d0))
  expect(d1.x).toBeCloseTo(d0.x, 0); expect(d1.y).toBeCloseTo(d0.y, 0); expect(d1.z).toBeCloseTo(d0.z, 0)
})
```

- [ ] **Step 2: 跑红 → Step 3: 实现**

```ts
// SceneRoot.ts
import { Group, Vector3 } from 'three'
export class SceneRoot extends Group {
  constructor() {
    super()
    this.scale.setScalar(0.001)        // mm → 米
    this.rotation.x = -Math.PI / 2     // Z-up → Y-up：data(x,y,z) → world(x, z, -y)
    this.updateMatrixWorld(true)
  }
  dataToWorld(p: { x:number; y:number; z:number }): Vector3 {
    return this.localToWorld(new Vector3(p.x, p.y, p.z))   // 子对象用数据 mm 建，经本容器变换
  }
  worldToData(v: Vector3): { x:number; y:number; z:number } {
    const l = this.worldToLocal(v.clone())                 // 逆变换回数据 mm
    return { x: l.x, y: l.y, z: l.z }
  }
}
```

> **实现者注**：测试里 SceneRoot 需 `updateMatrixWorld` 后 localToWorld 才正确（无父级时 matrixWorld=matrix）。`RotationZ`（绕数据 Z 偏航）放入 SceneRoot 后自动成 Three 绕 Y——对象建矩阵时绕数据 Z 轴即可（K-2）。

- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(space-viewer): SceneRoot coord adapter Z-up/mm→Y-up/m (ch05 §3)"`

---

## Task J-3: Renderer + Loop + SpaceViewer 入口骨架（05 §2/§9）

**Files:** Create `core/Renderer.ts`, `core/Loop.ts`, `SpaceViewer.ts`

- [ ] **Step 1: 实现**——Renderer（WebGLRenderer，antialias，pixelRatio，resize，webglcontextlost 重建 E-501/W-502）；Loop（rAF + **按需渲染**：dirty 标记才 render，静止不空转；节流任务注册）；SpaceViewer（组装 renderer+scene+SceneRoot+camera+loop，实现 ViewerHandle 生命周期，`requestRender()` 置 dirty）。
- [ ] **Step 2: tsc 通过 + 提交** → `npx vue-tsc --noEmit` → `git commit -m "feat(space-viewer): Renderer + on-demand Loop + SpaceViewer entry (ch05 §2/§9)"`

---

# Phase K — 建图（消费 /scene）

## Task K-1: BoxFactory 参数化盒体（05 §4，零素材）

**Files:** Create `build/BoxFactory.ts`

- [ ] **Step 1: 实现**——全局共享 `UNIT_BOX = new BoxGeometry(1,1,1)`（永不 dispose）；`locMaterial` MeshLambertMaterial（默认灰，支持 instanceColor）；货架框 EdgesGeometry/线材质；库区面半透明 MeshBasicMaterial；光照 HemisphereLight + 一方向光。提供 `makeInstanceMatrix(absX,absY,absZ, rotZ, sizeW,sizeD,sizeH)` 组 Matrix4（数据系，SceneRoot 再转）。
- [ ] **Step 2: tsc + 提交** → `git commit -m "feat(space-viewer): parametric box factory, shared UNIT_BOX (ch05 §4)"`

---

## Task K-2: InstancedBuckets 分桶 + 映射表（05 §5，先单测映射）

**Files:** Create `build/InstancedBuckets.ts`; Test `build/InstancedBuckets.spec.ts`

- [ ] **Step 1: 失败测试**（按 ZoneId 分桶；instanceId↔LocationId 双向；只纳 Placed=true）

```ts
import { describe, it, expect } from 'vitest'
import { InstancedBuckets } from './InstancedBuckets'

it('buckets by zoneId and maps instance<->location, placed only', () => {
  const locs = [
    { id:'L1', zoneId:'Z1', placed:true, absX:0,absY:0,absZ:0, sizeW:1,sizeH:1,sizeD:1, rotationZ:0 },
    { id:'L2', zoneId:'Z1', placed:true, absX:1,absY:0,absZ:0, sizeW:1,sizeH:1,sizeD:1, rotationZ:0 },
    { id:'L3', zoneId:'Z2', placed:false, absX:0,absY:0,absZ:0, sizeW:1,sizeH:1,sizeD:1, rotationZ:0 },
  ]
  const b = new InstancedBuckets()
  b.build(locs as any)
  expect(b.bucketCount()).toBe(1)             // 只 Z1（Z2 全未放置）
  const meshId = b.meshIdForZone('Z1')!
  expect(b.instanceToLocation(meshId, 0)).toBe('L1')
  expect(b.instanceToLocation(meshId, 1)).toBe('L2')
})
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（过滤 placed；GroupBy zoneId；每桶建 InstancedMesh(UNIT_BOX, locMaterial, n)，setMatrixAt 用 BoxFactory.makeInstanceMatrix；维护 `Map<meshId, Map<instanceId, locationId>>` + 反向 `Map<locationId, {meshId,instanceId}>`；每桶 computeBoundingBox 供剔除）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(space-viewer): InstancedMesh buckets by zone + id mapping (ch05 §5)"`

---

## Task K-3: SceneBuilder 分批建图 + FloorViewer 加载（05 §8）

**Files:** Create `build/SceneBuilder.ts`; Modify `SpaceViewer.ts`; Create `views/space/viewer/FloorViewer.vue`

- [ ] **Step 1: 实现**——SceneBuilder.build(sceneDto)：①结构组(zones/aisles 多边形面+地面)同步建；②货架框（InstancedMesh 或线框）；③库位桶（InstancedBuckets，**分批**：requestIdleCallback 每帧建 M 桶，onProgress 回调 W-501/I-502）；④标签池空建。`SpaceViewer.load(floorId)`：`sceneApi.get` → SceneBuilder → 挂 SceneRoot → onReady(I-501)；`dispose()` 释放每层 InstancedMesh/材质/标签，**UNIT_BOX 不释放**。FloorViewer.vue 外壳挂 canvas + 调 load。
- [ ] **Step 2: e2e 冒烟**（打开 viewer，种子楼层 → 出现 3D 货架/库位盒）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-viewer): batched SceneBuilder + FloorViewer loads scene (ch05 §8)"`

---

# Phase L — 性能机制

## Task L-1: 视锥剔除 FrustumCuller（按桶，05 §6.1）

**Files:** Create `cull/FrustumCuller.ts`; Modify Loop（节流接入）

- [ ] **Step 1: 实现**——每帧（节流）用相机视锥测各桶包围盒，`bucket.visible = inFrustum`（视锥外整桶不提交 GPU）。单测可对"桶包围盒在视锥内/外"判定做纯几何测试。
- [ ] **Step 2: 提交** → `git commit -m "feat(space-viewer): per-bucket frustum culling (ch05 §6.1)"`

## Task L-2: LOD 控制器（按距离 + 滞回，05 §6.2）

**Files:** Create `cull/LodController.ts`; Test `cull/LodController.spec.ts`

- [ ] **Step 1: 失败测试**（远→隐库位桶/合并；中→货架框+库位格；近→+标签+状态色；档位切换带滞回防抖）
- [ ] **Step 2: 跑红 → Step 3: 实现**（distance→档，滞回阈值上下不同避免边界抖动）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(space-viewer): distance LOD with hysteresis (ch05 §6.2)"`

## Task L-3: 标签虚拟化 LabelVirtualizer（05 §7）

**Files:** Create `labels/LabelVirtualizer.ts`; Test `labels/LabelVirtualizer.spec.ts`; +CSS2DRenderer 接入 Loop

- [ ] **Step 1: 失败测试**（候选=视锥内∧近；屏幕网格去重每格≤K；总数≤maxLabels=200；对象池复用不随库位数增长）
- [ ] **Step 2: 跑红 → Step 3: 实现**（节流计算可见标签；LabelPool 固定上限循环复用 CSS2DObject；只近 LOD 显库位编码标签）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(space-viewer): virtualized labels + object pool (ch05 §7)"`

---

# Phase M — 交互（相机 / 拾取 / 信息卡）

## Task M-1: CameraController（OrbitControls + 投影 + 预设 + flyTo，06 §2）

**Files:** Create `navigate/CameraController.ts`; Modify FloorViewer（工具条按钮）

- [ ] **Step 1: 实现**——OrbitControls（阻尼 + 俯仰/缩放/平移约束）；透视↔正交切换（保视线、补间）；视角预设 俯视/等轴/正视/复位；`flyTo(camPos,target,duration,easing)` 每帧 lerp + controls.update + requestRender，到达回调（定位/聚焦共用）。flyTo 缓动函数可纯单测（easeInOutCubic）。
- [ ] **Step 2: e2e**（拖拽旋转视角变；点预设相机飞到位）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-viewer): camera orbit + projection + presets + flyTo (ch06 §2)"`

## Task M-2: Picker 拾取链路（粗筛 + 射线精拾 → 库位编码，06 §3）

**Files:** Create `navigate/Picker.ts`; Test `navigate/Picker.spec.ts`（映射部分）

- [ ] **Step 1: 失败测试**（给定射线命中某桶 instanceId → 经 InstancedBuckets.instanceToLocation → locationId → PickResult.locationCode；worldToData 反算 dataPoint）——映射/装配逻辑可单测，raycaster 命中用 mock。
- [ ] **Step 2: 跑红 → Step 3: 实现**（①包围盒粗筛：射线测各桶 bbox 得候选桶；②候选桶 raycaster.intersectObject → instanceId；③instanceToLocation→LocationId→查 code；拾取优先级 库位>货架>库区/打点，06 §3.2；worldToData 反算）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(space-viewer): two-stage pick (bbox cull + raycast) → location code (ch06 §3)"`

## Task M-3: Highlighter + hover/click 信息卡（06 §4）

**Files:** Create `navigate/Highlighter.ts`; Create `views/space/viewer/InfoCard.vue`; Modify FloorViewer

- [ ] **Step 1: 实现**——hover（节流拾取→改 instanceColor 高亮色，**记原色**复原，07 状态色兼容）；click（选中态描边/亮色 + 弹 InfoCard）；InfoCard 内容 = 库位编码+变长路径+Status+CodeOrigin+坐标（路径调后端 detail，N-2）。全程只读（D1）。
- [ ] **Step 2: e2e**（hover 高亮；click 弹信息卡）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-viewer): hover/click highlight + info card (ch06 §4)"`

---

# Phase N — 导航与定位（P1 闭环收口）

## Task N-1: 楼层切换（06 §5）

**Files:** Create `views/space/viewer/FloorList.vue`; Modify FloorViewer

- [ ] **Step 1: 实现**——侧栏列当前 Site 楼层（Floor.Level 排序，调后端地基 `/floor?siteId=`）；点层 → `viewer.dispose()` → `viewer.load(floorId)`（重建场景，每 Floor 局部系独立）；切层显进度（onProgress）+ 防抖（连点只切最后）；切层清选中。
- [ ] **Step 2: e2e**（切层→场景重建→相机复位）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-viewer): floor switching (ch06 §5)"`

## Task N-2: 后端定位查询 SpaceLocateService（06 §8）

**Files:** Create `CP6.Core/Services/Space/{ISpaceLocateService,SpaceLocateService}.cs`, `CP6.Entity/DTOs/Space/LocateDtos.cs`; Modify controller; Test `CP6.Tests/SpaceLocateServiceTests.cs`

- [ ] **Step 1: 失败测试**

```csharp
[Fact]
public async Task Locate_Found_ReturnsFloorAndCoords()
{
    var t = DefaultSpaceTenantContext.DefaultTenant; using var db = Db();
    var fid = Guid.NewGuid();
    db.Space_Locations.Add(new Space_Location { Id=Guid.NewGuid(), TenantId=t, FloorId=fid, LocationCode="A-03-02-05",
        Placed=true, Status=1, AbsX=100, AbsY=200, AbsZ=300 });
    await db.SaveChangesAsync();
    var svc = new SpaceLocateService(db, new DefaultSpaceTenantContext());
    var r = await svc.LocateAsync("A-03-02-05");
    Assert.NotNull(r); Assert.Equal(fid, r!.FloorId); Assert.Equal(100, r.AbsX);
}

[Fact]
public async Task Locate_NotFound_ReturnsNull() { /* → 控制器转 E-SPACE-601 */ }

[Fact]
public async Task Search_Prefix_ReturnsCandidates()
{
    // LocationCode LIKE 'A-03-%' → 候选列表（编码 + 路径摘要）
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（LocateAsync：按 code 精确查（TenantId 过滤）→ LocateResult（含 placed/status）；SearchAsync：`StartsWith(prefix)` + 可选 floorId，返回候选 + 路径摘要；DetailAsync：组变长路径（Rack→Zone→Floor→Site，跳 null Aisle，复用发布计划 BuildItem 的 path 组装逻辑或独立查））
- [ ] **Step 4: 跑绿 → Step 5: Controller `/location/locate?code=`、`/location/search?prefix=&floorId=`、`/location/{id}/detail`（locate 未命中→E-601，命中 Placed=false→W-601）+ DI + 提交**

```bash
git commit -m "feat(space): locate/search/detail backend queries (ch06 §8)"
```

## Task N-3: 前端按编码定位 Locator + 搜索框（06 §6，D8 P1 半）

**Files:** Create `navigate/Locator.ts`, `views/space/viewer/SearchBox.vue`; Create `api/space/locate.ts`

- [ ] **Step 1: 实现**——`locate(code)`：调 `/location/locate` → 未命中 E-601 / 未放置 W-601；目标在别层 → 先 `viewer.load(目标floorId)` 等建完 → `dataToWorld(absXYZ)` → `flyTo` 斜视机位 → 选中+高亮闪烁（脉冲）+ InfoCard（06 §6.1）；前缀模糊 → `/location/search` 返候选列表选一个再定位（I-602）。扫码枪/手输同一入口。
- [ ] **Step 2: e2e**（搜索框输编码→相机飞到+高亮；跨层编码→自动切层+定位）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-viewer): locate-by-code + search box (ch06 §6, D8 P1)"`

## Task N-4: 视角辅助 + P1 闭环冒烟（06 §7）

**Files:** Modify `navigate/CameraController.ts`, FloorViewer

- [ ] **Step 1: 实现**——聚焦选中 Focus（flyTo 选中对象，按包围盒定距）、复位 Home、整层概览 Overview（正交俯视铺满）、双击聚焦、定位高亮闪烁（脉冲数次回选中态高亮）。
- [ ] **Step 2: P1 端到端冒烟（e2e）**——种子一个 Floor（经编辑器或种子）→ viewer 加载渲染 → 旋转相机 → 点中库位看信息卡 → 搜索编码定位 → 断言飞到+高亮。**这条 e2e 即 P1 闭环验证**（00 建模→03 编码→04 发布→05 渲染→06 定位）。
- [ ] **Step 3: 提交** → `git commit -m "feat(space-viewer): view aids + P1 closed-loop e2e smoke (ch06 §7)"`

---

## Self-Review（对照 05/06 覆盖）

**05 覆盖：** 内核架构(J-3) ✅ / 坐标适配唯一收口(J-2) ✅ / 参数化盒体零素材(K-1) ✅ / InstancedMesh 分桶+映射(K-2) ✅ / 视锥剔除(L-1) ✅ / LOD 滞回(L-2) ✅ / 标签虚拟化+对象池(L-3) ✅ / 场景构建分批(K-3) ✅ / 按需渲染(J-3 Loop) ✅ / ViewerHandle 挂点(J-1/各任务) ✅
**06 覆盖：** 相机轨道+投影+预设+补间(M-1) ✅ / 拾取两级链路→编码(M-2) ✅ / hover/click 信息卡(M-3) ✅ / 楼层切换(N-1) ✅ / 按编码定位含跨层(N-3) ✅ / 视角辅助(N-4) ✅ / 后端 locate/search/detail(N-2) ✅ / 只读保证(M-3) ✅

**已知缺口/推迟（已标注）：**
1. **GPU 拾取（B 案）** — V-D2 留配置不实现（射线够用）。
2. **Sprite 标签回退** — V-D3 留接口不实现（CSS2D 够用）。
3. **多层堆叠俯视预览**（06 §5.2）— v1 侧栏列表，堆叠预览推迟。
4. **07/08 挂点**（setInstanceColor 状态色、巷道中心线路径）— 已在 ViewerHandle 预留，本计划不实现（属 P2/P3）。
5. **10 万级 / 多仓 streaming**（05 §9.2 超 Medium tier）— P2+。

**Type 一致性：** `ViewerHandle`(J-1) 各方法在 SpaceViewer(J-3)/Picker(M-2)/Highlighter(M-3)/Locator(N-3) 一致调用；`SceneRoot.worldToData/dataToWorld`(J-2)；`InstancedBuckets.instanceToLocation/meshIdForZone`(K-2)；`LocateResult/LocationDetail`(J-1) 与后端 `SpaceLocateService`(N-2) 字段对齐。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-space-p1-viewer.md`。这是 **P1 三份计划的收官**：
1. `2026-06-13-space-p1-backend.md`（00/03/04 后端地基）
2. `2026-06-13-space-p1-editor.md`（01/02 编辑器 + 配套后端）
3. `2026-06-13-space-p1-viewer.md`（05/06 viewer + 定位后端）← 本文

三份合起来覆盖 P1 全章（00~06），端到端闭环（N-4 e2e 验证）。**下一步按工作流是你修订**（拍板 V-D1~V-D5）。定稿后建议执行顺序：后端地基 → 编辑器 → viewer（viewer 依赖 /scene 与库位数据）。

---

*初稿生成于 2026-06-13。v1.1 同步于 2026-06-27（后端 A→D + 编辑器 01/02 已落地后）。源：docs/space/05·06（引用 00 §2/§6/§9）。cp6.web 真实栈：**three 仍需新增**、OrbitControls/CSS2DRenderer 走 three/examples/jsm；**vitest ^4.1.9 + @vue/test-utils + jsdom 已在**（编辑器引入）。as-built reconcile：SceneLocationDto 无 zoneId/rotationZ→viewer 前端从 scene.racks enrich。*
