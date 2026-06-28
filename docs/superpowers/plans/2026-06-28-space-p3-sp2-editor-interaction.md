# Space P3 · SP2 编辑器交互运行态收尾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 Konva 2D 编辑器四处交互运行态收口：lasso 用精确 OBB、旋转绕几何中心且无跳变、角度吸附有实时读数、模板放置幽灵跟随光标并按合法性着色。

**Architecture:** 纯前端（`cp6.web/src/space-editor/` + `SceneStage` + `FloorEditor.vue`），零后端 / 零 EF 迁移 / 零 i18n 键。纯逻辑（`rotateAboutCenter`/`snapAngle`/`obbIntersectsRect`/`arrayFootprint`）走 vitest TDD；画布交互运行态留集中 gstack/Playwright QA。渲染/碰撞/吸附全链继续锚点制，中心枢轴逻辑只活在 `RotateTool` + `RotateRackCmd`。

**关键实现决策（落码前必读）：** 经核实，Konva `Transformer` 对**单个节点**的旋转枢轴 = 该节点**包围盒中心**；而 rack group 的 position=锚点、子节点画在 `(0,-dPx)~(wPx,0)`，故其包围盒中心**恰为货架几何中心**。因此 Transformer 的**实时旋转预览本就绕几何中心**——P1 的"跳变"仅来自 `RotateRackCmd` 只改 `rotationZ`（枢轴=锚点）。**结论：保留 Konva Transformer 旋转，只把提交时的锚点用 `rotateAboutCenter` 回算**（满足 spec 的中心枢轴/消跳变/吸附/读数全部意图，且远低于自定义手柄的风险）。这是对 spec §2.3「自定义手柄」措辞的有据简化。

**Tech Stack:** Vue 3.5 + TS + Pinia + Konva（2D）+ vitest 4 + Playwright/gstack。

**命令速查（worktree `D:\CP6-space-backend`，bash cwd 每次重置回 `D:\CP6`）：**
- 单测：`cd /d/CP6-space-backend/cp6.web && npx vitest run <spec 相对路径>`
- 类型门：`cd /d/CP6-space-backend/cp6.web && npm run type-check`
- 构建门：`cd /d/CP6-space-backend/cp6.web && npm run build`
- e2e：`cd /d/CP6-space-backend/cp6.web && npx playwright test e2e/<spec> --project=chromium`

**交付序：** ③ lasso（Task 1-2）→ ① 旋转中心枢轴（Task 3-5，含 ② 读数）→ ② 角度纯逻辑测（并入 Task 3）→ ④ 幽灵跟随（Task 6-8）→ 集中 QA（Task 9）。

---

## 文件清单

新增：
- `cp6.web/src/space-editor/interact/select/lassoHit.ts`（`obbIntersectsRect`）+ `lassoHit.spec.ts`
- `cp6.web/src/space-editor/interact/rotate/rotateGeometry.ts`（`rotateAboutCenter` + `snapAngle`）+ `rotateGeometry.spec.ts`
- `cp6.web/src/space-editor/generate/arrayFootprint.ts` + `arrayFootprint.spec.ts`

改动：
- `cp6.web/src/space-editor/interact/collide/CollisionHint.ts`（export `Vec2`/`project`/`separated`）
- `cp6.web/src/space-editor/interact/tools/SelectTool.ts`（OBB lasso）
- `cp6.web/src/space-editor/command/commands/RotateRackCmd.ts`（位姿命令）
- `cp6.web/src/space-editor/command/commands.spec.ts`（RotateRackCmd 用例更新）
- `cp6.web/src/space-editor/interact/tools/RotateTool.ts`（中心枢轴提交 + 角度读数；删内联 `snapAngle`，改 import）
- `cp6.web/src/space-editor/SceneStage.ts`（`showFootprintGhost`）
- `cp6.web/src/space-editor/interact/InteractionManager.ts`（`snapWorld` 公开方法）
- `cp6.web/src/views/space/editor/FloorEditor.vue`（placement mousemove 幽灵跟随）

---

## Task 1: lasso OBB 相交判定（纯逻辑 ③-a）

**Files:**
- Modify: `cp6.web/src/space-editor/interact/collide/CollisionHint.ts`（export 三个原语）
- Create: `cp6.web/src/space-editor/interact/select/lassoHit.ts`
- Test: `cp6.web/src/space-editor/interact/select/lassoHit.spec.ts`

- [ ] **Step 1: 把 CollisionHint 的 SAT 原语 export 出来**

在 `CollisionHint.ts` 改三处（`Vec2`、`project`、`separated`）加 `export`：

```ts
// 顶部 Vec2
export interface Vec2 { x: number; y: number }
```
```ts
// project / separated 各加 export
export function project(points: Vec2[], axis: Vec2): [number, number] {
```
```ts
export function separated(a: [number, number], b: [number, number]): boolean {
```

（`rackCorners` 已是 export，无需改。函数体一律不动。）

- [ ] **Step 2: 写失败测试**

`lassoHit.spec.ts`：

```ts
import { describe, it, expect } from 'vitest'
import { obbIntersectsRect } from './lassoHit'
import { rackCorners } from '../collide/CollisionHint'
import type { RackVO } from '@/types/space/scene'

function rack(partial: Partial<RackVO>): RackVO {
  return {
    id: 'r', zoneId: 'z', floorId: 'f', rackCode: 'R',
    x: 0, y: 0, z: 0, rotationZ: 0,
    cols: 1, levels: 1, depthCount: 1, cellW: 1000, cellH: 1000, cellD: 1000,
    ...partial,
  }
}

describe('obbIntersectsRect', () => {
  it('轴对齐货架与重叠矩形相交', () => {
    const r = rack({ x: 0, y: 0 }) // OBB [0,0]~[1000,1000]
    expect(obbIntersectsRect(rackCorners(r), { minX: 500, minY: 500, maxX: 1500, maxY: 1500 })).toBe(true)
  })

  it('轴对齐货架与远处矩形不相交', () => {
    const r = rack({ x: 0, y: 0 })
    expect(obbIntersectsRect(rackCorners(r), { minX: 2000, minY: 2000, maxX: 3000, maxY: 3000 })).toBe(false)
  })

  it('矩形完全包含货架则相交', () => {
    const r = rack({ x: 100, y: 100 })
    expect(obbIntersectsRect(rackCorners(r), { minX: -100, minY: -100, maxX: 2000, maxY: 2000 })).toBe(true)
  })

  it('45°旋转货架：擦过其AABB角但不碰OBB → 不相交（AABB会误判）', () => {
    // 边长1000的方架绕锚点(0,0)转45°，OBB顶点在 (0,0),(707,707),(0,1414),(-707,707)
    // 其AABB为 x∈[-707,707], y∈[0,1414]。取一个落在AABB左上角、但在OBB外的小矩形。
    const r = rack({ x: 0, y: 0, rotationZ: 45 })
    const corners = rackCorners(r)
    // 角点 (-707,707) 附近左外侧的小框：与AABB重叠但与菱形OBB分离
    const rect = { minX: -707, minY: 0, maxX: -600, maxY: 100 }
    expect(obbIntersectsRect(corners, rect)).toBe(false)
  })

  it('45°旋转货架：矩形真实覆盖其中心 → 相交', () => {
    const r = rack({ x: 0, y: 0, rotationZ: 45 })
    const corners = rackCorners(r)
    // 几何中心约 (0,707)
    expect(obbIntersectsRect(corners, { minX: -50, minY: 650, maxX: 50, maxY: 760 })).toBe(true)
  })

  it('边缘紧贴视为分离（与碰撞口径一致）', () => {
    const r = rack({ x: 0, y: 0 }) // 右边界 x=1000
    expect(obbIntersectsRect(rackCorners(r), { minX: 1000, minY: 0, maxX: 2000, maxY: 1000 })).toBe(false)
  })
})
```

- [ ] **Step 3: 运行测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor/interact/select/lassoHit.spec.ts`
Expected: FAIL（`obbIntersectsRect` 未定义 / 模块不存在）

- [ ] **Step 4: 写实现**

`lassoHit.ts`：

```ts
// lassoHit — 旋转货架 OBB 与轴对齐框选矩形的 SAT 相交判定（SP2 ③）
// 纯逻辑，不引 Konva；复用 CollisionHint 的 SAT 原语
import { type Vec2, project, separated } from '../collide/CollisionHint'

/** 世界坐标轴对齐矩形（lasso 屏幕矩形经 screenToWorld 后） */
export interface WorldRect {
  minX: number
  minY: number
  maxX: number
  maxY: number
}

/**
 * 判断货架 OBB（4 世界角）与轴对齐世界矩形是否相交（SAT，触碰即真）。
 * 分离轴 = 矩形两法线(世界X/Y) + 货架两边法线。
 */
export function obbIntersectsRect(corners: Vec2[], rect: WorldRect): boolean {
  const rectCorners: Vec2[] = [
    { x: rect.minX, y: rect.minY },
    { x: rect.maxX, y: rect.minY },
    { x: rect.maxX, y: rect.maxY },
    { x: rect.minX, y: rect.maxY },
  ]
  const c0 = corners[0]!, c1 = corners[1]!, c3 = corners[3]!
  const e1: Vec2 = { x: c1.x - c0.x, y: c1.y - c0.y }
  const e2: Vec2 = { x: c3.x - c0.x, y: c3.y - c0.y }
  const axes: Vec2[] = [
    { x: 1, y: 0 },
    { x: 0, y: 1 },
    { x: -e1.y, y: e1.x },
    { x: -e2.y, y: e2.x },
  ]
  for (const axis of axes) {
    const a = project(corners, axis)
    const b = project(rectCorners, axis)
    if (separated(a, b)) return false
  }
  return true
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor/interact/select/lassoHit.spec.ts`
Expected: PASS（6 个用例全绿）

- [ ] **Step 6: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/interact/collide/CollisionHint.ts cp6.web/src/space-editor/interact/select/lassoHit.ts cp6.web/src/space-editor/interact/select/lassoHit.spec.ts && git commit -m "feat(space-sp2): lasso OBB-SAT 相交判定（③-a 纯逻辑）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: SelectTool 接入 OBB lasso（③-b）

**Files:**
- Modify: `cp6.web/src/space-editor/interact/tools/SelectTool.ts:72-105`（`onMouseUp` 改 OBB；删 `rectsOverlap` AABB 路径）

- [ ] **Step 1: 改 `onMouseUp` 用世界 OBB 判定**

把 `onMouseUp` 中"Collect racks within the lasso rect"一段（现用 `node.getClientRect` + `rectsOverlap`）替换为：把 lasso 屏幕矩形两角 `screenToWorld` 成世界轴对齐矩形，逐货架用 `obbIntersectsRect(rackCorners(rack), worldRect)`。

文件顶部 import 增加：

```ts
import { rackCorners } from '../collide/CollisionHint'
import { obbIntersectsRect, type WorldRect } from '../select/lassoHit'
```

`onMouseUp` 从 `// Collect racks within the lasso rect` 起到 `this.justLassoed = true` 前替换为：

```ts
    // Collect racks whose true OBB intersects the lasso rect (③ OBB, not AABB)
    const scene = this.ctx.store.scene
    if (!scene) return

    // lasso 屏幕矩形两角 → 世界轴对齐矩形（worldToScreen 无旋转，故世界仍轴对齐）
    const wA = this.ctx.stage.screenToWorld({ x: selX, y: selY })
    const wB = this.ctx.stage.screenToWorld({ x: selX + selW, y: selY + selH })
    const worldRect: WorldRect = {
      minX: Math.min(wA.x, wB.x),
      minY: Math.min(wA.y, wB.y),
      maxX: Math.max(wA.x, wB.x),
      maxY: Math.max(wA.y, wB.y),
    }

    const selected: string[] = []
    for (const rack of scene.racks) {
      if (obbIntersectsRect(rackCorners(rack), worldRect)) {
        selected.push(rack.id)
      }
    }
```

（其后 `this.ctx.store.setSelection(selected)` / `this.refreshTransformer()` / `this.justLassoed = true` 保留不动。）

- [ ] **Step 2: 删除不再使用的 `rectsOverlap` 私有方法**

删除 `SelectTool.ts` 末尾的 `private rectsOverlap(...) { ... }` 整段（OBB 路径不再需要 AABB-AABB 判定）。

- [ ] **Step 3: 类型门**

Run: `cd /d/CP6-space-backend/cp6.web && npm run type-check`
Expected: 0 error（无未用变量 / 无 `rectsOverlap` 残引用）

- [ ] **Step 4: 跑既有相关单测确认无回归**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor`
Expected: PASS（含 Task 1 新增 + 既有 space-editor 单测）

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/interact/tools/SelectTool.ts && git commit -m "feat(space-sp2): SelectTool lasso 用真实 OBB 替 AABB（③-b 接入）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: 旋转纯逻辑 rotateAboutCenter + snapAngle（①②-pure）

**Files:**
- Create: `cp6.web/src/space-editor/interact/rotate/rotateGeometry.ts`
- Test: `cp6.web/src/space-editor/interact/rotate/rotateGeometry.spec.ts`

> 说明：`snapAngle` 现内联在 `RotateTool.ts`，本任务把它迁到 `rotateGeometry.ts` 与 `rotateAboutCenter` 同处（旋转纯逻辑聚合、可独立测）。`RotateTool` 改 import 在 Task 5。

- [ ] **Step 1: 写失败测试**

`rotateGeometry.spec.ts`：

```ts
import { describe, it, expect } from 'vitest'
import { rotateAboutCenter, snapAngle } from './rotateGeometry'

const SQUARE = { cols: 1, cellW: 1000, depthCount: 1, cellD: 1000 }

describe('rotateAboutCenter', () => {
  it('0°→90° 保持几何中心不变（锚点随之位移）', () => {
    const rack = { x: 0, y: 0, rotationZ: 0, ...SQUARE }
    // 原中心 (500,500)。绕中心转90°后新锚点应为 (1000,0)
    const a = rotateAboutCenter(rack, 90)
    expect(a.x).toBeCloseTo(1000, 6)
    expect(a.y).toBeCloseTo(0, 6)
  })

  it('几何中心在旋转前后一致（不变量）', () => {
    const rack = { x: 137, y: -42, rotationZ: 23, cols: 4, cellW: 1000, depthCount: 2, cellD: 800 }
    const W = rack.cols * rack.cellW, D = rack.depthCount * rack.cellD
    const center = (x: number, y: number, deg: number) => {
      const th = (deg * Math.PI) / 180, cos = Math.cos(th), sin = Math.sin(th)
      return { x: x + (W / 2) * cos - (D / 2) * sin, y: y + (W / 2) * sin + (D / 2) * cos }
    }
    const c0 = center(rack.x, rack.y, rack.rotationZ)
    const a = rotateAboutCenter(rack, 137)
    const c1 = center(a.x, a.y, 137)
    expect(c1.x).toBeCloseTo(c0.x, 6)
    expect(c1.y).toBeCloseTo(c0.y, 6)
  })

  it('角度不变则锚点不变', () => {
    const rack = { x: 50, y: 60, rotationZ: 30, ...SQUARE }
    const a = rotateAboutCenter(rack, 30)
    expect(a.x).toBeCloseTo(50, 6)
    expect(a.y).toBeCloseTo(60, 6)
  })
})

describe('snapAngle', () => {
  it('阈内吸附到 15° 倍数', () => {
    expect(snapAngle(14)).toBe(15)
    expect(snapAngle(31)).toBe(30)
    expect(snapAngle(2)).toBe(0)
  })
  it('阈外保持原角', () => {
    expect(snapAngle(20)).toBe(20)   // 距 15/30 均 >3
    expect(snapAngle(37)).toBe(37)
  })
  it('358° 环绕吸附到 0', () => {
    expect(snapAngle(358)).toBe(0)
  })
  it('负角规范化', () => {
    expect(snapAngle(-1)).toBe(0)
    expect(snapAngle(-46)).toBe(315)  // -46 → 314 → 阈内 315
  })
})
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor/interact/rotate/rotateGeometry.spec.ts`
Expected: FAIL（模块不存在）

- [ ] **Step 3: 写实现**

`rotateGeometry.ts`：

```ts
// rotateGeometry — 旋转纯逻辑（SP2 ①②）：绕几何中心回算锚点 + 角度吸附
// 不引 Konva。坐标约定与 CollisionHint.rackCorners / coords.computeAbs 一致：
//   x' = x·cosθ − y·sinθ ; y' = x·sinθ + y·cosθ（θ = rotationZ，世界坐标 mm）

const SNAP_STEP = 15      // 度
const SNAP_THRESHOLD = 3  // ±度

interface RackPoseDims {
  x: number
  y: number
  rotationZ: number
  cols: number
  cellW: number
  depthCount: number
  cellD: number
}

function rotateVec(deg: number, px: number, py: number): { x: number; y: number } {
  const th = (deg * Math.PI) / 180
  const cos = Math.cos(th), sin = Math.sin(th)
  return { x: px * cos - py * sin, y: px * sin + py * cos }
}

/**
 * 保持货架几何中心不变，把 rotationZ 改为 newRotationZ，返回新锚点 (x,y)。
 * C = anchor + R(rotationZ)·(W/2,D/2) ；anchor' = C − R(newRotationZ)·(W/2,D/2)
 */
export function rotateAboutCenter(rack: RackPoseDims, newRotationZ: number): { x: number; y: number } {
  const W = rack.cols * rack.cellW
  const D = rack.depthCount * rack.cellD
  const r0 = rotateVec(rack.rotationZ, W / 2, D / 2)
  const cx = rack.x + r0.x, cy = rack.y + r0.y
  const r1 = rotateVec(newRotationZ, W / 2, D / 2)
  return { x: cx - r1.x, y: cy - r1.y }
}

/** 把角度吸附到 15° 倍数（±3° 阈内，含环绕），返回 [0,360) 规范化角。 */
export function snapAngle(deg: number): number {
  const normalized = ((deg % 360) + 360) % 360
  const nearest = Math.round(normalized / SNAP_STEP) * SNAP_STEP
  const delta = Math.abs(normalized - nearest)
  if (delta <= SNAP_THRESHOLD || delta >= 360 - SNAP_THRESHOLD) {
    return nearest % 360
  }
  return normalized
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor/interact/rotate/rotateGeometry.spec.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/interact/rotate/rotateGeometry.ts cp6.web/src/space-editor/interact/rotate/rotateGeometry.spec.ts && git commit -m "feat(space-sp2): rotateAboutCenter + snapAngle 旋转纯逻辑（①②）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: RotateRackCmd 升级为位姿命令（①-cmd）

**Files:**
- Modify: `cp6.web/src/space-editor/command/commands/RotateRackCmd.ts`（整文件重写）
- Modify: `cp6.web/src/space-editor/command/commands.spec.ts:78-94`（RotateRackCmd 用例改新签名）

- [ ] **Step 1: 改测试为新签名（先写失败测试）**

把 `commands.spec.ts` 的 `describe('RotateRackCmd', ...)` 整段替换为：

```ts
// ─── RotateRackCmd ────────────────────────────────────────────────────────────
describe('RotateRackCmd', () => {
  it('do 设位姿到 to（x/y/rotationZ 三值齐改）', () => {
    const scene = makeScene([{ id: 'r1', x: 0, y: 0, rotationZ: 0 }])
    const ctx = makeCtx(scene)
    new RotateRackCmd('r1', { x: 0, y: 0, rotationZ: 0 }, { x: 100, y: 50, rotationZ: 90 }).do(ctx)
    expect(scene.racks[0]!.x).toBe(100)
    expect(scene.racks[0]!.y).toBe(50)
    expect(scene.racks[0]!.rotationZ).toBe(90)
    expect(ctx.dirtyIds).toContain('r1')
  })

  it('undo 回 from（三值齐还原）', () => {
    const scene = makeScene([{ id: 'r1', x: 100, y: 50, rotationZ: 90 }])
    const ctx = makeCtx(scene)
    new RotateRackCmd('r1', { x: 0, y: 0, rotationZ: 0 }, { x: 100, y: 50, rotationZ: 90 }).undo(ctx)
    expect(scene.racks[0]!.x).toBe(0)
    expect(scene.racks[0]!.y).toBe(0)
    expect(scene.racks[0]!.rotationZ).toBe(0)
  })
})
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor/command/commands.spec.ts`
Expected: FAIL（旧 `RotateRackCmd` 构造签名为 `(id, number, number)`，类型/断言不符）

- [ ] **Step 3: 重写 RotateRackCmd**

`RotateRackCmd.ts` 整文件：

```ts
import type { Command, EditorContext } from '../Command'

export interface RackPose {
  x: number
  y: number
  rotationZ: number
}

export class RotateRackCmd implements Command {
  label = 'RotateRack'

  constructor(
    private rackId: string,
    private from: RackPose,
    private to: RackPose,
  ) {}

  do(ctx: EditorContext): void {
    const rack = ctx.scene.racks.find(r => r.id === this.rackId)
    if (!rack) return
    rack.x = this.to.x
    rack.y = this.to.y
    rack.rotationZ = this.to.rotationZ
    ctx.markDirty(this.rackId)
  }

  undo(ctx: EditorContext): void {
    const rack = ctx.scene.racks.find(r => r.id === this.rackId)
    if (!rack) return
    rack.x = this.from.x
    rack.y = this.from.y
    rack.rotationZ = this.from.rotationZ
    ctx.markDirty(this.rackId)
  }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor/command/commands.spec.ts`
Expected: PASS

> 注：此刻 `RotateTool.ts` 仍用旧签名 `new RotateRackCmd(id, fromDeg, newRotationZ)`，类型门会报错——由 Task 5 修复。本任务**不跑** `npm run type-check`，仅跑本 spec。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/command/commands/RotateRackCmd.ts cp6.web/src/space-editor/command/commands.spec.ts && git commit -m "feat(space-sp2): RotateRackCmd 升级为位姿命令（x/y/rotationZ 三值）①

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: RotateTool 中心枢轴提交 + 角度读数（①②-tool）

**Files:**
- Modify: `cp6.web/src/space-editor/interact/tools/RotateTool.ts`（整文件重写）

**实现要点：** 保留 Konva Transformer 旋转（其单节点枢轴=包围盒中心=货架几何中心）。`onTransformStart` 捕完整 from 位姿；`transform`（实时）更新角度读数 Text + 吸附变绿（不改节点，避免与 Transformer 内部状态打架）；`onTransformEnd` 用 `snapAngle` 定最终角、`rotateAboutCenter(fromRack, θ)` 回算锚点，提交位姿命令。单选旋转（`onClick` 维持单选）。

- [ ] **Step 1: 整文件重写 RotateTool**

`RotateTool.ts`：

```ts
// RotateTool — 旋转货架（Konva Transformer + 中心枢轴提交，SP2 ①②）
// 枢轴：Konva Transformer 对单节点绕包围盒中心旋转；rack group 的包围盒中心 == 货架几何中心，
//   故实时预览本就绕几何中心。提交时用 rotateAboutCenter 回算锚点，使最终渲染==预览（消跳变）。
// 角度吸附：snapAngle（15° 倍数 / ±3°）；按住 Ctrl 关吸附。旋转中显示角度读数，吸附时变绿。
import Konva from 'konva'
import type { ITool, ToolContext } from '../InteractionManager'
import { findRackGroup, isTransformerNode } from '../InteractionManager'
import { RotateRackCmd, type RackPose } from '../../command/commands/RotateRackCmd'
import { rotateAboutCenter, snapAngle } from '../rotate/rotateGeometry'
import type { RackVO } from '@/types/space/scene'

export class RotateTool implements ITool {
  private ctx: ToolContext
  // 旋转起始时的 from 位姿（单选）
  private fromPose: RackPose | null = null
  private fromRack: RackVO | null = null
  private angleText: Konva.Text | null = null

  constructor(ctx: ToolContext) {
    this.ctx = ctx
  }

  onActivate(): void {
    this.ctx.transformer.rotateEnabled(true)
    this.ctx.transformer.resizeEnabled(false)
    this.ctx.transformer.enabledAnchors([])
    this.refreshTransformer()
    this.ctx.transformer.on('transformstart.rt', () => { this.onTransformStart() })
    this.ctx.transformer.on('transform.rt', () => { this.onTransform() })
    this.ctx.transformer.on('transformend.rt', () => { this.onTransformEnd() })
  }

  onDeactivate(): void {
    this.ctx.transformer.off('transformstart.rt')
    this.ctx.transformer.off('transform.rt')
    this.ctx.transformer.off('transformend.rt')
    this.ctx.transformer.rotateEnabled(false)
    this.ctx.transformer.nodes([])
    this.clearAngleText()
    this.ctx.stage.layers.rack.batchDraw()
    this.fromPose = null
    this.fromRack = null
  }

  onEscape(): void {
    // InteractionManager.escape() 随后清选区
    this.clearAngleText()
  }

  onClick(e: Konva.KonvaEventObject<MouseEvent>): void {
    if (isTransformerNode(e.target)) return
    const rackGroup = findRackGroup(e.target)
    if (rackGroup) {
      this.ctx.store.setSelection([rackGroup.id()])  // 单选旋转
    } else {
      this.ctx.store.clearSelection()
    }
    this.refreshTransformer()
  }

  private onTransformStart(): void {
    this.fromPose = null
    this.fromRack = null
    const scene = this.ctx.store.scene
    if (!scene) return
    const id = this.ctx.store.selectionIds[0]
    if (!id) return
    const rack = scene.racks.find((r) => r.id === id)
    if (!rack) return
    this.fromRack = { ...rack }
    this.fromPose = { x: rack.x, y: rack.y, rotationZ: rack.rotationZ }
  }

  private onTransform(): void {
    if (!this.fromRack) return
    const node = this.ctx.transformer.nodes()[0] as Konva.Group | undefined
    if (!node) return
    const rawZ = this.normalize(-node.rotation())
    const snapped = !this.ctx.ctrlHeld()
    const shownZ = snapped ? snapAngle(rawZ) : rawZ
    const isSnapped = snapped && Math.round(shownZ) % 15 === 0
    this.drawAngleText(node, shownZ, isSnapped)
  }

  private onTransformEnd(): void {
    const node = this.ctx.transformer.nodes()[0] as Konva.Group | undefined
    this.clearAngleText()
    if (!node || !this.fromPose || !this.fromRack) return

    const rawZ = this.normalize(-node.rotation())
    const toZ = this.normalize(this.ctx.ctrlHeld() ? rawZ : snapAngle(rawZ))
    const anchor = rotateAboutCenter({ ...this.fromRack, rotationZ: this.fromRack.rotationZ }, toZ)
    const to: RackPose = { x: anchor.x, y: anchor.y, rotationZ: toZ }

    const id = this.fromRack.id
    const cmd = new RotateRackCmd(id, this.fromPose, to)
    this.ctx.store.stack.exec(cmd, this.ctx.store.buildEditorContext())
    this.ctx.store.updateUndoRedo()

    this.fromPose = null
    this.fromRack = null
    this.ctx.afterCommand()
  }

  private drawAngleText(node: Konva.Group, deg: number, snapped: boolean): void {
    const box = node.getClientRect({ relativeTo: this.ctx.stage.stage })
    const cx = box.x + box.width / 2
    const top = box.y - 22
    if (!this.angleText) {
      this.angleText = new Konva.Text({
        text: '', fontSize: 13, fontStyle: 'bold', listening: false,
      })
      this.ctx.stage.layers.ghost.add(this.angleText)
    }
    this.angleText.text(`${Math.round(deg)}°`)
    this.angleText.fill(snapped ? '#1aab4a' : '#333')
    this.angleText.position({ x: cx - 12, y: top })
    this.ctx.stage.layers.ghost.batchDraw()
  }

  private clearAngleText(): void {
    if (this.angleText) {
      this.angleText.destroy()
      this.angleText = null
      this.ctx.stage.layers.ghost.batchDraw()
    }
  }

  private normalize(deg: number): number {
    return ((deg % 360) + 360) % 360
  }

  private refreshTransformer(): void {
    const nodes = this.ctx.store.selectionIds
      .map((id: string) => this.ctx.stage.getRackNode(id))
      .filter((n): n is Konva.Group => n !== null)
    this.ctx.transformer.nodes(nodes)
    this.ctx.stage.layers.rack.batchDraw()
  }
}
```

- [ ] **Step 2: 类型门**

Run: `cd /d/CP6-space-backend/cp6.web && npm run type-check`
Expected: 0 error（RotateTool 新签名与 RotateRackCmd 对齐；`snapAngle` 不再内联）

- [ ] **Step 3: 跑 space-editor 全量单测确认无回归**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor`
Expected: PASS（lassoHit + rotateGeometry + commands + 既有）

- [ ] **Step 4: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/interact/tools/RotateTool.ts && git commit -m "feat(space-sp2): RotateTool 中心枢轴提交 + 实时角度读数（①②）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: 阵列外包尺寸 arrayFootprint（纯逻辑 ④-pure）

**Files:**
- Create: `cp6.web/src/space-editor/generate/arrayFootprint.ts`
- Test: `cp6.web/src/space-editor/generate/arrayFootprint.spec.ts`

> 步长须与 `genZoneArray` 一致：`rackWidth = cols*cellW`，`rackDepth = depthCount*cellD`；
> 总宽 `racksPerRow*rackWidth + (racksPerRow-1)*rackGap`；总深 `rows*rackDepth + (rows-1)*rowGap`。

- [ ] **Step 1: 写失败测试**

`arrayFootprint.spec.ts`：

```ts
import { describe, it, expect } from 'vitest'
import { arrayFootprint } from './arrayFootprint'

const TPL = { cols: 4, cellW: 1000, depthCount: 1, cellD: 800 }

describe('arrayFootprint', () => {
  it('1×1 = 单架尺寸', () => {
    const f = arrayFootprint(TPL, { rows: 1, racksPerRow: 1, rowGap: 2000, rackGap: 1000 })
    expect(f.w).toBe(4000)  // 4*1000
    expect(f.d).toBe(800)   // 1*800
  })

  it('rows×racksPerRow 含间隙累加', () => {
    const f = arrayFootprint(TPL, { rows: 3, racksPerRow: 2, rowGap: 2000, rackGap: 1000 })
    // 宽 = 2*4000 + 1*1000 = 9000；深 = 3*800 + 2*2000 = 6400
    expect(f.w).toBe(9000)
    expect(f.d).toBe(6400)
  })

  it('与 genZoneArray 末架终点一致', () => {
    const params = { rows: 2, racksPerRow: 3, rowGap: 1500, rackGap: 500 }
    const f = arrayFootprint(TPL, params)
    const rackWidth = TPL.cols * TPL.cellW, rackDepth = TPL.depthCount * TPL.cellD
    const lastX = (params.racksPerRow - 1) * (rackWidth + params.rackGap) + rackWidth
    const lastY = (params.rows - 1) * (rackDepth + params.rowGap) + rackDepth
    expect(f.w).toBe(lastX)
    expect(f.d).toBe(lastY)
  })
})
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor/generate/arrayFootprint.spec.ts`
Expected: FAIL（模块不存在）

- [ ] **Step 3: 写实现**

`arrayFootprint.ts`：

```ts
// arrayFootprint — 整阵列外包尺寸（SP2 ④幽灵预览用），步长对齐 genZoneArray
// 纯逻辑：单架 W=cols*cellW / D=depthCount*cellD；行内含 rackGap、行间含 rowGap。

interface FootprintTpl {
  cols: number
  cellW: number
  depthCount: number
  cellD: number
}

interface FootprintParams {
  rows: number
  racksPerRow: number
  rowGap: number
  rackGap: number
}

/** 返回未旋转阵列的外包尺寸 {w,d}（mm），原点在阵列锚点角。 */
export function arrayFootprint(tpl: FootprintTpl, params: FootprintParams): { w: number; d: number } {
  const rackWidth = tpl.cols * tpl.cellW
  const rackDepth = tpl.depthCount * tpl.cellD
  const w = params.racksPerRow * rackWidth + (params.racksPerRow - 1) * params.rackGap
  const d = params.rows * rackDepth + (params.rows - 1) * params.rowGap
  return { w, d }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-editor/generate/arrayFootprint.spec.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/generate/arrayFootprint.ts cp6.web/src/space-editor/generate/arrayFootprint.spec.ts && git commit -m "feat(space-sp2): arrayFootprint 阵列外包尺寸纯逻辑（④）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: SceneStage 幽灵 API showFootprintGhost（④-render）

**Files:**
- Modify: `cp6.web/src/space-editor/SceneStage.ts`（在 `showGhost`/`hideGhost` 附近加 `showFootprintGhost`）

- [ ] **Step 1: 加 `showFootprintGhost` 方法**

在 `SceneStage.ts` 的 `hideGhost()` 方法之后插入：

```ts
  /**
   * 在 originWorld 处画 w×d（mm）外包矩形幽灵，valid 决定绿/琥珀着色（SP2 ④）。
   * 与 renderRack 同向（矩形向屏幕上方延伸 dPx）。
   */
  showFootprintGhost(originWorld: XY, w: number, d: number, valid: boolean): void {
    this.layers.ghost.destroyChildren()
    const origin = worldToScreen(originWorld, this.view)
    const wPx = w * this.view.zoom
    const dPx = d * this.view.zoom
    const rect = new Konva.Rect({
      x: origin.x,
      y: origin.y - dPx,
      width: wPx,
      height: dPx,
      fill: valid ? 'rgba(80,200,120,0.30)' : 'rgba(255,170,0,0.25)',
      stroke: valid ? '#40cc70' : '#ffaa00',
      strokeWidth: 2,
      dash: [6, 4],
      listening: false,
    })
    this.layers.ghost.add(rect)
    this.layers.ghost.batchDraw()
  }
```

- [ ] **Step 2: 类型门**

Run: `cd /d/CP6-space-backend/cp6.web && npm run type-check`
Expected: 0 error

- [ ] **Step 3: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/SceneStage.ts && git commit -m "feat(space-sp2): SceneStage.showFootprintGhost 外包矩形幽灵（④）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: FloorEditor 放置幽灵跟随光标（④-wire）

**Files:**
- Modify: `cp6.web/src/space-editor/interact/InteractionManager.ts`（加公开 `snapWorld`）
- Modify: `cp6.web/src/views/space/editor/FloorEditor.vue`（placement mousemove 幽灵跟随）

- [ ] **Step 1: InteractionManager 暴露 snapWorld**

在 `InteractionManager` 类内（如 `selectAll()` 之后）加：

```ts
  /** 对世界坐标点做吸附（供放置幽灵跟随用）。IM 禁用时仍可调用（纯计算）。 */
  snapWorld(point: { x: number; y: number }): { x: number; y: number; snapped: boolean } {
    const scene = this.ctx.store.scene
    if (!scene) return { ...point, snapped: false }
    return this.ctx.snap.snap(point, {
      zoom: this.ctx.stage.view.zoom,
      racks: scene.racks,
      aisles: scene.aisles,
    })
  }
```

- [ ] **Step 2: FloorEditor 引入幽灵跟随**

`FloorEditor.vue` `<script setup>` 顶部 import 增加：

```ts
import { arrayFootprint } from '@/space-editor/generate/arrayFootprint'
import { pointInPolygon } from '@/space-editor/interact/collide/CollisionHint'
```

在 `bindStageClick()` 函数之后新增 `bindPlacementGhost()` + 合法性助手，并在 `onTemplateSelect` / `exitPlacementMode` 中开关 mousemove：

```ts
// ── Placement ghost follow (SP2 ④) ──────────────────────────────────────────

function placementValid(originX: number, originY: number, w: number, d: number): boolean {
  if (!selectedZoneId.value) return false
  const zone = store.scene?.zones.find(z => z.id === selectedZoneId.value)
  if (!zone) return false
  let poly: [number, number][]
  try { poly = JSON.parse(zone.polygon) as [number, number][] } catch { return false }
  const corners: [number, number][] = [
    [originX, originY], [originX + w, originY],
    [originX + w, originY + d], [originX, originY + d],
  ]
  return corners.every(([cx, cy]) => pointInPolygon(cx, cy, poly))
}

function onPlacementMove(): void {
  if (!placementMode.value || !pendingSel.value || !stageRef) return
  const ptr = stageRef.stage.getPointerPosition()
  if (!ptr) return
  const raw = stageRef.screenToWorld(ptr)
  const snapped = imRef.value?.snapWorld(raw) ?? { x: raw.x, y: raw.y }
  const sel = pendingSel.value
  const { w, d } = arrayFootprint(sel.template, sel.arrayParams)
  const valid = placementValid(snapped.x, snapped.y, w, d)
  stageRef.showFootprintGhost({ x: snapped.x, y: snapped.y }, w, d, valid)
}

function bindPlacementGhost(): void {
  stageRef?.stage.on('mousemove.place', onPlacementMove)
}

function unbindPlacementGhost(): void {
  stageRef?.stage.off('mousemove.place')
}
```

- [ ] **Step 3: 在进入/退出放置模式时开关跟随**

在 `onTemplateSelect`（`ElMessage.info(...)` 之前或之后）末尾加 `bindPlacementGhost()`：

```ts
function onTemplateSelect(sel: TemplatePanelSelection): void {
  pendingSel.value = sel
  placementMode.value = true
  imRef.value?.setEnabled(false)
  bindPlacementGhost()
  ElMessage.info(t('移动到画布，单击放置货架'))
}
```

在 `exitPlacementMode` 内 `stageRef?.hideGhost()` 之前加 `unbindPlacementGhost()`：

```ts
function exitPlacementMode(): void {
  placementMode.value = false
  unbindPlacementGhost()
  stageRef?.hideGhost()
  imRef.value?.setEnabled(true)
}
```

（`exitPlacementMode` 现有其余行不动；若该函数体在 150 行后被截断，仅在 `hideGhost` 前插入 `unbindPlacementGhost()` 一行、并保留其余逻辑。）

- [ ] **Step 4: 类型门 + 构建门**

Run: `cd /d/CP6-space-backend/cp6.web && npm run type-check`
Expected: 0 error

Run: `cd /d/CP6-space-backend/cp6.web && npm run build`
Expected: 构建成功（type-check + vite build 双绿）

- [ ] **Step 5: 全量前端单测**

Run: `cd /d/CP6-space-backend/cp6.web && npm run test`
Expected: PASS（既有 + SP2 新增 lassoHit/rotateGeometry/arrayFootprint/commands）

- [ ] **Step 6: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-editor/interact/InteractionManager.ts cp6.web/src/views/space/editor/FloorEditor.vue && git commit -m "feat(space-sp2): 放置模式幽灵预览跟随光标 + 区内/外着色（④）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 9: 集中运行态 QA（gstack / Playwright）

**Files:**
- Create: `docs/superpowers/qa/space-p3-sp2/`（截图 + 步骤记录）

**前置环境（沿用 SP1）：** 后端 5177（worktree `appsettings.Local.json`→`CP6DB_SpaceQA`，无 RabbitMQ 稳跑）；前端 5173（`npm run dev`）；登录 admin/123456；路由 `/space/editor/{floorId}`，Floor `5C92E6A8…`；真实编码 `A-01-01-01…A-01-02-02`。坑：el-input 用 `pressSequentially` 非 `.fill()`；GUID 比较大小写无关；冷后端首调 Space 端点 ~5-6s JIT。

- [ ] **Step 1: 三门复核**

```
cd /d/CP6-space-backend/cp6.web && npm run type-check
cd /d/CP6-space-backend/cp6.web && npm run test
cd /d/CP6-space-backend/cp6.web && npm run build
```
Expected: 全绿。

- [ ] **Step 2: 起真栈**

后端：`cd /d/CP6-space-backend && (后台) dotnet run --project CP6.WebApi`（监听 5177，读 `appsettings.Local.json`）。
前端：`cd /d/CP6-space-backend/cp6.web && (后台) npm run dev`（5173）。
gstack 浏览器登录 admin/123456，进 `/space/editor/{Floor 5C92E6A8…}`。

- [ ] **Step 3: 验收点①旋转（gstack 截图）**

切 rotate 工具，单选一货架，抓 Transformer 旋转手柄拖动：
- 货架**绕几何中心原地转**，松手**无大跳变**（仅 ≤3° 吸附settle）。
- 旋转中顶部出现角度读数；接近 15° 倍数时读数变绿。
- 按住 Ctrl 拖动 → 自由角不吸附。
- Ctrl+Z → 位姿回原。
截图存档。

- [ ] **Step 4: 验收点③ lasso（gstack 截图）**

先把一货架旋转 ~45°；切 select 工具，用一个**刚擦过其 AABB 角、不碰真实 OBB** 的橡皮筋框 → 该货架**不被选中**（旧 AABB 会误选）；再用真实覆盖其本体的框 → 选中。截图存档。

- [ ] **Step 5: 验收点④幽灵（gstack 截图）**

模板面板选模板设阵列参数→「点击画布放置」：
- 外包矩形幽灵**跟随光标**移动。
- 未选库区 / 落点出库区 → 幽灵**琥珀**；落在库区内 → **绿**。
- 单击落点生成阵列位置正确；Esc 取消并清除幽灵。
截图存档。

- [ ] **Step 6: 无回归抽查**

拖拽移动货架 / 碰撞着色（红/黄）/ 绑码入口 / 保存（POST scene）正常。

- [ ] **Step 7: 固化 QA 证据 + Commit**

把步骤记录 + 截图写入 `docs/superpowers/qa/space-p3-sp2/`。

```bash
cd /d/CP6-space-backend && git add docs/superpowers/qa/space-p3-sp2 && git commit -m "test(space-sp2): 编辑器交互运行态 gstack QA 固化（旋转/lasso/幽灵）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review（写完即查）

**Spec 覆盖：**
- §2 ① 中心枢轴 → Task 3（`rotateAboutCenter`）+ Task 4（位姿命令）+ Task 5（Tool 提交）✓
- §3 ② 角度反馈 → Task 3（`snapAngle` 纯逻辑测）+ Task 5（读数 Text + 吸附绿）✓
- §4 ③ lasso OBB → Task 1（`obbIntersectsRect`）+ Task 2（SelectTool 接入）✓
- §5 ④ 幽灵跟随 → Task 6（`arrayFootprint`）+ Task 7（`showFootprintGhost`）+ Task 8（FloorEditor 接线）✓
- §6 测试 → 各 Task 内 vitest + Task 9 集中 gstack ✓
- §7 文件清单 → 与本计划文件清单一致 ✓

**偏离说明（须在执行/交付时告知用户）：** spec §2.3 写「自定义旋转手柄替掉 Konva Transformer」，本计划改为**保留 Konva Transformer**（实测其单节点旋转枢轴=包围盒中心=货架几何中心，已满足中心枢轴/消跳变/吸附/读数全部意图，风险更低）。功能意图无损。

**占位扫描：** 无 TBD/TODO；每个代码步骤均含完整代码。✓

**类型一致性：** `RackPose{x,y,rotationZ}` 在 Task 4 定义、Task 5 引用一致；`WorldRect` 在 Task 1 定义、Task 2 引用一致；`obbIntersectsRect`/`rackCorners`/`project`/`separated`/`snapAngle`/`rotateAboutCenter`/`arrayFootprint`/`showFootprintGhost`/`snapWorld`/`pointInPolygon` 全部签名前后一致。✓

**已知运行态校准点（留 Task 9 gstack）：** 旋转角度的屏幕↔世界方向号（`-node.rotation()`，与渲染 `rotation:-rotationZ` 自洽）；角度读数定位（用 `getClientRect` bbox 顶部，缩放/平移下观感）；幽灵在大楼层的可见度。
