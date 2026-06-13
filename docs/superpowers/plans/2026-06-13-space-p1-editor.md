# Space P1 编辑器（01+02）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**这是 P1 第二份计划**（第一份 = `2026-06-13-space-p1-backend.md` 后端地基）。本计划**依赖后端地基计划已落地**（9 实体 + `ISpaceTenantContext` + GET `/floor/{id}/scene` + 编码引擎 + `LocationGeometryService.RecalcRackLocationsAsync`）。

**Goal:** 落地 Space P1 的**建模编辑器**——01 章 2D 俯视 Konva 画布 + 模板化批量生成 + 草稿保存 + 场景导入导出 + D7 采纳反向建模入口，02 章受控自由布局交互（拖拽/旋转/打点/框选/捕捉/碰撞提示/撤销重做），以及它们驱动的**配套后端**（模板服务、整层差量保存、导入导出、绑码）。

**Architecture:** 前端 `cp6.web/src/space-editor/`（与视图解耦的 Konva 画布引擎，可独立单测）+ `cp6.web/src/views/space/editor/`（页面外壳）。Pinia `useEditorStore` 持有当前 Floor 场景对象图 + dirty 集 + 选中集。**01 管"成批生成几何"（`generate/` + `io/`），02 管"手工精修几何"（`interact/` + `command/`），二者共用同一 `SceneStage` 画布与 store**。库位坐标是货架位姿的派生：前端只动 `Rack.{X,Y,RotationZ}` 与 `Marker`，**保存时由后端 `RecalcRackLocationsAsync` 统一重算库位 AbsXYZ**。02 不新增保存通道，复用 01 的 POST `/floor/{id}/scene`。

**Tech Stack:** Vue 3.5 + TypeScript + **Konva.js（新增依赖）** + Pinia 3 + element-plus + vue-router 5 / 后端 .NET 8 + EF Core（扩展后端地基）。源文档：`docs/space/01-editor-template.md`、`docs/space/02-free-layout.md`（引用 00 §6 坐标公式、§9 接口）。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状 | **建议值** |
|---|---|---|---|
| **E-D1** | **Konva 依赖** | `package.json` 无 konva | 新增 `konva ^9`（纯 canvas，无 vue 包装；自行封装 `SceneStage`，不引 vue-konva 避免黑盒） |
| **E-D2** | **前端单测框架** | devDeps 只有 `@playwright/test`，**无 vitest** | 新增 `vitest` + `@vue/test-utils` 测纯逻辑（genRack/SnapEngine/CommandStack/坐标映射）；画布渲染/交互用 Playwright e2e。**若你不想引 vitest**，则纯逻辑测也走 Playwright component test——建议引 vitest（轻、快） |
| **E-D3** | **临时 Id 策略** | CP6 用 GUID 主键 | 前端 `crypto.randomUUID()` 生成对象 Id，保存直接用（00 §6 `Id=LocationId` 稳定主键，前后端 GUID 一致，**省 Id 映射**，01 §6.2） |
| **E-D4** | **批量生成落点** | 01 §9 给两实现 | v1 用**前端纯函数生成 + 随 `/scene` 保存**（预览即所得）；服务端 `/generate` 留大阵列优化（不在本计划） |
| **E-D5** | **库位在 2D 不画** | 01 §3.4 | 画布**只画货架矩形**（俯视，含 RotationZ），库位以"6×4 格"网格线/计数表达；库位 VO 在 store 里**按货架懒展开**（选中才展开），避免万级 Konva 节点 |

> **继承后端地基计划的决策**：TenantId 方案A（前端不感知，后端注入）、审计字段、`/scene` GET 已存在。本计划只**新增** POST `/scene`、template、export/import、bind-codes 后端。

---

## File Structure

### 前端画布引擎（`cp6.web/src/space-editor/`，与视图解耦、可单测）
- `SceneStage.ts` — Konva.Stage 封装：图层、缩放/平移、坐标映射（floor mm ↔ 屏幕 px，Y 翻转）
- `coords.ts` — 坐标映射纯函数（worldToScreen/screenToWorld）+ `computeAbs`（镜像 00 §6.1，前端预览用）
- `layers/` — `UnderlayLayer.ts` `GridLayer.ts` `ZoneLayer.ts` `AisleLayer.ts` `RackLayer.ts` `MarkerLayer.ts`
- `generate/genRack.ts` `generate/genZoneArray.ts` — 模板生成纯函数（参数 → 货架+库位草稿 / 阵列+巷道）
- `io/sceneIo.ts` — 场景导出/导入序列化（前端侧，配合后端 ID 重映射）
- `interact/InteractionManager.ts` + `interact/tools/{SelectTool,DragTool,RotateTool,MarkerTool}.ts` — 02 工具状态机
- `interact/snap/SnapEngine.ts` — 捕捉求解（网格/货架边/巷道中心线/对齐）
- `interact/collide/CollisionHint.ts` — OBB+SAT 碰撞与越界（实时着色）
- `command/Command.ts` `command/CommandStack.ts` + `command/commands/{MoveRackCmd,RotateRackCmd,AddMarkerCmd,MoveMarkerCmd,EditMarkerCmd,DeleteCmd,BatchCmd}.ts`

### 前端视图 / 状态 / 类型 / API
- `cp6.web/src/views/space/editor/FloorEditor.vue` — 编辑器外壳（工具栏+画布+属性面板+模板库侧栏）
- `cp6.web/src/views/space/editor/panels/{RackPanel,MarkerPanel,TemplatePanel,BindCodesDialog}.vue`
- `cp6.web/src/stores/spaceEditor.ts` — `useEditorStore`（场景对象图 + dirty 集 + 选中集 + CommandStack 持有）
- `cp6.web/src/types/space/scene.ts` — `EditorScene/RackVO/ZoneVO/AisleVO/LocationVO/MarkerVO/TemplateVO` + DTO 类型
- `cp6.web/src/api/space/scene.ts` `template.ts` `io.ts` — axios 封装（`{code,message,data}` 信封）
- `cp6.web/src/router/` — 新增 space 路由（编辑器页）

### 配套后端（扩展 `CP6.Core/Services/Space/` + `CP6.WebApi/Controllers/Space/`）
- `ITemplateService.cs` / `TemplateService.cs` — 模板 CRUD + clone（`Space_Template`）
- `ISceneService.cs` / `SceneService.cs` — `SaveSceneAsync`（整层差量 upsert + 触发 RecalcRack + RowVersion）+ `BindCodesAsync`（D7 绑码）
- `ISceneIoService.cs` / `SceneIoService.cs` — `ExportAsync` / `ImportAsync`（ID 重映射）
- `TemplateController.cs`（`/api/space/template*`）；`SpaceMasterController` 扩 POST `/floor/{id}/scene`、`/floor/{id}/export`、`/site/{id}/import`、`/rack/{id}/bind-codes`
- DTO：`CP6.Entity/DTOs/Space/SceneSaveDto.cs`、`TemplateDto.cs`、`SceneExportDto.cs`、`BindCodesDto.cs`

### 测试
- 前端（vitest）：`genRack.spec.ts` `coords.spec.ts` `SnapEngine.spec.ts` `CommandStack.spec.ts`
- 前端（Playwright e2e）：`space-editor.e2e.ts`（建仓→生成→拖拽→保存冒烟）
- 后端（xUnit+InMemory）：`SceneServiceTests.cs`（差量保存+recalc+乐观锁）、`SceneIoServiceTests.cs`（导入重映射）、`TemplateServiceTests.cs`、`BindCodesTests.cs`

---

## 实施分五阶段

- **Phase E**（E-1..E-4）：前端地基——依赖/类型/store/路由 + Konva 画布 + 坐标映射 + 图层
- **Phase F**（F-1..F-3）：模板 + 批量生成——后端 TemplateService + 前端 genRack/genZoneArray + 模板库 UI
- **Phase G**（G-1..G-4）：草稿保存 + 导入导出——后端 SceneService/SceneIoService + 前端保存/导入导出
- **Phase H**（H-1..H-6）：受控自由布局 02——Command 框架 + 选择/拖拽/旋转/捕捉/碰撞/撤销
- **Phase I**（I-1..I-2）：D7 反向建模——unplaced + bind-codes 后端 + 绑定 UI

---

# Phase E — 前端地基

## Task E-1: 依赖 + 类型 + API 封装 + 路由

**Files:**
- Modify: `cp6.web/package.json`
- Create: `cp6.web/src/types/space/scene.ts`, `cp6.web/src/api/space/{scene,template,io}.ts`
- Modify: `cp6.web/src/router/index.ts`（或既有路由文件）

- [ ] **Step 1: 装依赖**

Run: `cd cp6.web && npm i konva && npm i -D vitest @vue/test-utils jsdom`
Expected: package.json 出现 `konva`、devDeps 出现 `vitest`。在 `package.json` scripts 加 `"test:unit": "vitest run"`；新建 `vitest.config.ts`（`environment: 'jsdom'`，`@` alias 指 `src`）。

- [ ] **Step 2: 写类型（镜像 00 实体 + 01 §2.3 EditorScene）**

```ts
// src/types/space/scene.ts
export interface RackVO { id:string; zoneId:string; aisleId?:string|null; floorId:string; templateId?:string|null;
  rackCode:string; x:number; y:number; z:number; rotationZ:number;
  cols:number; levels:number; depthCount:number; cellW:number; cellH:number; cellD:number; rowVersion?:string|null }
export interface ZoneVO { id:string; floorId:string; zoneCode:string; zoneName:string; zoneType:number; polygon:string; color?:string|null }
export interface AisleVO { id:string; zoneId:string; aisleCode:string; polygon:string; centerline:string }
export interface LocationVO { id:string; rackId:string; floorId:string; locationCode:string|null; codeOrigin:number;
  col:number; level:number; depth:number; absX:number; absY:number; absZ:number; sizeW:number; sizeH:number; sizeD:number;
  placed:boolean; status:number; version:number }
export interface MarkerVO { id:string; floorId:string; x:number; y:number; z:number; markerType:number; text:string; refRackId?:string|null }
export interface FloorVO { id:string; siteId:string; level:number; floorCode:string; floorName:string; height:number;
  underlayImage?:string|null; underlayScale?:number|null; underlayOffsetX:number; underlayOffsetY:number; originX:number; originY:number }
export interface EditorScene { floor:FloorVO; zones:ZoneVO[]; aisles:AisleVO[]; racks:RackVO[]; locations:LocationVO[]; markers:MarkerVO[] }
export interface TemplateVO { id:string; templateCode:string; templateName:string; templateType:number; params:string }
// 差量保存载荷
export interface SceneSaveDto { racks?:RackVO[]; aisles?:AisleVO[]; zones?:ZoneVO[]; markers?:MarkerVO[]; locations?:LocationVO[];
  deletes?:{ racks?:string[]; aisles?:string[]; zones?:string[]; markers?:string[] } }
export type Envelope<T> = { code:number; message:string; data:T }
```

- [ ] **Step 3: 写 API 封装（仿 `src/api/wms/*` 风格）**

```ts
// src/api/space/scene.ts
import http from '../http'
import type { Envelope, EditorScene, SceneSaveDto } from '@/types/space/scene'

export const sceneApi = {
  get(floorId: string) { return http.get<any, Envelope<EditorScene>>(`/space/floor/${floorId}/scene`) },
  save(floorId: string, dto: SceneSaveDto) { return http.post<any, Envelope<{ idMap: Record<string,string> }>>(`/space/floor/${floorId}/scene`, dto) },
  export(floorId: string) { return http.get<any, Envelope<any>>(`/space/floor/${floorId}/export`) },
  import(siteId: string, dto: any) { return http.post<any, Envelope<{ floorId: string }>>(`/space/site/${siteId}/import`, dto) },
  bindCodes(rackId: string, pairs: { locationId:string; col:number; level:number; depth:number }[]) {
    return http.post<any, Envelope<any>>(`/space/rack/${rackId}/bind-codes`, { pairs })
  },
}
```
（`template.ts` 同模式：`/space/template` CRUD + `/space/template/{id}/clone`。）

- [ ] **Step 4: 加路由 + 提交**

在路由表加（仿既有 LayoutView 子路由）：
```ts
{ path: '/space/editor/:floorId', name: 'space-editor', component: () => import('@/views/space/editor/FloorEditor.vue') }
```
```bash
git add cp6.web/package.json cp6.web/vitest.config.ts cp6.web/src/types/space cp6.web/src/api/space cp6.web/src/router
git commit -m "feat(space-editor): deps(konva,vitest) + scene types + api + route"
```

---

## Task E-2: 坐标映射纯函数（01 §3.1 / 00 §6.1 镜像）

**Files:** Create `cp6.web/src/space-editor/coords.ts`; Test `cp6.web/src/space-editor/coords.spec.ts`

- [ ] **Step 1: 失败测试**

```ts
// coords.spec.ts
import { describe, it, expect } from 'vitest'
import { worldToScreen, screenToWorld, computeAbs } from './coords'

describe('coords', () => {
  it('worldToScreen flips Y (world +Y is up, screen Y down)', () => {
    const view = { panX:0, panY:0, zoom:0.1, height:1000 }   // zoom px/mm
    const p = worldToScreen({ x:1000, y:2000 }, view)
    expect(p.x).toBeCloseTo(100)
    expect(p.y).toBeCloseTo(1000 - 200)   // Y 翻转
  })
  it('screenToWorld is inverse of worldToScreen', () => {
    const view = { panX:500, panY:300, zoom:0.2, height:800 }
    const w = { x:3456, y:7890 }
    const back = screenToWorld(worldToScreen(w, view), view)
    expect(back.x).toBeCloseTo(w.x, 1); expect(back.y).toBeCloseTo(w.y, 1)
  })
  it('computeAbs matches backend formula (anchor + rotate around corner)', () => {
    const rack = { x:1000, y:2000, z:0, rotationZ:0, cellW:1200, cellH:1500, cellD:1000 }
    expect(computeAbs(rack, 2, 2, 1)).toEqual({ x:1000+1800, y:2000+500, z:2250 })
  })
})
```

- [ ] **Step 2: 跑红** → Run: `cd cp6.web && npx vitest run coords` → FAIL

- [ ] **Step 3: 实现**

```ts
// coords.ts
export interface ViewState { panX:number; panY:number; zoom:number; height:number }  // zoom = px/mm
export interface XY { x:number; y:number }

export function worldToScreen(w: XY, v: ViewState): XY {
  return { x: (w.x - v.panX) * v.zoom, y: v.height - (w.y - v.panY) * v.zoom }   // Y 翻转
}
export function screenToWorld(s: XY, v: ViewState): XY {
  return { x: s.x / v.zoom + v.panX, y: (v.height - s.y) / v.zoom + v.panY }
}

// 镜像后端 LocationGeometryService.ComputeAbs（00 §6.1）；前端预览用，权威值后端重算
export function computeAbs(rack: { x:number;y:number;z:number;rotationZ:number;cellW:number;cellH:number;cellD:number },
  col:number, level:number, depth:number): { x:number;y:number;z:number } {
  const lx = (col - 0.5) * rack.cellW, lz = (level - 0.5) * rack.cellH, ly = (depth - 0.5) * rack.cellD
  const th = rack.rotationZ * Math.PI / 180, cos = Math.cos(th), sin = Math.sin(th)
  return { x: rack.x + Math.round(lx*cos - ly*sin), y: rack.y + Math.round(lx*sin + ly*cos), z: rack.z + Math.round(lz) }
}
```

- [ ] **Step 4: 跑绿 + 提交** → `npx vitest run coords` PASS → `git commit -m "feat(space-editor): coord mapping + computeAbs mirror (ch01 §3 / ch00 §6)"`

---

## Task E-3: SceneStage 画布封装 + 图层（01 §2/§3）

**Files:** Create `cp6.web/src/space-editor/SceneStage.ts`, `layers/*.ts`

> Konva 渲染本身不易单测（需 DOM canvas）；本任务靠 Playwright e2e 冒烟（E-4 后），单测仅覆盖已抽到 coords.ts 的纯逻辑。

- [ ] **Step 1: 实现 SceneStage**（管理 Konva.Stage + 6 图层 + zoom/pan + 坐标映射，渲染 EditorScene）

```ts
// SceneStage.ts（核心骨架）
import Konva from 'konva'
import type { EditorScene } from '@/types/space/scene'
import { worldToScreen, type ViewState } from './coords'

export class SceneStage {
  stage: Konva.Stage
  layers: { underlay:Konva.Layer; grid:Konva.Layer; zone:Konva.Layer; aisle:Konva.Layer; rack:Konva.Layer; marker:Konva.Layer }
  view: ViewState
  constructor(container: HTMLDivElement) {
    this.stage = new Konva.Stage({ container, width: container.clientWidth, height: container.clientHeight })
    this.view = { panX:0, panY:0, zoom:0.1, height: container.clientHeight }
    this.layers = { underlay:new Konva.Layer(), grid:new Konva.Layer(), zone:new Konva.Layer(),
      aisle:new Konva.Layer(), rack:new Konva.Layer(), marker:new Konva.Layer() }
    Object.values(this.layers).forEach(l => this.stage.add(l))
    this.bindZoomPan()
  }
  render(scene: EditorScene) {
    // UnderlayLayer: 底图按 UnderlayScale/Offset 贴（01 §3.2）；不可选中
    // GridLayer: 按 snapStep 画参考线，随 zoom 自适应
    // ZoneLayer/AisleLayer: 多边形（Polygon JSON → Konva.Line points），按 ZoneType 着色，可选中
    // RackLayer: 每货架一个 Konva.Rect（俯视，宽=cols*cellW 高=depthCount*cellD），rotation=rotationZ，绕锚点；内画 cols×levels 网格线表达库位（不逐库位建节点，E-D5）
    // MarkerLayer: 打点
    Object.values(this.layers).forEach(l => l.draw())
  }
  private bindZoomPan() { /* 滚轮缩放（以光标为锚）+ 拖拽空白平移；更新 this.view + 重绘 */ }
  worldToScreen(w:{x:number;y:number}) { return worldToScreen(w, this.view) }
}
```

> **实现者注**：货架矩形锚点对齐 00 §2 角点锚——Konva.Rect 的 `x,y` 设为 `worldToScreen(rack锚点)`，`offsetX/Y=0`，`rotation` 用 `-rotationZ`（屏幕 Y 翻转后旋转方向相反，需验证符号）；库位网格线在 Rect 内用 `Konva.Line` 画 cols-1 竖 + levels-1 横。底图标定流程（01 §3.2）：上传→量已知长度线→反算 UnderlayScale，写回 Floor。

- [ ] **Step 2: 构建（tsc）** → Run: `cd cp6.web && npx vue-tsc --noEmit` → 无错（或仅预期 TODO）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-editor): SceneStage + 6 Konva layers (ch01 §2/§3)"`

---

## Task E-4: useEditorStore + FloorEditor.vue 外壳（加载 + 渲染）

**Files:** Create `cp6.web/src/stores/spaceEditor.ts`, `cp6.web/src/views/space/editor/FloorEditor.vue`

- [ ] **Step 1: 写 store（Pinia setup 风，仿 `stores/order.ts`）**

```ts
// stores/spaceEditor.ts
import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { EditorScene, RackVO, MarkerVO } from '@/types/space/scene'

export const useEditorStore = defineStore('spaceEditor', () => {
  const scene = ref<EditorScene | null>(null)
  const dirty = ref({ upsert: new Set<string>(), del: new Set<string>() })
  const selection = ref<{ kind:string; ids:Set<string> }>({ kind:'rack', ids:new Set() })

  function load(s: EditorScene) { scene.value = s; dirty.value = { upsert:new Set(), del:new Set() } }
  function markDirty(id: string) { dirty.value.upsert.add(id) }
  function markDirtyDelete(id: string) { dirty.value.del.add(id); dirty.value.upsert.delete(id) }
  function rackById(id: string) { return scene.value!.racks.find(r => r.id === id)! }
  return { scene, dirty, selection, load, markDirty, markDirtyDelete, rackById }
})
```

- [ ] **Step 2: 写 FloorEditor.vue 外壳（加载 scene → SceneStage.render；工具栏/属性面板/模板库槽位占位）**

```vue
<!-- views/space/editor/FloorEditor.vue -->
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { sceneApi } from '@/api/space/scene'
import { useEditorStore } from '@/stores/spaceEditor'
import { SceneStage } from '@/space-editor/SceneStage'

const route = useRoute(); const store = useEditorStore()
const canvasRef = ref<HTMLDivElement>(); let stage: SceneStage
onMounted(async () => {
  const floorId = route.params.floorId as string
  const res = await sceneApi.get(floorId)
  store.load(res.data)
  stage = new SceneStage(canvasRef.value!)
  stage.render(res.data)
})
</script>
<template>
  <div class="floor-editor">
    <div class="toolbar"><!-- 工具按钮：选择/拖拽/旋转/打点/对齐/撤销重做（Phase H 接） --></div>
    <div ref="canvasRef" class="canvas"></div>
    <aside class="side"><!-- 模板库（Phase F）+ 属性面板（panels/）--></aside>
  </div>
</template>
```

- [ ] **Step 3: e2e 冒烟（Playwright）** — 打开 `/space/editor/:floorId`（mock 或种子数据），断言画布出现货架矩形。`cd cp6.web && npx playwright test space-editor`
- [ ] **Step 4: 提交** → `git commit -m "feat(space-editor): editor store + FloorEditor shell loads & renders scene (ch01 §2)"`

---

# Phase F — 模板与批量生成（01 §4/§5）

## Task F-1: 后端 TemplateService（CRUD + clone）

**Files:** Create `CP6.Core/Services/Space/{ITemplateService,TemplateService}.cs`, `CP6.Entity/DTOs/Space/TemplateDto.cs`, `CP6.WebApi/Controllers/Space/TemplateController.cs`; Test `CP6.Tests/TemplateServiceTests.cs`

- [ ] **Step 1: 失败测试**（创建模板编码唯一；clone 系统模板→当前租户新 Id + 新编码）

```csharp
[Fact]
public async Task Clone_CreatesNewIdSameParams()
{
    var t = DefaultSpaceTenantContext.DefaultTenant;
    using var db = Db();
    var src = new Space_Template { Id=Guid.NewGuid(), TenantId=t, TemplateCode="STD-RACK", TemplateName="标准货架", TemplateType=1, Params="{\"cols\":6}" };
    db.Space_Templates.Add(src); await db.SaveChangesAsync();
    var svc = new TemplateService(db, new DefaultSpaceTenantContext());
    var newId = await svc.CloneAsync(src.Id, "u");
    var copy = await db.Space_Templates.FirstAsync(x => x.Id == newId);
    Assert.NotEqual(src.Id, copy.Id);
    Assert.Equal("{\"cols\":6}", copy.Params);
    Assert.NotEqual("STD-RACK", copy.TemplateCode);   // 编码避撞（如 STD-RACK-COPY）
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现 TemplateService**（CRUD 仿 `SpaceMasterService` 范式；CloneAsync 复制 Params/Type、生成新编码 `{code}-COPY`/带序号、落当前租户）
- [ ] **Step 4: 跑绿 → Step 5: 写 Controller + DI（`AddScoped<ITemplateService,TemplateService>`）+ 提交**

```bash
git commit -m "feat(space): template service CRUD + clone (ch01 §4)"
```

---

## Task F-2: 前端模板生成纯函数 genRack / genZoneArray（01 §5.2）

**Files:** Create `cp6.web/src/space-editor/generate/{genRack,genZoneArray}.ts`; Test `generate/genRack.spec.ts`

- [ ] **Step 1: 失败测试**（genRack 产 1 货架 + cols×levels×depthCount 库位，全草稿空码 placed=true status0 codeOrigin1，坐标=computeAbs）

```ts
import { describe, it, expect } from 'vitest'
import { genRack } from './genRack'

it('genRack produces rack + full location array, all draft empty-code', () => {
  const tpl = { id:'t1', cols:2, levels:2, depthCount:1, cellW:1000, cellH:1000, cellD:1000 }
  const { rack, locs } = genRack(tpl, 'zone1', 'floor1', 0, 0, 0, 'R01')
  expect(locs).toHaveLength(4)
  expect(locs.every(l => l.locationCode === null && l.status === 0 && l.codeOrigin === 1 && l.placed)).toBe(true)
  expect(locs[0].absX).toBe(500)   // computeAbs(col1,level1,depth1)
})
```

- [ ] **Step 2: 跑红 → Step 3: 实现 genRack（照 01 §5.2 + coords.computeAbs）+ genZoneArray（按 rows/racksPerRow/rowGap/rackGap 循环 genRack，aisleBetweenRows 则生成 Space_Aisle 多边形+中心线）**

```ts
// genRack.ts
import { computeAbs } from '../coords'
import type { RackVO, LocationVO } from '@/types/space/scene'
export interface RackTemplate { id:string; cols:number; levels:number; depthCount:number; cellW:number; cellH:number; cellD:number }

export function genRack(tpl: RackTemplate, zoneId:string, floorId:string, originX:number, originY:number, rotation:number, rackCode:string)
  : { rack: RackVO; locs: LocationVO[] } {
  const rack: RackVO = { id: crypto.randomUUID(), zoneId, floorId, templateId: tpl.id, rackCode,
    x:originX, y:originY, z:0, rotationZ:rotation, cols:tpl.cols, levels:tpl.levels, depthCount:tpl.depthCount,
    cellW:tpl.cellW, cellH:tpl.cellH, cellD:tpl.cellD }
  const locs: LocationVO[] = []
  for (let c=1;c<=tpl.cols;c++) for (let l=1;l<=tpl.levels;l++) for (let d=1;d<=tpl.depthCount;d++) {
    const a = computeAbs(rack, c, l, d)
    locs.push({ id:crypto.randomUUID(), rackId:rack.id, floorId, locationCode:null, codeOrigin:1,
      col:c, level:l, depth:d, absX:a.x, absY:a.y, absZ:a.z, sizeW:tpl.cellW, sizeH:tpl.cellH, sizeD:tpl.cellD,
      placed:true, status:0, version:0 })
  }
  return { rack, locs }
}
```

- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(space-editor): genRack/genZoneArray template generators (ch01 §5)"`

---

## Task F-3: 模板库 UI + 落点生成接画布（01 §5.1 / §4.3）

**Files:** Create `cp6.web/src/views/space/editor/panels/TemplatePanel.vue`; Modify `FloorEditor.vue`, `SceneStage.ts`

- [ ] **Step 1: 实现**——模板库侧栏（列模板 + 参数表单 + 缩略预览）；选模板 → 画布落点（点击=单架 genRack / 框选=genZoneArray 阵列）→ 幽灵预览 → 确认 → 写 store.scene + markDirty → SceneStage 重绘。生成校验（01 §5.3）：未选 Zone→E-101 阻断；落点出 Zone→W-101 提示；规模超阈值→W-103 二次确认。
- [ ] **Step 2: e2e**（选模板→画布点击→出现货架+库位计数→store dirty 含新 id）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-editor): template panel + place-to-generate (ch01 §5.1)"`

---

# Phase G — 草稿保存 + 导入导出

## Task G-1: 后端 SceneService.SaveSceneAsync（差量保存 + recalc + 乐观锁，01 §6.2）

**Files:** Create `CP6.Core/Services/Space/{ISceneService,SceneService}.cs`, `CP6.Entity/DTOs/Space/SceneSaveDto.cs`; Test `CP6.Tests/SceneServiceTests.cs`

- [ ] **Step 1: 失败测试**（保存新货架+库位→落库；改货架位姿→触发 RecalcRack 重算库位坐标；删除集→删；RowVersion 冲突→E-009）

```csharp
[Fact]
public async Task Save_UpsertRackAndLocations_RecalcsCoords()
{
    var t = DefaultSpaceTenantContext.DefaultTenant;
    using var db = Db();
    var geo = new LocationGeometryService(db, new DefaultSpaceTenantContext());
    var svc = new SceneService(db, new DefaultSpaceTenantContext(), geo);
    var rackId = Guid.NewGuid(); var locId = Guid.NewGuid();
    var dto = new SceneSaveDto { Racks = new() { new() { Id=rackId, ZoneId=Guid.NewGuid(), FloorId=Guid.NewGuid(), RackCode="R1",
        X=0,Y=0,Cols=1,Levels=1,DepthCount=1,CellW=1000,CellH=1000,CellD=1000 } },
        Locations = new() { new() { Id=locId, RackId=rackId, Placed=true, Col=1,Level=1,Depth=1,Status=0,CodeOrigin=1 } } };
    await svc.SaveSceneAsync(Guid.NewGuid(), dto, "u");
    var loc = await db.Space_Locations.SingleAsync();
    Assert.Equal(500, loc.AbsX);   // recalc 写入
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**

```csharp
// SceneService.SaveSceneAsync（核心）
public async Task<Dictionary<Guid,Guid>> SaveSceneAsync(Guid floorId, SceneSaveDto dto, string? user)
{
    var tid = _t.TenantId;
    using var tx = await _db.Database.BeginTransactionAsync();   // 真库事务；InMemory 忽略
    // 1. upsert zones/aisles/racks/markers/locations（按 Id 存在与否 insert/update；落 TenantId/Creator|Modifier）
    //    racks：检测位姿/尺寸是否变化 → 记 changedRackIds
    // 2. 处理 deletes（走删除护栏：Rack 有库位→E-003；Aisle SetNull）
    // 3. SaveChanges（RowVersion 冲突 → catch DbUpdateConcurrencyException → E-SPACE-009）
    // 4. 对 changedRackIds 逐个 _geo.RecalcRackLocationsAsync（库位坐标重算，码不变）
    await _db.SaveChangesAsync();
    foreach (var rid in changedRackIds) await _geo.RecalcRackLocationsAsync(rid);
    await tx.CommitAsync();
    return new();   // GUID 前后端一致，idMap 通常空（E-D3）
}
```

- [ ] **Step 4: 跑绿 → Step 5: Controller 加 POST `/floor/{id}/scene` + DI + 提交**

```bash
git commit -m "feat(space): scene diff-save + geometry recalc + optimistic lock (ch01 §6.2)"
```

---

## Task G-2: 前端保存（dirty 集 → POST /scene）

**Files:** Modify `FloorEditor.vue`, `stores/spaceEditor.ts`

- [ ] **Step 1: 实现**——"保存"按钮：从 dirty 集组 `SceneSaveDto`（upsert 的对象 + del 集）→ `sceneApi.save` → 成功清 dirty、刷新 RowVersion；409 乐观锁 → 弹"已被他人修改，刷新重试"（http.ts 已不自动 toast 409，调用方处理）。
- [ ] **Step 2: e2e**（生成→保存→reload 后货架仍在）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-editor): save dirty scene + 409 handling (ch01 §6)"`

---

## Task G-3: 后端 SceneIoService（export/import + ID 重映射，01 §7）

**Files:** Create `CP6.Core/Services/Space/{ISceneIoService,SceneIoService}.cs`, `CP6.Entity/DTOs/Space/SceneExportDto.cs`; Test `CP6.Tests/SceneIoServiceTests.cs`

- [ ] **Step 1: 失败测试**（导出含 meta+几何+模板、不含 TenantId/AbsXYZ/LocationCode/状态；导入全 Id 重映射、父子按映射重连、Status=0、LocationCode 清空、货架按参数 computeAbs 重建库位）

```csharp
[Fact]
public async Task Import_RemapsAllIds_AndRebuildsLocations()
{
    // 导出一个 floor → 导入到另一 site → 新 floor 的 rack/location 全新 Id，库位按参数重建、码空、status0
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（Export：投影几何+模板，剔除敏感字段；Import：建新 GUID 映射表，按映射重连 SiteId/FloorId/ZoneId/AisleId/RackId，注入当前租户，库位用 genRack 等价逻辑后端重建 + computeAbs）
- [ ] **Step 4: 跑绿 → Step 5: Controller GET `/floor/{id}/export` + POST `/site/{id}/import` + DI + 提交**

```bash
git commit -m "feat(space): scene export/import with ID remap (ch01 §7)"
```

---

## Task G-4: 前端导入导出 UI

**Files:** Modify `FloorEditor.vue`（导出下载 JSON / 导入上传 JSON → 跳新 floor）

- [ ] **Step 1-3: 实现 + e2e + 提交** → `git commit -m "feat(space-editor): import/export UI (ch01 §7)"`

---

# Phase H — 受控自由布局交互（02 章，纯前端）

## Task H-1: Command 框架（Command/CommandStack，02 §9）

**Files:** Create `cp6.web/src/space-editor/command/{Command.ts,CommandStack.ts}`; Test `command/CommandStack.spec.ts`

- [ ] **Step 1: 失败测试**（exec→可 undo/redo；新 exec 清 redo；超容量丢最旧；merge 合并栈顶）

```ts
import { describe, it, expect } from 'vitest'
import { CommandStack } from './CommandStack'
it('undo/redo + new exec clears redo', () => {
  const log:string[] = []; const stack = new CommandStack()
  const cmd = (n:string) => ({ label:n, do:()=>log.push('do'+n), undo:()=>log.push('un'+n) })
  stack.exec(cmd('A'), {} as any); stack.exec(cmd('B'), {} as any)
  stack.undo({} as any); expect(log.at(-1)).toBe('unB')
  stack.redo({} as any); expect(log.at(-1)).toBe('doB')
  stack.undo({} as any); stack.exec(cmd('C'), {} as any)   // 清 redo
  stack.redo({} as any); expect(log.at(-1)).toBe('doC')    // redo 无 B，C 不变
})
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 02 §9.1 CommandStack：undoStack/redoStack/cap=100/exec 含 merge/undo/redo）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(space-editor): Command + CommandStack double-stack undo/redo (ch02 §9)"`

---

## Task H-2: 位姿 Command（Move/Rotate/Batch + Marker，02 §4/§5/§7）

**Files:** Create `command/commands/*.ts`; Test `command/commands.spec.ts`

- [ ] **Step 1: 失败测试**（MoveRackCmd do/undo 改回 x/y + merge 同 id；BatchCmd undo 逆序；AddMarkerCmd/DeleteCmd 含快照还原）
- [ ] **Step 2: 跑红 → Step 3: 实现 7 个 Command（照 02 §4.3/§5.3/§7/§9.2 代码）**
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(space-editor): Move/Rotate/Marker/Batch/Delete commands (ch02 §4/5/7)"`

---

## Task H-3: 选择系统 + InteractionManager + 工具状态机（02 §2/§3）

**Files:** Create `interact/InteractionManager.ts`, `interact/tools/{SelectTool,DragTool,RotateTool,MarkerTool}.ts`

- [ ] **Step 1: 实现**——InteractionManager 绑 Konva 舞台事件分发当前 active tool；SelectTool（点选/Ctrl 加减选/Shift 追加/框选橡皮筋/Ctrl+A）；选中集是交互态不入栈；选中驱动 Konva Transformer 包围盒。
- [ ] **Step 2: e2e**（点货架→选中描边；框选多个；点空白清选）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-editor): selection system + tool state machine (ch02 §2/§3)"`

---

## Task H-4: 拖拽 + 旋转（接 Command + 捕捉占位，02 §4/§5）

**Files:** Modify `tools/DragTool.ts`, `tools/RotateTool.ts`

- [ ] **Step 1: 实现**——DragTool：选中对象按下拖动→幽灵跟随（不写 store）→松开构造 MoveRackCmd/BatchCmd exec；Esc 取消回起点。RotateTool：Konva Transformer 仅 rotateEnabled（禁缩放）→改 RotationZ 绕锚点→RotateRackCmd；角度吸附 0/15/30/45/90（±3°），Ctrl 关吸附。
- [ ] **Step 2: e2e**（拖货架→位置变→Ctrl+Z 复原；旋转→角度变）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-editor): drag + rotate via commands (ch02 §4/§5)"`

---

## Task H-5: 捕捉对齐 SnapEngine + 等距分布（02 §6）

**Files:** Create `interact/snap/SnapEngine.ts`; Test `snap/SnapEngine.spec.ts`

- [ ] **Step 1: 失败测试**（网格吸附取最近交点；阈值按屏幕 px 换算 mm；货架边吸附；超阈值不吸附返原值）

```ts
it('snaps to nearest grid intersection within threshold', () => {
  const eng = new SnapEngine({ snapStep:1000 })
  // 点 (1040, 980)，阈值 8px @ zoom0.1 → 80mm；最近网格 (1000,1000)，距 ~44mm < 80 → 吸附
  expect(eng.snap({ x:1040, y:980 }, { zoom:0.1, racks:[], aisles:[] })).toEqual({ x:1000, y:1000, snapped:true })
})
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（候选：网格交点/货架边角/巷道中心线/同行对齐；argmin 距离 < threshold(px→mm)；空间分桶邻近查询；等距分布/对齐成行批操作产 BatchCmd）
- [ ] **Step 4: 跑绿 + 接入 DragTool/RotateTool + 提交** → `git commit -m "feat(space-editor): SnapEngine + align/distribute (ch02 §6)"`

---

## Task H-6: 碰撞越界提示（OBB+SAT）+ 撤销重做接线 + 快捷键（02 §8/§9）

**Files:** Create `interact/collide/CollisionHint.ts`; Test `collide/CollisionHint.spec.ts`; Modify `FloorEditor.vue`（工具栏按钮 + 快捷键）

- [ ] **Step 1: 失败测试**（两旋转货架 OBB 相交判定 SAT：重叠→true，分离→false；越界=货架包围盒不全在 Zone 多边形内）
- [ ] **Step 2: 跑红 → Step 3: 实现 OBB+SAT（02 §8.2）+ 越界点在多边形内判定；命中着色（红=重叠/黄=越界），不阻断；右下角徽标计数**
- [ ] **Step 4: 跑绿 → Step 5: 工具栏接 undo/redo 按钮 + 快捷键（Ctrl+Z/Y、Delete、Ctrl+A、Esc、Ctrl 关捕捉，02 §9.6）+ e2e → 提交**

```bash
git commit -m "feat(space-editor): collision/out-of-zone hints + undo/redo keybindings (ch02 §8/§9)"
```

---

# Phase I — D7 采纳态反向建模（01 §8）

## Task I-1: 后端 BindCodesAsync（绑既有冻结码，01 §8.1⑤）

**Files:** Modify `SceneService.cs`（+ `BindCodesAsync`）, `SpaceMasterController.cs`; Test `CP6.Tests/BindCodesTests.cs`

- [ ] **Step 1: 失败测试**（把待绑库位[Status1/Placed false/CodeOrigin2/RackId null]绑到货架格口→回填 RackId/FloorId/col/level/depth/AbsXYZ/Size、Placed=true；LocationId 与 LocationCode 不变；不发布、Version 不变）

```csharp
[Fact]
public async Task BindCodes_FillsGeometry_KeepsCodeAndId_NoPublish()
{
    var t = DefaultSpaceTenantContext.DefaultTenant; using var db = Db();
    var rackId = Guid.NewGuid();
    db.Space_Racks.Add(new Space_Rack { Id=rackId, TenantId=t, ZoneId=Guid.NewGuid(), FloorId=Guid.NewGuid(), RackCode="R1", X=0,Y=0,Cols=1,Levels=1,DepthCount=1,CellW=1000,CellH=1000,CellD=1000 });
    var locId = Guid.NewGuid();
    db.Space_Locations.Add(new Space_Location { Id=locId, TenantId=t, Status=1, Placed=false, CodeOrigin=2, RackId=null, LocationCode="LEGACY-001", Version=5 });
    await db.SaveChangesAsync();
    var svc = MakeSceneSvc(db);
    await svc.BindCodesAsync(rackId, new[]{ (locId, 1,1,1) }, "u");
    var l = await db.Space_Locations.SingleAsync();
    Assert.True(l.Placed); Assert.Equal(rackId, l.RackId); Assert.Equal(500, l.AbsX);
    Assert.Equal("LEGACY-001", l.LocationCode); Assert.Equal(locId, l.Id); Assert.Equal(5, l.Version);  // 码/Id/版本不变、不发布
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现 BindCodesAsync**（校验库位 Status=1∧Placed=false∧CodeOrigin=2；回填几何 + computeAbs；Placed=true；不动 Code/Id/Status/Version；不发事件）
- [ ] **Step 4: 跑绿 → Step 5: Controller POST `/rack/{id}/bind-codes` + 提交**

```bash
git commit -m "feat(space): D7 reverse-modeling bind-codes (ch01 §8.1)"
```

---

## Task I-2: 前端反向建模 UI（待绑定列表 + 绑定对话框，01 §8.2）

**Files:** Create `cp6.web/src/views/space/editor/panels/BindCodesDialog.vue`; Modify `FloorEditor.vue`, `api/space/scene.ts`（已有 bindCodes）

- [ ] **Step 1: 实现**——拉 `/location/unplaced`（后端地基已有）→ 待绑定列表；用户摆货架（Phase F genRack 但库位先不建码）→ 打开绑定对话框：货架格口(col,level,depth) ←→ 待绑码配对（自动按顺序预匹配 + 人工拖拽校正）；三类 mismatch 着色（有几何无码=黄/有码无几何=红/数量不匹配=汇总，01 §8.2）→ 提交 `bindCodes`。
- [ ] **Step 2: e2e**（采纳导入桩数据→待绑列表→摆架→绑定→库位 placed）
- [ ] **Step 3: 提交** → `git commit -m "feat(space-editor): reverse-modeling bind UI (ch01 §8.2)"`

---

## Self-Review（对照 01/02 覆盖）

**01 覆盖：** 编辑器架构/Konva 画布(E-3) ✅ / 坐标映射+底图描图(E-2/E-3) ✅ / 图层(E-3) ✅ / 模板库+参数(F-1/F-3) ✅ / 模板化批量生成(F-2/F-3) ✅ / 草稿态+整层保存+乐观锁(G-1/G-2) ✅ / 场景导入导出+ID重映射(G-3/G-4) ✅ / D7 反向建模(I-1/I-2) ✅ / 生成校验(F-3) ✅ / API(template/scene POST/export/import/bind-codes 均落后端任务) ✅

**02 覆盖：** Command 框架双栈(H-1) ✅ / 7 Command(H-2) ✅ / 选择系统+工具状态机(H-3) ✅ / 拖拽+旋转(H-4) ✅ / 捕捉对齐+等距分布(H-5) ✅ / 碰撞越界 OBB+SAT(H-6) ✅ / 撤销重做+快捷键(H-1/H-6) ✅ / 复用 01 /scene 不新增保存(G-2/H-*) ✅ / 打点 Marker(H-2) ✅

**已知缺口/推迟（已标注）：**
1. **底图标定交互**（量已知长度反算 UnderlayScale，01 §3.2）——E-3 留注，建议作 E 阶段补充小任务。
2. **服务端 `/generate`**（01 §9 大阵列优化）——v1 走前端生成，推迟。
3. **多层堆叠预览 / 实时协同**（01 §6.3 / 02 §9.5 OT-CRDT）——P3+，明确不做。
4. **库位懒展开预览刷新**（02 §10.3 懒一致）——前端可选优化，权威值后端重算，可后补。

**Type 一致性：** `EditorScene/RackVO/LocationVO`(E-1)、`computeAbs`(E-2，与后端 `ComputeAbs` 同公式)、`genRack`(F-2)、`SaveSceneAsync`/`BindCodesAsync`(G-1/I-1)、`CommandStack.exec/undo/redo`(H-1)、`SnapEngine.snap`(H-5) 跨任务签名对齐。前端 `crypto.randomUUID()` = 后端 GUID 主键（E-D3，免映射）。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-space-p1-editor.md`。**下一步按工作流是你修订**（先拍板「关键前置决策」E-D1~E-D5）。这是 P1 第二份；第三份 = `2026-06-13-space-p1-viewer.md`（05/06 viewer）。定稿后执行方式同后端计划（Subagent-Driven 推荐 / Inline）。

---

*初稿生成于 2026-06-13。源：docs/space/01·02（引用 00 §6/§9）。已勘察 cp6.web 前端真实栈：Vue3.5+TS+Pinia3+vue-router5+element-plus+axios(http.ts 信封+409 处理)+vue-i18n；konva/three 均未引入需新增；devDeps 仅 Playwright 无 vitest。*
