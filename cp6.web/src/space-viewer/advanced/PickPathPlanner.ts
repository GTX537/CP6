// cp6.web/src/space-viewer/advanced/PickPathPlanner.ts
// 拣货路径规划：中心线图 + Dijkstra（纯逻辑，mm 数据空间，2D-XY）。

import { segSegIntersection, splitPointsOnSegment } from './segmentIntersect'

export interface Pt { x: number; y: number }

export interface PlannedRoute {
  points: Pt[]          // 完整折线（mm，XY），首=起库位 末=止库位
  totalDistance: number // mm
  degraded: boolean     // 任一段退化为直连（W-SPACE-801）
}

export interface Graph {
  nodes: Map<string, Pt>
  adj: Map<string, Array<{ to: string; w: number }>>
  segments: Array<{ a: Pt; b: Pt }>
}

const key = (p: Pt): string => `${Math.round(p.x)},${Math.round(p.y)}`
const dist = (a: Pt, b: Pt): number => Math.hypot(a.x - b.x, a.y - b.y)

/** 解析中心线 JSON `[[x,y],…]`；非法/空 → []。 */
export function parseCenterline(json: string): Pt[] {
  if (!json) return []
  try {
    const raw = JSON.parse(json)
    if (!Array.isArray(raw)) return []
    return raw
      .filter((p) => Array.isArray(p) && p.length >= 2 && Number.isFinite(p[0]) && Number.isFinite(p[1]))
      .map((p) => ({ x: p[0], y: p[1] }))
  } catch {
    return []
  }
}

function addEdge(g: Graph, a: Pt, b: Pt): void {
  const ka = key(a), kb = key(b)
  if (ka === kb) return
  if (!g.nodes.has(ka)) g.nodes.set(ka, a)
  if (!g.nodes.has(kb)) g.nodes.set(kb, b)
  const w = dist(a, b)
  if (!g.adj.has(ka)) g.adj.set(ka, [])
  if (!g.adj.has(kb)) g.adj.set(kb, [])
  if (!g.adj.get(ka)!.some((e) => e.to === kb)) g.adj.get(ka)!.push({ to: kb, w })
  if (!g.adj.get(kb)!.some((e) => e.to === ka)) g.adj.get(kb)!.push({ to: ka, w })
  g.segments.push({ a, b })
}

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

/** 点投影到线段 [a,b]，返回垂足（钳制到段内）与距离。 */
function projectToSegment(p: Pt, a: Pt, b: Pt): { foot: Pt; d: number } {
  const dx = b.x - a.x, dy = b.y - a.y
  const len2 = dx * dx + dy * dy
  let t = len2 === 0 ? 0 : ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2
  t = Math.max(0, Math.min(1, t))
  const foot = { x: a.x + t * dx, y: a.y + t * dy }
  return { foot, d: dist(p, foot) }
}

/** 最近接入点：把库位投影到最近中心线段，返回垂足 + 该段两端点。无段 → null。 */
function nearestAccess(g: Graph, p: Pt): { foot: Pt; segA: Pt; segB: Pt } | null {
  let best: { foot: Pt; segA: Pt; segB: Pt; d: number } | null = null
  for (const s of g.segments) {
    const { foot, d } = projectToSegment(p, s.a, s.b)
    if (!best || d < best.d) best = { foot, segA: s.a, segB: s.b, d }
  }
  return best ? { foot: best.foot, segA: best.segA, segB: best.segB } : null
}

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

/** 相邻两拣货点路径：a→接入→沿巷道→接入→b。不连通/无段 → 直连 degraded。 */
function pathBetween(g: Graph, a: Pt, b: Pt): { points: Pt[]; degraded: boolean } {
  const accA = nearestAccess(g, a)
  const accB = nearestAccess(g, b)
  if (!accA || !accB) return { points: [a, b], degraded: true }

  // 临时邻接：克隆 + 接入 FA/FB（连到各自段两端；同段则直连 FA-FB）
  const adj = new Map<string, Array<{ to: string; w: number }>>()
  for (const [k, list] of g.adj) adj.set(k, list.slice())
  const FA = 'FA', FB = 'FB'
  const link = (n: string, p: Pt, segA: Pt, segB: Pt) => {
    adj.set(n, [
      { to: key(segA), w: dist(p, segA) },
      { to: key(segB), w: dist(p, segB) },
    ])
    adj.get(key(segA))!.push({ to: n, w: dist(p, segA) })
    adj.get(key(segB))!.push({ to: n, w: dist(p, segB) })
  }
  link(FA, accA.foot, accA.segA, accA.segB)
  link(FB, accB.foot, accB.segA, accB.segB)
  if (key(accA.segA) === key(accB.segA) && key(accA.segB) === key(accB.segB)) {
    adj.get(FA)!.push({ to: FB, w: dist(accA.foot, accB.foot) })
    adj.get(FB)!.push({ to: FA, w: dist(accA.foot, accB.foot) })
  }

  const nodePt = (k: string): Pt => (k === FA ? accA.foot : k === FB ? accB.foot : g.nodes.get(k)!)
  const path = astar(adj, FA, FB, nodePt)
  if (!path) return { points: [a, b], degraded: true }

  const mid = path.map(nodePt)
  return { points: [a, ...mid, b], degraded: false }
}

function polyDist(pts: Pt[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += dist(pts[i - 1]!, pts[i]!)
  return d
}

/** 整条拣货路径：依次拼接相邻拣货点（去重接缝点）。 */
export function planPickRoute<T extends { centerline: string }>(aisles: T[], stops: Pt[]): PlannedRoute {
  if (stops.length < 2) return { points: stops.slice(), totalDistance: 0, degraded: false }
  const g = buildCenterlineGraph(aisles)
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
