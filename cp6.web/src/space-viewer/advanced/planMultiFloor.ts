// cp6.web/src/space-viewer/advanced/planMultiFloor.ts —— 多层图 + 跨层路径（承 SP3，图在前端）
import { buildCenterlineGraph, key, type Pt } from './PickPathPlanner'
import { mfKey, type Pt3, type FloorMeta } from './multiFloor'

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
