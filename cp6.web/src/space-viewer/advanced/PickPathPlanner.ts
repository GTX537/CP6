// cp6.web/src/space-viewer/advanced/PickPathPlanner.ts
// 拣货路径规划：中心线图 + Dijkstra（纯逻辑，mm 数据空间，2D-XY）。

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

/** 把全部 Aisle 中心线连成一张图（顶点按 1mm 取整去重，共端点/交叉自动合并）。 */
export function buildCenterlineGraph(aisles: Array<{ centerline: string }>): Graph {
  const g: Graph = { nodes: new Map(), adj: new Map(), segments: [] }
  for (const a of aisles) {
    const v = parseCenterline(a.centerline)
    for (let i = 0; i + 1 < v.length; i++) addEdge(g, v[i]!, v[i + 1]!)
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

/** Dijkstra（邻接表 + 临时接入节点 FA/FB）。返回 key 序列或 null。 */
function dijkstra(adj: Map<string, Array<{ to: string; w: number }>>, start: string, end: string): string[] | null {
  const dists = new Map<string, number>()
  const prev = new Map<string, string>()
  const visited = new Set<string>()
  dists.set(start, 0)
  // 简单 O(V^2) 选最小（一层巷道节点数十级，足够）
  while (true) {
    let u: string | null = null
    let best = Infinity
    for (const [k, d] of dists) if (!visited.has(k) && d < best) { best = d; u = k }
    if (u === null) break
    if (u === end) break
    visited.add(u)
    for (const e of adj.get(u) ?? []) {
      if (visited.has(e.to)) continue
      const nd = best + e.w
      if (nd < (dists.get(e.to) ?? Infinity)) { dists.set(e.to, nd); prev.set(e.to, u) }
    }
  }
  if (!dists.has(end)) return null
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
  const path = dijkstra(adj, FA, FB)
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
export function planPickRoute(aisles: Array<{ centerline: string }>, stops: Pt[]): PlannedRoute {
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
