# Space P3 · SP3 拣货路径规划做真 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把拣货路径 planner 做真 —— A 真交叉口图（修连通性正确性）+ B 重排对比（what-if 优化洞察）+ C A\*（drop-in 加速），纯前端零后端。

**Architecture:** 升级 `cp6.web/src/space-viewer/advanced/` 三处：(A) `buildCenterlineGraph` 两阶段插交叉口（新 `segmentIntersect.ts` 求交拆段）；(C) `dijkstra`→`astar`（欧氏启发式，drop-in）；(B) 新 `routeOptimize.ts`（仅矩阵→顺序）+ `PickPathPlanner.planPickComparison`（单次建图 + actual/optimized baseline 兜底）+ `PathAnimator.setComparisonPath`（绿静态对比线）+ `AdvancedPanel`/`FloorViewer` 接线。

**Tech Stack:** Vue 3.5 + TS + vitest + three（仅 PathAnimator 触 three）。纯逻辑 vitest 当场绿；画布运行态留 Task 8 gstack。

**关键铁律（来自 spec v1.1 §4.5）：**
- 单位：`actualMm/optimizedMm` 一律 mm；UI 层 `/1000` 显示米——planner 层不换算。
- 优化序以 actual（LineNo 原序）为 baseline 兜底：NN+2opt 后若不更短则回退原序 → **强保证 `optimizedMm ≤ actualMm`、`savingsPct ≥ 0`**。
- `routeOptimize.ts` 只 `import type`（不引 planner 运行时函数，避免循环依赖）；`distanceMatrixFromGraph` 留在 `PickPathPlanner`（需 `Graph`/`pathBetween`）。
- 线段相交 `eps` 按段长折算（`denom` 单位 mm²）；共线端点贴合用 `pointOnSegment` 兜。
- i18n：面板文本一律 `t()` plain Chinese（本仓 `missingWarn:false`，无警告，零新键）；**不引 `el-switch`**，开关用本面板既有 `.ap-check` 复选框样式。
- 单次建图：`planPickComparison` 内 `buildCenterlineGraph` 只调一次。

**spec:** `docs/superpowers/specs/2026-06-29-space-p3-sp3-pathfinding-design.md`（v1.1）

---

## File Structure

**新增：**
- `cp6.web/src/space-viewer/advanced/segmentIntersect.ts` —— 纯几何：`pointOnSegment` / `segSegIntersection` / `splitPointsOnSegment`。无 three/Konva 依赖。
- `cp6.web/src/space-viewer/advanced/segmentIntersect.spec.ts`
- `cp6.web/src/space-viewer/advanced/routeOptimize.ts` —— 纯矩阵优化：`routeLengthByOrder` / `optimizeOrder`。**仅 `import type`，无运行时依赖。**
- `cp6.web/src/space-viewer/advanced/routeOptimize.spec.ts`

**改动：**
- `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts` —— `buildCenterlineGraph` 插交叉口 / `dijkstra`→`astar(adj,start,end,nodePt)`（导出供测）/ 新增 `distanceMatrixFromGraph` + 内部 `planPickRouteOnGraph` + `planPickComparison`+`PickComparison`+`degradedPairCount`；`planPickRoute` 改为薄包装不破旧签名。
- `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts` —— 新增交叉口连通 / astar / distanceMatrix / planPickComparison 用例。
- `cp6.web/src/space-viewer/advanced/PathAnimator.ts` —— 新增 `setComparisonPath(points|null)`；`clear` 复位 `_compareLine`。
- `cp6.web/src/space-viewer/advanced/PathAnimator.spec.ts` —— 新增对比线增删用例。
- `cp6.web/src/views/space/viewer/AdvancedPanel.vue` —— 新增 `compareInfo` / `showOptimized` props + `toggle-optimized` emit + `.ap-check` 复选框。
- `cp6.web/src/views/space/viewer/FloorViewer.vue` —— `onLoadPath` 改用 `planPickComparison`（stops 按 seq 排序）+ `comparison`/`showOptimized`/`compareInfo` 状态 + `onToggleOptimized` + 切层复位。

**QA：**
- `docs/superpowers/qa/space-p3-sp3/`（README + 截图 + `seed.sql`）。

> 所有命令在 worktree 跑：bash cwd 每次重置回 `D:\CP6`，故命令前缀 `cd /d/CP6-space-backend && ...`。前端命令在 `cp6.web/` 下：`cd /d/CP6-space-backend/cp6.web && ...`。

---

## Task 1 (A-1): `segmentIntersect.ts` 纯几何

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/segmentIntersect.ts`
- Test: `cp6.web/src/space-viewer/advanced/segmentIntersect.spec.ts`

- [ ] **Step 1: Write the failing test**

Create `cp6.web/src/space-viewer/advanced/segmentIntersect.spec.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { pointOnSegment, segSegIntersection, splitPointsOnSegment } from './segmentIntersect'

const near = (p: { x: number; y: number } | null, x: number, y: number) => {
  expect(p).not.toBeNull()
  expect(p!.x).toBeCloseTo(x, 3)
  expect(p!.y).toBeCloseTo(y, 3)
}

describe('pointOnSegment', () => {
  const a = { x: 0, y: 0 }, b = { x: 100, y: 0 }
  it('endpoints and midpoint are on', () => {
    expect(pointOnSegment(a, a, b)).toBe(true)
    expect(pointOnSegment(b, a, b)).toBe(true)
    expect(pointOnSegment({ x: 50, y: 0 }, a, b)).toBe(true)
  })
  it('off-segment by perpendicular distance is not on', () => {
    expect(pointOnSegment({ x: 50, y: 10 }, a, b)).toBe(false)
  })
  it('beyond the extent (on the line) is not on', () => {
    expect(pointOnSegment({ x: 150, y: 0 }, a, b)).toBe(false)
  })
  it('degenerate point segment matches only within eps', () => {
    expect(pointOnSegment({ x: 0, y: 0 }, a, a)).toBe(true)
    expect(pointOnSegment({ x: 5, y: 0 }, a, a)).toBe(false)
  })
})

describe('segSegIntersection', () => {
  it('cross at midpoint', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 50, y: -50 }, { x: 50, y: 50 }), 50, 0)
  })
  it('T-junction: endpoint of seg2 on seg1', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 50, y: 0 }, { x: 50, y: 50 }), 50, 0)
  })
  it('intersection exactly at an endpoint', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }), 100, 0)
  })
  it('parallel separated -> null', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 0, y: 50 }, { x: 100, y: 50 })).toBeNull()
  })
  it('collinear non-touching -> null', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 200, y: 0 }, { x: 300, y: 0 })).toBeNull()
  })
  it('endpoint extended (does not reach) -> null', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 200, y: -50 }, { x: 200, y: 50 })).toBeNull()
  })
  it('zero-length segment -> null (no crash)', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 50, y: 0 }, { x: 50, y: 50 })).toBeNull()
  })
  it('collinear endpoint touch A-B / B-C -> B', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 0 }, { x: 200, y: 0 }), 100, 0)
  })
  it('near endpoint touch within eps (0.4mm) -> merge', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100.4, y: 0 }, { x: 200, y: 0 }), 100, 0)
  })
  it('long near-parallel segments that meet beyond extent -> null', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 10000, y: 0 }, { x: 0, y: 10 }, { x: 10000, y: 5 })).toBeNull()
  })
})

describe('splitPointsOnSegment', () => {
  it('orders cuts along the segment and dedups, keeping a and b', () => {
    const a = { x: 0, y: 0 }, b = { x: 1000, y: 0 }
    const pts = splitPointsOnSegment(a, b, [{ x: 500, y: 0 }, { x: 200, y: 0 }, { x: 500, y: 0 }])
    expect(pts.map((p) => p.x)).toEqual([0, 200, 500, 1000])
  })
  it('ignores cuts not on the segment', () => {
    const a = { x: 0, y: 0 }, b = { x: 1000, y: 0 }
    const pts = splitPointsOnSegment(a, b, [{ x: 500, y: 80 }, { x: 1500, y: 0 }])
    expect(pts.map((p) => p.x)).toEqual([0, 1000])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/segmentIntersect.spec.ts`
Expected: FAIL — `Failed to resolve import './segmentIntersect'`.

- [ ] **Step 3: Write minimal implementation**

Create `cp6.web/src/space-viewer/advanced/segmentIntersect.ts`:

```ts
// cp6.web/src/space-viewer/advanced/segmentIntersect.ts
// 纯几何：线段相交 / 端点贴合 / 分割点排序（mm 数据空间，2D-XY，无 three/Konva 依赖）。
import type { Pt } from './PickPathPlanner'

const dist = (a: Pt, b: Pt): number => Math.hypot(a.x - b.x, a.y - b.y)
const cross = (ax: number, ay: number, bx: number, by: number): number => ax * by - ay * bx

/** 点 p 是否落在段 [a,b] 上（含端点；垂距 ≤ eps）。钳制投影 → 只认段内+端点，不认延长线。 */
export function pointOnSegment(p: Pt, a: Pt, b: Pt, eps = 1): boolean {
  const dx = b.x - a.x, dy = b.y - a.y
  const len2 = dx * dx + dy * dy
  if (len2 < eps * eps) return dist(p, a) <= eps      // 退化点段
  let t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2
  t = Math.max(0, Math.min(1, t))
  const foot = { x: a.x + t * dx, y: a.y + t * dy }
  return dist(p, foot) <= eps
}

/**
 * 线段 [p1,p2] 与 [p3,p4] 的交点；含 T 型端点贴合 + 共线端点贴合。无交点 → null。
 * eps 默认 1mm；平行判据与 t/u 容差均按段长折算（denom 单位 mm²，与 eps 量纲对齐）。
 */
export function segSegIntersection(p1: Pt, p2: Pt, p3: Pt, p4: Pt, eps = 1): Pt | null {
  const len1 = dist(p1, p2), len2 = dist(p3, p4)
  // 零长度段：退化，仅判端点贴合
  if (len1 < eps || len2 < eps) {
    if (len1 < eps && pointOnSegment(p1, p3, p4, eps)) return { x: p1.x, y: p1.y }
    if (len2 < eps && pointOnSegment(p3, p1, p2, eps)) return { x: p3.x, y: p3.y }
    return null
  }
  const d1x = p2.x - p1.x, d1y = p2.y - p1.y
  const d2x = p4.x - p3.x, d2y = p4.y - p3.y
  const denom = cross(d1x, d1y, d2x, d2y)            // mm²
  // 平行/共线（按段长折算）：不直接 null，先做共线端点贴合
  if (Math.abs(denom) <= eps * Math.max(len1, len2)) {
    if (pointOnSegment(p1, p3, p4, eps)) return { x: p1.x, y: p1.y }
    if (pointOnSegment(p2, p3, p4, eps)) return { x: p2.x, y: p2.y }
    if (pointOnSegment(p3, p1, p2, eps)) return { x: p3.x, y: p3.y }
    if (pointOnSegment(p4, p1, p2, eps)) return { x: p4.x, y: p4.y }
    return null
  }
  const rx = p3.x - p1.x, ry = p3.y - p1.y
  const t = cross(rx, ry, d2x, d2y) / denom
  const u = cross(rx, ry, d1x, d1y) / denom
  const tEps = eps / Math.max(len1, eps)
  const uEps = eps / Math.max(len2, eps)
  if (t >= -tEps && t <= 1 + tEps && u >= -uEps && u <= 1 + uEps) {
    return { x: p1.x + t * d1x, y: p1.y + t * d1y }
  }
  return null
}

/** 把一组分割点排序到段 [a,b] 上（按到 a 的参数 t 升序，1mm 去重），返回有序点列（含 a、b）。 */
export function splitPointsOnSegment(a: Pt, b: Pt, cuts: Pt[], eps = 1): Pt[] {
  const dx = b.x - a.x, dy = b.y - a.y
  const len2 = dx * dx + dy * dy
  const pts: Array<{ p: Pt; t: number }> = [{ p: a, t: 0 }, { p: b, t: 1 }]
  for (const c of cuts) {
    if (!pointOnSegment(c, a, b, eps)) continue
    const t = len2 === 0 ? 0 : ((c.x - a.x) * dx + (c.y - a.y) * dy) / len2
    pts.push({ p: c, t: Math.max(0, Math.min(1, t)) })
  }
  pts.sort((x, y) => x.t - y.t)
  const out: Pt[] = []
  for (const { p } of pts) {
    const last = out[out.length - 1]
    if (!last || Math.hypot(last.x - p.x, last.y - p.y) > eps) out.push(p)
  }
  return out
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/segmentIntersect.spec.ts`
Expected: PASS (all cases green).

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/segmentIntersect.ts cp6.web/src/space-viewer/advanced/segmentIntersect.spec.ts && git commit -m "feat(space-sp3): segmentIntersect 纯几何（段长折算 eps + 共线端点贴合）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2 (A-2): `buildCenterlineGraph` 插交叉口

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts:48-56`（`buildCenterlineGraph`）
- Test: `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts`（新增用例）

- [ ] **Step 1: Write the failing test**

Append to `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts`（在最后一个 `})` 闭合 `describe` 之前新增一个 `it`）:

```ts
  it('buildCenterlineGraph splits at a mid-segment crossing (plus shape)', () => {
    // H 在 y=500、V 在 x=500，中段 (500,500) 相交，互不共端点
    const g = buildCenterlineGraph([
      { aisleCode: 'H', centerline: '[[0,500],[1000,500]]' },
      { aisleCode: 'V', centerline: '[[500,0],[500,1000]]' },
    ])
    expect(g.nodes.has('500,500')).toBe(true)       // 交叉口成为公共节点
    expect(g.nodes.size).toBe(5)                     // 4 端点 + 1 交叉口
    expect(g.adj.get('500,500')!.length).toBe(4)     // 四向连通
  })

  it('planPickRoute connects across a mid-segment crossing (v1 would degrade)', () => {
    const aisles = [
      { aisleCode: 'H', centerline: '[[0,500],[1000,500]]' },
      { aisleCode: 'V', centerline: '[[500,0],[500,1000]]' },
    ]
    // 起点贴 V 巷下段、终点贴 H 巷右段 → 必须经交叉口连通
    const route = planPickRoute(aisles, [{ x: 480, y: 100 }, { x: 900, y: 520 }])
    expect(route.degraded).toBe(false)
    expect(route.points.some((p) => Math.round(p.x) === 500 && Math.round(p.y) === 500)).toBe(true)
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts`
Expected: FAIL — `g.nodes.size` 为 4（v1 不拆交叉口）、`'500,500'` 不存在、`degraded` 为 true。

- [ ] **Step 3: Write minimal implementation**

In `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`, add the import at the top (after line 3, before `export interface Pt`):

```ts
import { segSegIntersection, splitPointsOnSegment } from './segmentIntersect'
```

Replace the existing `buildCenterlineGraph` (lines 48-56) with:

```ts
/** 把全部 Aisle 中心线连成一张图：两阶段——收集原始段 → 求交拆段（交叉口按 1mm 取整成公共节点）。 */
export function buildCenterlineGraph<T extends { centerline: string }>(aisles: T[]): Graph {
  const g: Graph = { nodes: new Map(), adj: new Map(), segments: [] }
  // 阶段 1：收集所有 aisle 中心线的相邻点对为原始段
  const raw: Array<{ a: Pt; b: Pt }> = []
  for (const a of aisles) {
    const v = parseCenterline(a.centerline)
    for (let i = 0; i + 1 < v.length; i++) raw.push({ a: v[i]!, b: v[i + 1]! })
  }
  // 阶段 2：每段扫描其余段求交点，拆段后逐子边 addEdge（共享 1mm 取整顶点 → 交叉口自动合并）
  for (let i = 0; i < raw.length; i++) {
    const s = raw[i]!
    const cuts: Pt[] = []
    for (let j = 0; j < raw.length; j++) {
      if (j === i) continue
      const x = segSegIntersection(s.a, s.b, raw[j]!.a, raw[j]!.b)
      if (x) cuts.push(x)
    }
    const ordered = splitPointsOnSegment(s.a, s.b, cuts)
    for (let k = 0; k + 1 < ordered.length; k++) addEdge(g, ordered[k]!, ordered[k + 1]!)
  }
  return g
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts`
Expected: PASS (含既有 4 用例 + 新 2 用例)。既有「共端点合并」「L 拐角」用例仍绿（共端点仍合并、拆段不破坏）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PickPathPlanner.ts cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts && git commit -m "feat(space-sp3): buildCenterlineGraph 两阶段插交叉口（中段交叉/T接连通）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3 (C): `dijkstra` → `astar`（drop-in，导出供测）

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts:78-103`（`dijkstra`）+ L131（`pathBetween` 调用点）
- Test: `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts`（新增 astar 用例）

- [ ] **Step 1: Write the failing test**

Append to the import line of `PickPathPlanner.spec.ts`（line 2）—— 把 `astar` 加入导入：

```ts
import { parseCenterline, buildCenterlineGraph, planPickRoute, astar } from './PickPathPlanner'
```

Append a new `it` inside the `describe`:

```ts
  it('astar finds the optimal path and respects the admissible heuristic', () => {
    // S(0,0)—M(50,100)—E(100,0) 三角：直连 S-E=100 短于 S-M-E≈223.6
    const adj = new Map<string, Array<{ to: string; w: number }>>([
      ['0,0', [{ to: '50,100', w: Math.hypot(50, 100) }, { to: '100,0', w: 100 }]],
      ['50,100', [{ to: '0,0', w: Math.hypot(50, 100) }, { to: '100,0', w: Math.hypot(50, 100) }]],
      ['100,0', [{ to: '0,0', w: 100 }, { to: '50,100', w: Math.hypot(50, 100) }]],
    ])
    const coords: Record<string, { x: number; y: number }> = {
      '0,0': { x: 0, y: 0 }, '50,100': { x: 50, y: 100 }, '100,0': { x: 100, y: 0 },
    }
    const path = astar(adj, '0,0', '100,0', (k) => coords[k]!)
    expect(path).toEqual(['0,0', '100,0'])
  })

  it('astar returns null when disconnected', () => {
    const adj = new Map<string, Array<{ to: string; w: number }>>([
      ['0,0', [{ to: '10,0', w: 10 }]],
      ['10,0', [{ to: '0,0', w: 10 }]],
      ['99,99', []],
    ])
    const coords: Record<string, { x: number; y: number }> = {
      '0,0': { x: 0, y: 0 }, '10,0': { x: 10, y: 0 }, '99,99': { x: 99, y: 99 },
    }
    expect(astar(adj, '0,0', '99,99', (k) => coords[k]!)).toBeNull()
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts`
Expected: FAIL — `astar` 未导出（`astar is not a function`）。

- [ ] **Step 3: Write minimal implementation**

In `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`, replace the `dijkstra` function (lines 78-103) with `astar`:

```ts
/** A\*（邻接表 + 欧氏启发式 + 临时接入节点 FA/FB）。返回 key 序列或 null。
 *  nodePt：取节点坐标（FA/FB→各自 foot，其余→g.nodes）；h(k)=dist(nodePt(k),nodePt(end))，admissible。 */
export function astar(
  adj: Map<string, Array<{ to: string; w: number }>>,
  start: string,
  end: string,
  nodePt: (k: string) => Pt,
): string[] | null {
  const g = new Map<string, number>()       // 已知最短 g 值
  const f = new Map<string, number>()        // f = g + h
  const prev = new Map<string, string>()
  const visited = new Set<string>()
  const endPt = nodePt(end)
  g.set(start, 0)
  f.set(start, dist(nodePt(start), endPt))
  while (true) {
    // 开集取最小 f（节点数小，O(V^2) 选最小可接受，不引堆）
    let u: string | null = null
    let best = Infinity
    for (const [k, fk] of f) if (!visited.has(k) && fk < best) { best = fk; u = k }
    if (u === null) break
    if (u === end) break
    visited.add(u)
    const gu = g.get(u)!
    for (const e of adj.get(u) ?? []) {
      if (visited.has(e.to)) continue
      const nd = gu + e.w
      if (nd < (g.get(e.to) ?? Infinity)) {
        g.set(e.to, nd)
        f.set(e.to, nd + dist(nodePt(e.to), endPt))
        prev.set(e.to, u)
      }
    }
  }
  if (!g.has(end)) return null
  const path: string[] = []
  let cur: string | undefined = end
  while (cur !== undefined) { path.unshift(cur); cur = prev.get(cur) }
  return path[0] === start ? path : null
}
```

Then update the call site in `pathBetween` (currently line 131 `const path = dijkstra(adj, FA, FB)`) to pass `nodePt`:

```ts
  const path = astar(adj, FA, FB, nodePt)
```

(The `nodePt` const is already defined just above on the prior line — no other change needed.)

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts`
Expected: PASS (astar 2 用例 + Task 2 交叉口用例 + 既有用例全绿——astar 是 drop-in，pathBetween/planPickRoute 行为不变)。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PickPathPlanner.ts cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts && git commit -m "feat(space-sp3): dijkstra→astar（欧氏启发 drop-in，pathBetween 透明）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4 (B-1): `routeOptimize.ts`（仅矩阵→顺序）

**Files:**
- Create: `cp6.web/src/space-viewer/advanced/routeOptimize.ts`
- Test: `cp6.web/src/space-viewer/advanced/routeOptimize.spec.ts`

- [ ] **Step 1: Write the failing test**

Create `cp6.web/src/space-viewer/advanced/routeOptimize.spec.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { routeLengthByOrder, optimizeOrder } from './routeOptimize'

// 单位正方形四角的距离矩阵：0=(0,0) 1=(0,10) 2=(10,0) 3=(10,10)
const S = Math.SQRT2 * 10 // ≈14.142 对角
const SQUARE = [
  [0, 10, 10, S],
  [10, 0, S, 10],
  [10, S, 0, 10],
  [S, 10, 10, 0],
]

describe('routeLengthByOrder', () => {
  it('open-path sum of adjacent matrix entries', () => {
    expect(routeLengthByOrder([[0, 1, 2], [1, 0, 1], [2, 1, 0]], [0, 1, 2])).toBeCloseTo(2)
  })
  it('empty / single -> 0', () => {
    expect(routeLengthByOrder([], [])).toBe(0)
    expect(routeLengthByOrder([[0]], [0])).toBe(0)
  })
})

describe('optimizeOrder', () => {
  it('empty -> []', () => { expect(optimizeOrder([])).toEqual([]) })
  it('single -> [0]', () => { expect(optimizeOrder([[0]])).toEqual([0]) })
  it('two -> [0,1]', () => { expect(optimizeOrder([[0, 5], [5, 0]])).toEqual([0, 1]) })
  it('fixes start at 0 and is a permutation', () => {
    const order = optimizeOrder(SQUARE)
    expect(order[0]).toBe(0)
    expect([...order].sort()).toEqual([0, 1, 2, 3])
  })
  it('result is no worse than the natural [0,1,2,3] order', () => {
    const order = optimizeOrder(SQUARE)
    const natural = routeLengthByOrder(SQUARE, [0, 1, 2, 3]) // 10+14.142+10 ≈ 34.142
    expect(routeLengthByOrder(SQUARE, order)).toBeLessThanOrEqual(natural + 1e-9)
    expect(routeLengthByOrder(SQUARE, order)).toBeCloseTo(30) // 走三条边长10
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/routeOptimize.spec.ts`
Expected: FAIL — `Failed to resolve import './routeOptimize'`.

- [ ] **Step 3: Write minimal implementation**

Create `cp6.web/src/space-viewer/advanced/routeOptimize.ts`:

```ts
// cp6.web/src/space-viewer/advanced/routeOptimize.ts
// 纯矩阵优化：给距离矩阵 → 出开放路径访问序。不依赖 PickPathPlanner 运行时（避免循环依赖）。

/** 按访问序计算开放路径总长（相邻项矩阵距离之和；无回程）。order 为下标排列。 */
export function routeLengthByOrder(matrix: number[][], order: number[]): number {
  let len = 0
  for (let i = 0; i + 1 < order.length; i++) len += matrix[order[i]!]![order[i + 1]!]!
  return len
}

/** 开放路径优化：起点固定 index 0，最近邻 seed + 2-opt 改进，返回访问序（order[0]===0）。 */
export function optimizeOrder(matrix: number[][]): number[] {
  const n = matrix.length
  if (n === 0) return []
  if (n === 1) return [0]
  // 最近邻 seed（从 0 出发）
  const visited = new Array<boolean>(n).fill(false)
  const order: number[] = [0]
  visited[0] = true
  for (let step = 1; step < n; step++) {
    const cur = order[order.length - 1]!
    let nextIdx = -1, best = Infinity
    for (let j = 0; j < n; j++) {
      if (visited[j]) continue
      if (matrix[cur]![j]! < best) { best = matrix[cur]![j]!; nextIdx = j }
    }
    order.push(nextIdx)
    visited[nextIdx] = true
  }
  // 2-opt：反转区间 [i,j]（i≥1 保持 order[0] 固定），若降低总长则采纳，直到无改进
  let improved = true
  let guard = 0
  while (improved && guard++ < 1000) {
    improved = false
    for (let i = 1; i < n - 1; i++) {
      for (let j = i + 1; j < n; j++) {
        const cand = order.slice(0, i).concat(order.slice(i, j + 1).reverse(), order.slice(j + 1))
        if (routeLengthByOrder(matrix, cand) + 1e-9 < routeLengthByOrder(matrix, order)) {
          for (let k = 0; k < n; k++) order[k] = cand[k]!
          improved = true
        }
      }
    }
  }
  return order
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/routeOptimize.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/routeOptimize.ts cp6.web/src/space-viewer/advanced/routeOptimize.spec.ts && git commit -m "feat(space-sp3): routeOptimize 仅矩阵→顺序（NN+2opt，无 planner 依赖）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5 (B-2): `distanceMatrixFromGraph` + `planPickRouteOnGraph` + `planPickComparison`

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`（新增 3 函数 + 接口；`planPickRoute` 改包装）
- Test: `cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts`（新增 comparison/distanceMatrix 用例）

- [ ] **Step 1: Write the failing test**

Update the import line of `PickPathPlanner.spec.ts`:

```ts
import {
  parseCenterline, buildCenterlineGraph, planPickRoute, astar,
  distanceMatrixFromGraph, planPickComparison,
} from './PickPathPlanner'
```

Append new `it` cases inside the `describe`:

```ts
  it('distanceMatrixFromGraph is symmetric; degraded pairs use euclidean', () => {
    const g = buildCenterlineGraph([{ aisleCode: 'H', centerline: '[[0,0],[1000,0]]' }])
    const stops = [{ x: 0, y: 50 }, { x: 200, y: 50 }]
    const m = distanceMatrixFromGraph(g, stops)
    expect(m[0]![0]).toBe(0)
    expect(m[0]![1]).toBeCloseTo(m[1]![0]!)        // 对称
    expect(m[0]![1]).toBeCloseTo(300)              // 50 下 + 200 巷 + 50 上

    const empty = buildCenterlineGraph([])         // 无段 → degraded 欧氏
    const md = distanceMatrixFromGraph(empty, stops)
    expect(md[0]![1]).toBeCloseTo(200)             // 直连欧氏
  })

  it('planPickComparison: optimized never longer than actual; savings >= 0', () => {
    const aisles = [{ aisleCode: 'H', centerline: '[[0,0],[1000,0]]' }]
    // LineNo 序 0->1000->200->800 来回绕路；优化序应 0->200->800->1000
    const stops = [{ x: 0, y: 50 }, { x: 1000, y: 50 }, { x: 200, y: 50 }, { x: 800, y: 50 }]
    const cmp = planPickComparison(aisles, stops)
    expect(cmp.order[0]).toBe(0)
    expect(cmp.order).toEqual([0, 2, 3, 1])
    expect(cmp.actualMm).toBeCloseTo(2700)
    expect(cmp.optimizedMm).toBeCloseTo(1300)
    expect(cmp.optimizedMm).toBeLessThanOrEqual(cmp.actualMm + 1e-6)
    expect(cmp.savingsPct).toBeGreaterThan(0)
    expect(cmp.actual.degraded).toBe(false)
    expect(cmp.optimized.degraded).toBe(false)
    expect(cmp.degradedPairCount).toBe(0)
  })

  it('planPickComparison: already-optimal order falls back, savings = 0', () => {
    const aisles = [{ aisleCode: 'H', centerline: '[[0,0],[1000,0]]' }]
    const stops = [{ x: 0, y: 50 }, { x: 200, y: 50 }, { x: 800, y: 50 }, { x: 1000, y: 50 }]
    const cmp = planPickComparison(aisles, stops)
    expect(cmp.order).toEqual([0, 1, 2, 3])        // 回退原序
    expect(cmp.savingsPct).toBe(0)
    expect(cmp.optimizedMm).toBeCloseTo(cmp.actualMm)
  })

  it('planPickComparison: single stop -> zero distances, savings 0', () => {
    const cmp = planPickComparison([{ aisleCode: 'H', centerline: '[[0,0],[1000,0]]' }], [{ x: 0, y: 50 }])
    expect(cmp.order).toEqual([0])
    expect(cmp.actualMm).toBe(0)
    expect(cmp.optimizedMm).toBe(0)
    expect(cmp.savingsPct).toBe(0)
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts`
Expected: FAIL — `distanceMatrixFromGraph`/`planPickComparison` 未导出。

- [ ] **Step 3: Write minimal implementation**

In `cp6.web/src/space-viewer/advanced/PickPathPlanner.ts`:

(a) Add the import near the top (with the segmentIntersect import):

```ts
import { optimizeOrder, routeLengthByOrder } from './routeOptimize'
```

(b) Refactor `planPickRoute` (currently lines 144-157) into an internal `planPickRouteOnGraph` + thin public wrapper. Replace the whole `planPickRoute` block with:

```ts
/** 按已建图规划整条路径（内部，避免重复 buildCenterlineGraph）。 */
function planPickRouteOnGraph(g: Graph, stops: Pt[]): PlannedRoute {
  if (stops.length < 2) return { points: stops.slice(), totalDistance: 0, degraded: false }
  const points: Pt[] = []
  let degraded = false
  for (let i = 0; i + 1 < stops.length; i++) {
    const seg = pathBetween(g, stops[i]!, stops[i + 1]!)
    degraded = degraded || seg.degraded
    const segPts = i === 0 ? seg.points : seg.points.slice(1) // 去掉与上段重合的接缝起点
    points.push(...segPts)
  }
  return { points, totalDistance: polyDist(points), degraded }
}

/** 整条拣货路径：依次拼接相邻拣货点（去重接缝点）。对外旧签名，内部建图一次。 */
export function planPickRoute<T extends { centerline: string }>(aisles: T[], stops: Pt[]): PlannedRoute {
  return planPickRouteOnGraph(buildCenterlineGraph(aisles), stops)
}

/** 拣货点两两图最短距离矩阵（mm；degraded 段记直连欧氏，一致可比）。对称。
 *  degradedPairs：i<j 计一次退化点对数（写入引用，供 planPickComparison 透出）。 */
export function distanceMatrixFromGraph(g: Graph, stops: Pt[], degradedPairs?: { count: number }): number[][] {
  const n = stops.length
  const m: number[][] = Array.from({ length: n }, () => new Array<number>(n).fill(0))
  for (let i = 0; i < n; i++) {
    for (let j = i + 1; j < n; j++) {
      const seg = pathBetween(g, stops[i]!, stops[j]!)
      const d = polyDist(seg.points)
      m[i]![j] = d
      m[j]![i] = d
      if (seg.degraded && degradedPairs) degradedPairs.count++
    }
  }
  return m
}

export interface PickComparison {
  actual: PlannedRoute       // LineNo 序
  optimized: PlannedRoute    // 优化序（已兜底 ≤ actual）
  order: number[]            // 优化访问序（stops 下标，order[0]===0；回退时 = [0,1,2,…]）
  actualMm: number           // mm（底层数据空间即 mm）
  optimizedMm: number        // mm
  savingsPct: number         // (actualMm-optimizedMm)/actualMm*100；actualMm=0→0；钳 ≥0
  degradedPairCount: number  // distanceMatrix 中退化（直连欧氏）的点对数
}

/** what-if 重排对比：actual=LineNo 序，optimized=NN+2opt（以 actual 为 baseline 兜底，强保证 ≤ actual）。 */
export function planPickComparison<T extends { centerline: string }>(aisles: T[], stops: Pt[]): PickComparison {
  const g = buildCenterlineGraph(aisles)                  // 单次建图
  const actual = planPickRouteOnGraph(g, stops)
  if (stops.length < 2) {
    return { actual, optimized: actual, order: stops.map((_, i) => i), actualMm: actual.totalDistance, optimizedMm: actual.totalDistance, savingsPct: 0, degradedPairCount: 0 }
  }
  const degradedPairs = { count: 0 }
  const matrix = distanceMatrixFromGraph(g, stops, degradedPairs)
  const actualOrder = stops.map((_, i) => i)
  const candidateOrder = optimizeOrder(matrix)
  const actualLen = routeLengthByOrder(matrix, actualOrder)
  const candidateLen = routeLengthByOrder(matrix, candidateOrder)
  const order = candidateLen + 1e-9 < actualLen ? candidateOrder : actualOrder
  const optimized = planPickRouteOnGraph(g, order.map((i) => stops[i]!))
  const actualMm = actual.totalDistance
  const optimizedMm = optimized.totalDistance
  const savingsPct = actualMm === 0 ? 0 : Math.max(0, ((actualMm - optimizedMm) / actualMm) * 100)
  return { actual, optimized, order, actualMm, optimizedMm, savingsPct, degradedPairCount: degradedPairs.count }
}
```

> 注：`planPickRouteOnGraph`、`pathBetween`、`polyDist`、`buildCenterlineGraph` 均已在文件内定义；删除原 `planPickRoute` 旧体后只保留上面的新体。

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PickPathPlanner.spec.ts`
Expected: PASS（comparison 4 用例 + 既有用例全绿；`planPickRoute` 旧签名行为不变）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PickPathPlanner.ts cp6.web/src/space-viewer/advanced/PickPathPlanner.spec.ts && git commit -m "feat(space-sp3): planPickComparison（单次建图+actual baseline 兜底+degradedPairCount）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6 (B-3): `PathAnimator.setComparisonPath`

**Files:**
- Modify: `cp6.web/src/space-viewer/advanced/PathAnimator.ts`（新增 `_compareLine` + `setComparisonPath`；`clear` 复位）
- Test: `cp6.web/src/space-viewer/advanced/PathAnimator.spec.ts`（新增对比线用例）

- [ ] **Step 1: Write the failing test**

Append to `cp6.web/src/space-viewer/advanced/PathAnimator.spec.ts` inside the `describe`:

```ts
  it('setComparisonPath adds a green line and null removes only it (keeps path+cart)', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    expect(v.root.children[0]!.children.length).toBe(2)   // line + cart
    a.setComparisonPath([{ x: 0, y: 0 }, { x: 500, y: 500 }])
    expect(v.root.children[0]!.children.length).toBe(3)   // + compare line
    a.setComparisonPath(null)
    expect(v.root.children[0]!.children.length).toBe(2)   // 只移除对比线
  })

  it('clear() also removes the comparison line', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    a.setComparisonPath([{ x: 0, y: 0 }, { x: 500, y: 500 }])
    a.clear()
    expect(v.root.children.length).toBe(0)
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PathAnimator.spec.ts`
Expected: FAIL — `a.setComparisonPath is not a function`.

- [ ] **Step 3: Write minimal implementation**

In `cp6.web/src/space-viewer/advanced/PathAnimator.ts`:

(a) Add a color constant near the others (after line 13 `const CART_COLOR = 0xff4081`):

```ts
const COMPARE_COLOR = 0x76ff03   // 绿，优化对比线
```

(b) Add the field after `private _cart: Mesh | null = null` (line 21):

```ts
  private _compareLine: Line | null = null
```

(c) Add the method after `setPath` (after line 52, before `_positionCart`):

```ts
  /** 静态对比线（优化序，无小车不参与动画）；null 清除。挂在同一 _group 下。 */
  setComparisonPath(points: Pt[] | null): void {
    if (this._compareLine) {
      this._group.remove(this._compareLine)
      this._compareLine = null
    }
    if (points && points.length >= 2) {
      const arr: number[] = []
      for (const p of points) arr.push(p.x, p.y, GROUND_Z + 20)  // +20mm 防 z-fight
      const geom = new BufferGeometry()
      geom.setAttribute('position', new Float32BufferAttribute(arr, 3))
      this._compareLine = new Line(geom, new LineBasicMaterial({ color: COMPARE_COLOR }))
      this._group.add(this._compareLine)
    }
    this._viewer.requestRender()
  }
```

(d) In `clear()` (line 107-116), add `_compareLine` reset — after `this._cart = null` add:

```ts
    this._compareLine = null
```

(`this._group.clear()` already removes all children including the compare line; the field reset prevents a stale reference.)

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /d/CP6-space-backend/cp6.web && npx vitest run src/space-viewer/advanced/PathAnimator.spec.ts`
Expected: PASS（新 2 用例 + 既有 4 用例全绿）。

- [ ] **Step 5: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/space-viewer/advanced/PathAnimator.ts cp6.web/src/space-viewer/advanced/PathAnimator.spec.ts && git commit -m "feat(space-sp3): PathAnimator.setComparisonPath（绿静态对比线，clear 同步移除）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7 (B-4): `AdvancedPanel` + `FloorViewer` 接线 + 三门

**Files:**
- Modify: `cp6.web/src/views/space/viewer/AdvancedPanel.vue`
- Modify: `cp6.web/src/views/space/viewer/FloorViewer.vue`

- [ ] **Step 1: AdvancedPanel —— 新增 compareInfo + showOptimized 开关**

In `cp6.web/src/views/space/viewer/AdvancedPanel.vue`:

(a) In the template, replace the path-info line (line 21 `<div class="ap-info" v-if="pathInfo">{{ pathInfo }}</div>`) with the info line + a comparison block (shown only when path loaded):

```html
      <div class="ap-info" v-if="pathInfo">{{ pathInfo }}</div>
      <div class="ap-info" v-if="pathLoaded && compareInfo">{{ compareInfo }}</div>
      <label class="ap-check" v-if="pathLoaded">
        <input type="checkbox" :checked="showOptimized" @change="$emit('toggle-optimized')" />{{ t('显示优化路径') }}
      </label>
```

(b) Update `defineProps` (line 45) to add `compareInfo` and `showOptimized`:

```ts
defineProps<{ pathLoaded: boolean; pathInfo: string; compareInfo: string; showOptimized: boolean; workloadOn: boolean; deviceOn: boolean }>()
```

(c) Add the emit to `defineEmits` (inside the emits block, lines 46-53) — add this line:

```ts
  (e: 'toggle-optimized'): void
```

- [ ] **Step 2: FloorViewer —— 状态 + 接线 + 复位**

In `cp6.web/src/views/space/viewer/FloorViewer.vue`:

(a) Update the import (line 93) to add `planPickComparison` + the type:

```ts
import { planPickComparison, type Pt, type PickComparison } from '@/space-viewer/advanced/PickPathPlanner'
```

(b) Add state refs after `const pathInfo = ref('')` (line 123):

```ts
const comparison = ref<PickComparison | null>(null)
const showOptimized = ref(false)
const compareInfo = ref('')
```

(c) In `loadFloor`, after `pathInfo.value = ''` (line 143), add comparison reset:

```ts
  comparison.value = null
  showOptimized.value = false
  compareInfo.value = ''
```

(d) Replace the body of `onLoadPath` (lines 251-270) with the comparison flow (sort stops by seq; build comparison; feed actual to animator; reset optimized line; compose compareInfo):

```ts
async function onLoadPath(taskNo: string): Promise<void> {
  if (!taskNo || !pathAnimator) return
  try {
    const env = await advancedApi.pickPath(currentFloorId.value, taskNo)
    const data = env.data
    const stopPts: Pt[] = [...data.stops]
      .sort((a, b) => a.seq - b.seq)                              // 按 LineNo(seq) 升序，固定 actual 语义
      .filter((s) => s.absX != null && s.absY != null)
      .map((s) => ({ x: s.absX as number, y: s.absY as number }))
    if (stopPts.length < 2) { ElMessage.info(t('该拣货单无可定位拣货点')); return }
    const cmp = planPickComparison(data.aisles, stopPts)
    comparison.value = cmp
    pathAnimator.setPath(cmp.actual.points)                       // 青线 + 小车 = 实际 LineNo 序
    showOptimized.value = false
    pathAnimator.setComparisonPath(null)
    pathLoaded.value = true
    pathInfo.value = t('拣货路径：{n} 点，总距 {d} 米')
      .replace('{n}', String(stopPts.length))
      .replace('{d}', (cmp.actualMm / 1000).toFixed(1))           // I-SPACE-801
    compareInfo.value = t('实际 {a} 米 / 优化 {o} 米 / 省 {p}%')
      .replace('{a}', (cmp.actualMm / 1000).toFixed(1))
      .replace('{o}', (cmp.optimizedMm / 1000).toFixed(1))
      .replace('{p}', cmp.savingsPct.toFixed(0))
    if (cmp.actual.degraded) ElMessage.warning(t('巷道路径不连通，近似直连显示'))  // W-SPACE-801
  } catch {
    ElMessage.warning(t('高级可视化数据获取失败'))   // W-SPACE-802
  }
}

function onToggleOptimized(): void {
  showOptimized.value = !showOptimized.value
  pathAnimator?.setComparisonPath(showOptimized.value ? (comparison.value?.optimized.points ?? null) : null)
}
```

(e) In the template `<AdvancedPanel ... />` (lines 52-66), add the new props + emit binding:

```html
      <AdvancedPanel
        :path-loaded="pathLoaded"
        :path-info="pathInfo"
        :compare-info="compareInfo"
        :show-optimized="showOptimized"
        :workload-on="workloadOn"
        :device-on="deviceOn"
        @load-path="onLoadPath"
        @play="onPathPlay"
        @pause="onPathPause"
        @step="onPathStep"
        @replay="onPathReplay"
        @speed="onPathSpeed"
        @toggle-optimized="onToggleOptimized"
        @toggle-workload="onToggleWorkload"
        @apply-workload="onApplyWorkload"
        @toggle-device="onToggleDevice"
      />
```

- [ ] **Step 3: Run the three gates (type-check / vitest / build)**

```bash
cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit && npx vitest run && npm run build
```
Expected:
- `vue-tsc --noEmit` → 0 errors.
- `vitest run` → all green (既有 + 新增 segmentIntersect/routeOptimize/PickPathPlanner/PathAnimator)。
- `npm run build` → success.

> 若 `npm run build` 脚本名不同，先看 `cp6.web/package.json` 的 `scripts`（既往用 `npm run build`）。

- [ ] **Step 4: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/views/space/viewer/AdvancedPanel.vue cp6.web/src/views/space/viewer/FloorViewer.vue && git commit -m "feat(space-sp3): AdvancedPanel/FloorViewer 接线（stops 按 seq 排序+对比统计+优化路径开关+切层复位）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8 (QA): 多巷十字种子 + gstack 验收

**Files:**
- Create: `docs/superpowers/qa/space-p3-sp3/seed.sql`
- Create: `docs/superpowers/qa/space-p3-sp3/README.md`
- Create: `docs/superpowers/qa/space-p3-sp3/*.png`（截图）

> 此 Task 为运行态 QA，不走 vitest。环境沿用 SP2：后端 5177（`appsettings.Local.json`→`CP6DB_SpaceQA`）/ vite / admin·123456 / viewer 路由 `/space/viewer/{siteId}?floorId=`。坑：冷后端首调 ~5-6s JIT；sqlcmd 种子用 PowerShell + ASCII；raw SQL 用 `[LineNo]`（保留字）；演示 floor `5C92E6A8…`，真实编码 `A-01-01-01…A-01-02-02`。

- [ ] **Step 1: 写多巷十字网格种子（含「明显绕路」出库单）**

构造 `docs/superpowers/qa/space-p3-sp3/seed.sql`：在演示 floor 插 ≥2 主巷 + ≥1 横巷的 `Space_Aisle.Centerline`（中心线相交成网格），并造一张出库单带跨多巷的多条有序明细，**LineNo 顺序刻意绕路**（如 左上→右下→左下→右上），使优化序明显更短。落点对齐已发布编码 `A-01-01-01 / A-01-01-02 / A-01-02-01 / A-01-02-02`（这些 `Space_Location` 已有 AbsX/Y/Z）。
- 先用 sqlcmd 查 floor `5C92E6A8…` 现有 `Space_Aisle`/`Space_Location` 的坐标范围，让中心线网格覆盖这 4 个编码的 AbsX/Y。
- 中心线 JSON 形如 `[[x1,y1],[x2,y2]]`（mm）；主巷竖向、横巷横向，中段相交。
- 出库单：`T_OutboundOrder`（Status=Picking=3）+ `T_OutboundOrderDetail`（4 行，`[LineNo]` 1..4 对应绕路顺序，关联上述 4 编码）。
- 幂等：`IF NOT EXISTS` 包裹插入。

运行（PowerShell，ASCII-only）：
```
& "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" -S "localhost\KOUSQLSERVER" -E -d CP6DB_SpaceQA -i "D:\CP6-space-backend\docs\superpowers\qa\space-p3-sp3\seed.sql"
```
Expected: 插入成功，无主键/唯一键冲突。

- [ ] **Step 2: 启栈 + 后端 API 冒烟（pick-path 返回多巷中心线 + 多有序明细）**

启后端 5177（`appsettings.Local.json`→`CP6DB_SpaceQA`）+ vite。登录 admin/123456（POST `/api/auth/login` {userName,password}，dev Csrf 关）。curl pick-path：
```
GET /api/space/floor/5C92E6A8.../pick-path?taskNo=<新出库单号>
```
Expected: HTTP 200；`aisles` 含 ≥3 条中心线（主+横，相交）；`stops` 4 点带 AbsXYZ，按绕路 LineNo 序。冷后端首调 sleep ~6s。

- [ ] **Step 3: gstack 浏览器验收 4 点**

用 gstack headless 打开 `/space/viewer/{siteId}?floorId=5C92E6A8...`，加载该拣货单，逐点确认并截图：
1. **A 真交叉口**：拣货路径**沿巷道走、不穿货架**（v1 会直连穿过）；无 `W-SPACE-801`（degraded）告警 → `degradedPairCount=0`。
2. **B 重排对比**：面板显示「实际 X.X 米 / 优化 Y.Y 米 / 省 Z%」（Z>0）；勾「显示优化路径」→ 绿色优化线叠加；取消勾 → 绿线消失；播放跑实际（青）路径。
3. **C A\***：路径沿巷道同形（与单测等价性互证）。
4. **无回归**：07 库存叠加 / 08 热图（与优化线/动画互斥逻辑不冲突）/ 设备占位 / 既有单巷 pick-path 正常；切楼层后优化线/开关复位（不残留绿线）。

- [ ] **Step 4: 固化 QA 证据 + 提交**

写 `docs/superpowers/qa/space-p3-sp3/README.md`（环境 + 4 验收点结论 + 截图引用 + 已知 headless 限制）。

```bash
cd /d/CP6-space-backend && git add docs/superpowers/qa/space-p3-sp3 && git commit -m "test(space-sp3): 多巷十字种子 + gstack 验收（沿巷道不穿货架/对比省距/优化线开关/无回归）

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage（逐节核对 spec v1.1）：**
- §2.1 segmentIntersect（pointOnSegment/segSegIntersection/splitPointsOnSegment，段长折算 eps + 共线端点贴合）→ Task 1 ✓
- §2.2 buildCenterlineGraph 插交叉口 → Task 2 ✓
- §3 dijkstra→astar（nodePt 启发式）→ Task 3 ✓
- §4.1 routeOptimize（仅矩阵：routeLengthByOrder + optimizeOrder）→ Task 4 ✓
- §4.2 distanceMatrixFromGraph + planPickRouteOnGraph + planPickComparison（actualMm/optimizedMm/savingsPct/degradedPairCount/baseline 兜底/单次建图）→ Task 5 ✓
- §4.3 PathAnimator.setComparisonPath（+clear 移除）→ Task 6 ✓
- §4.4 AdvancedPanel(compareInfo/showOptimized/.ap-check/t())+FloorViewer(seq 排序/接线/复位）→ Task 7 ✓
- §4.5 兜底规则（单位 mm/baseline/无循环依赖/degraded 透出/seq 排序/单次建图）→ 分散落在 Task 1/4/5/7，铁律已在 plan 头部列明 ✓
- §5.1 vitest 全表 → Task 1/2/3/4/5/6 ✓
- §5.2 gstack（多巷十字 + 明显绕路种子 + 4 验收点）→ Task 8 ✓
- §6 文件清单 → File Structure ✓
- §7 交付序 A→C→B→QA → Task 1→2→3→4→5→6→7→8 ✓

**2. Placeholder scan:** 无 TBD/TODO；每个 code 步骤含完整代码；测试均含具体断言值（几何坐标/矩阵/距离均手算可验）。

**3. Type consistency:**
- `Pt`（PickPathPlanner 既有导出）— Task 1 `import type`、Task 4 不依赖、Task 5/6/7 一致 ✓
- `astar(adj,start,end,nodePt)` — Task 3 定义、pathBetween 调用、Task 3 测试一致 ✓
- `optimizeOrder(matrix)` / `routeLengthByOrder(matrix,order)` — Task 4 定义、Task 5 调用一致 ✓
- `distanceMatrixFromGraph(g,stops,degradedPairs?)` — Task 5 定义/自用一致；`{count:number}` 引用传递 ✓
- `PickComparison { actual, optimized, order, actualMm, optimizedMm, savingsPct, degradedPairCount }` — Task 5 定义、Task 7 消费（`cmp.actualMm`/`cmp.optimizedMm`/`cmp.savingsPct`/`cmp.actual.points`/`cmp.optimized.points`/`cmp.actual.degraded`）一致 ✓
- `setComparisonPath(points|null)` — Task 6 定义、Task 7 `onToggleOptimized`/`onLoadPath` 调用一致 ✓
- AdvancedPanel props `compareInfo:string`/`showOptimized:boolean` + emit `toggle-optimized` — Task 7 定义/绑定一致 ✓

---

## Execution Handoff

见会话——本计划默认 subagent-driven TDD（用户已定流程）。
