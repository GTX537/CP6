// cp6.web/src/space-viewer/advanced/planMultiFloor.ts —— 多层图 + 跨层路径（承 SP3，图在前端）
import { buildCenterlineGraph, key, astar, type Pt } from './PickPathPlanner'
import { mfKey, dist3, type Pt3, type FloorMeta } from './multiFloor'

export interface MFGraph {
  nodes: Map<string, Pt3>                                   // key=mfKey；z=层标高
  adj: Map<string, Array<{ to: string; w: number }>>
  segments: Array<{ a: Pt; b: Pt; floorId: string }>        // 供按层投影接入
}
export interface AisleVOLite { aisleCode: string; centerline: string }
export interface ConnectorPath { connectorCode: string; type: number; stops: Array<{ floorId: string; x: number; y: number }> }

function addMFEdge(g: MFGraph, ka: string, pa: Pt3, kb: string, pb: Pt3, w: number): void {
  if (ka === kb) return
  if (!g.nodes.has(ka)) g.nodes.set(ka, pa)
  if (!g.nodes.has(kb)) g.nodes.set(kb, pb)
  if (!g.adj.has(ka)) g.adj.set(ka, [])
  if (!g.adj.has(kb)) g.adj.set(kb, [])
  if (!g.adj.get(ka)!.some((e) => e.to === kb)) g.adj.get(ka)!.push({ to: kb, w })
  if (!g.adj.get(kb)!.some((e) => e.to === ka)) g.adj.get(kb)!.push({ to: ka, w })
}

/** nearestAccess 的 segments 版（投影到最近段取两端）。 */
export function nearestAccessOnSegments(segs: Array<{ a: Pt; b: Pt }>, p: Pt): { segA: Pt; segB: Pt } | null {
  let best: { segA: Pt; segB: Pt; d: number } | null = null
  for (const s of segs) {
    const dx = s.b.x - s.a.x, dy = s.b.y - s.a.y
    const len2 = dx * dx + dy * dy
    let t = len2 === 0 ? 0 : ((p.x - s.a.x) * dx + (p.y - s.a.y) * dy) / len2
    t = Math.max(0, Math.min(1, t))
    const foot = { x: s.a.x + t * dx, y: s.a.y + t * dy }
    const d = Math.hypot(p.x - foot.x, p.y - foot.y)
    if (!best || d < best.d) best = { segA: s.a, segB: s.b, d }
  }
  return best ? { segA: best.segA, segB: best.segB } : null
}

/** 合并各层 SP3 子图（按 floorId 命名空间）+ 连接体接入本层巷道 + 同连接体相邻层 stop 竖直边（权=|Δz|）。 */
export function buildMultiFloorGraph(
  floors: FloorMeta[],
  aislesByFloor: Map<string, AisleVOLite[]>,
  connectors: ConnectorPath[],
): MFGraph {
  const zOf = new Map(floors.map((f) => [f.floorId, f.z]))
  const g: MFGraph = { nodes: new Map(), adj: new Map(), segments: [] }

  // 1) 各层 SP3 子图 → 前缀合并
  for (const f of floors) {
    const z = f.z
    const g2d = buildCenterlineGraph(aislesByFloor.get(f.floorId) ?? [])
    for (const [k2d, pt] of g2d.nodes) g.nodes.set(`${f.floorId}:${k2d}`, { x: pt.x, y: pt.y, z })
    for (const [k2d, list] of g2d.adj) g.adj.set(`${f.floorId}:${k2d}`, list.map((e) => ({ to: `${f.floorId}:${e.to}`, w: e.w })))
    for (const s of g2d.segments) g.segments.push({ a: s.a, b: s.b, floorId: f.floorId })
  }

  // 2) 连接体：每 stop 接入本层最近巷道；同连接体相邻层 stop 竖直边
  for (const c of connectors) {
    const placed = c.stops.filter((s) => zOf.has(s.floorId)).map((s) => ({ s, z: zOf.get(s.floorId)! }))
    for (const { s, z } of placed) {
      const floorSegs = g.segments.filter((seg) => seg.floorId === s.floorId)
      const acc = nearestAccessOnSegments(floorSegs, { x: s.x, y: s.y })
      const nodeK = mfKey(s.floorId, s)
      const nodeP: Pt3 = { x: s.x, y: s.y, z }
      if (acc) {
        addMFEdge(g, nodeK, nodeP, `${s.floorId}:${key(acc.segA)}`, { x: acc.segA.x, y: acc.segA.y, z }, Math.hypot(s.x - acc.segA.x, s.y - acc.segA.y))
        addMFEdge(g, nodeK, nodeP, `${s.floorId}:${key(acc.segB)}`, { x: acc.segB.x, y: acc.segB.y, z }, Math.hypot(s.x - acc.segB.x, s.y - acc.segB.y))
      } else {
        g.nodes.set(nodeK, nodeP)
      }
    }
    const sorted = placed.slice().sort((a, b) => a.z - b.z)
    for (let i = 0; i + 1 < sorted.length; i++) {
      const a = sorted[i]!, b = sorted[i + 1]!
      addMFEdge(g, mfKey(a.s.floorId, a.s), { x: a.s.x, y: a.s.y, z: a.z },
                   mfKey(b.s.floorId, b.s), { x: b.s.x, y: b.s.y, z: b.z }, Math.abs(a.z - b.z))
    }
  }
  return g
}

export interface MFStop { floorId: string; x: number; y: number }
export interface MFRoute { points: Pt3[]; totalDistance: number; degraded: boolean }

export function polyDist3(pts: Pt3[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += dist3(pts[i - 1]!, pts[i]!)
  return d
}

/** 取某层标高（从该层任一节点 z）。 */
function floorZ(g: MFGraph, fid: string): number {
  for (const [k, p] of g.nodes) if (k.startsWith(`${fid}:`)) return p.z
  return 0
}

/** 跨层相邻两拣货点：各端投影到本层巷道接入（临时 FA/FB），astar 跑多层图。不连通→直连 degraded。 */
export function pathBetweenMF(g: MFGraph, a: MFStop, b: MFStop): { points: Pt3[]; degraded: boolean } {
  const za = floorZ(g, a.floorId), zb = floorZ(g, b.floorId)
  const pa: Pt3 = { x: a.x, y: a.y, z: za }, pb: Pt3 = { x: b.x, y: b.y, z: zb }

  const accA = nearestAccessOnSegments(g.segments.filter((s) => s.floorId === a.floorId), { x: a.x, y: a.y })
  const accB = nearestAccessOnSegments(g.segments.filter((s) => s.floorId === b.floorId), { x: b.x, y: b.y })
  if (!accA || !accB) return { points: [pa, pb], degraded: true }

  const adj = new Map<string, Array<{ to: string; w: number }>>()
  for (const [k, list] of g.adj) adj.set(k, list.slice())
  const FA = 'FA', FB = 'FB'
  const link = (n: string, p: MFStop, segA: Pt, segB: Pt) => {
    const ka = `${p.floorId}:${key(segA)}`, kb = `${p.floorId}:${key(segB)}`
    adj.set(n, [{ to: ka, w: Math.hypot(p.x - segA.x, p.y - segA.y) }, { to: kb, w: Math.hypot(p.x - segB.x, p.y - segB.y) }])
    adj.get(ka)?.push({ to: n, w: Math.hypot(p.x - segA.x, p.y - segA.y) })
    adj.get(kb)?.push({ to: n, w: Math.hypot(p.x - segB.x, p.y - segB.y) })
  }
  link(FA, a, accA.segA, accA.segB)
  link(FB, b, accB.segA, accB.segB)

  const nodePt = (k: string): Pt3 => (k === FA ? pa : k === FB ? pb : g.nodes.get(k)!)
  const path = astar(adj, FA, FB, nodePt)
  if (!path) return { points: [pa, pb], degraded: true }
  return { points: path.map(nodePt), degraded: false }
}

export function distanceMatrixMF(g: MFGraph, stops: MFStop[], degradedPairs?: { count: number }): number[][] {
  const n = stops.length
  const m: number[][] = Array.from({ length: n }, () => new Array<number>(n).fill(0))
  for (let i = 0; i < n; i++) for (let j = i + 1; j < n; j++) {
    const seg = pathBetweenMF(g, stops[i]!, stops[j]!)
    const d = polyDist3(seg.points)
    m[i]![j] = d; m[j]![i] = d
    if (seg.degraded && degradedPairs) degradedPairs.count++
  }
  return m
}
