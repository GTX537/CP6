// cp6.web/src/space-viewer/advanced/planMultiFloor.ts —— 多层图 + 跨层路径（承 SP4，边权=时间秒，SP5）
import { buildCenterlineGraph, key, astar, type Pt } from './PickPathPlanner'
import { mfKey, dist3, type Pt3, type FloorMeta } from './multiFloor'
import { optimizeOrder, routeLengthByOrder } from './routeOptimize'
import { mmToSec, verticalSec, WALK_SPEED_MMPS } from './cost'

export interface MFGraph {
  nodes: Map<string, Pt3>
  adj: Map<string, Array<{ to: string; w: number }>>   // w = 时间(秒)
  segments: Array<{ a: Pt; b: Pt; floorId: string }>
  floorZ: Map<string, number>
  floorLevel: Map<string, number>
  hScale: number                                        // Kmin = 全图 min(边时间 / 边真3D长)，A* admissible 标定（h = 3D欧氏距 × hScale）
}
export interface AisleVOLite { aisleCode: string; centerline: string }
export interface ConnectorPath {
  connectorCode: string; type: number
  waitSec: number; travelSecPerFloor: number
  stops: Array<{ floorId: string; x: number; y: number }>
}

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

/** 合并各层 SP3 子图（边权 mm→秒）+ 连接体接入（水平秒）+ 同连接体相邻层竖直边（verticalSec）。
 *  hScale = Kmin = 全图 min(边时间/边物理长)。 */
export function buildMultiFloorGraph(
  floors: FloorMeta[],
  aislesByFloor: Map<string, AisleVOLite[]>,
  connectors: ConnectorPath[],
): MFGraph {
  const zOf = new Map(floors.map((f) => [f.floorId, f.z]))
  const levelOf = new Map(floors.map((f) => [f.floorId, f.level]))
  const g: MFGraph = { nodes: new Map(), adj: new Map(), segments: [], floorZ: zOf, floorLevel: levelOf, hScale: 1 / WALK_SPEED_MMPS }
  let minRate = 1 / WALK_SPEED_MMPS // 水平边 rate 恒定，作 Kmin 起点

  // 1) 各层 SP3 子图（mm 边权）→ 前缀合并 + mm→秒
  for (const f of floors) {
    const z = f.z
    const g2d = buildCenterlineGraph(aislesByFloor.get(f.floorId) ?? [])
    for (const [k2d, pt] of g2d.nodes) g.nodes.set(`${f.floorId}:${k2d}`, { x: pt.x, y: pt.y, z })
    for (const [k2d, list] of g2d.adj) g.adj.set(`${f.floorId}:${k2d}`, list.map((e) => ({ to: `${f.floorId}:${e.to}`, w: mmToSec(e.w) })))
    for (const s of g2d.segments) g.segments.push({ a: s.a, b: s.b, floorId: f.floorId })
  }

  // 2) 连接体：每 stop 接入本层最近巷道（水平秒）；同连接体相邻层竖直边（verticalSec）
  for (const c of connectors) {
    const placed = c.stops.filter((s) => zOf.has(s.floorId)).map((s) => ({ s, z: zOf.get(s.floorId)!, level: levelOf.get(s.floorId)! }))
    for (const { s, z } of placed) {
      const floorSegs = g.segments.filter((seg) => seg.floorId === s.floorId)
      const acc = nearestAccessOnSegments(floorSegs, { x: s.x, y: s.y })
      const nodeK = mfKey(s.floorId, s)
      const nodeP: Pt3 = { x: s.x, y: s.y, z }
      if (acc) {
        const dA = Math.hypot(s.x - acc.segA.x, s.y - acc.segA.y)
        const dB = Math.hypot(s.x - acc.segB.x, s.y - acc.segB.y)
        addMFEdge(g, nodeK, nodeP, `${s.floorId}:${key(acc.segA)}`, { x: acc.segA.x, y: acc.segA.y, z }, mmToSec(dA))
        addMFEdge(g, nodeK, nodeP, `${s.floorId}:${key(acc.segB)}`, { x: acc.segB.x, y: acc.segB.y, z }, mmToSec(dB))
      } else {
        g.nodes.set(nodeK, nodeP)
      }
    }
    const sorted = placed.slice().sort((a, b) => a.z - b.z)
    for (let i = 0; i + 1 < sorted.length; i++) {
      const a = sorted[i]!, b = sorted[i + 1]!
      const span = Math.abs(a.level - b.level)
      const w = verticalSec(c.waitSec, c.travelSecPerFloor, span)
      const physLen = dist3({ x: a.s.x, y: a.s.y, z: a.z }, { x: b.s.x, y: b.s.y, z: b.z }) // 真 3D 边长（含 xy 位移，倾斜连接体 admissible）
      if (physLen > 0) minRate = Math.min(minRate, w / physLen)
      addMFEdge(g, mfKey(a.s.floorId, a.s), { x: a.s.x, y: a.s.y, z: a.z },
                   mfKey(b.s.floorId, b.s), { x: b.s.x, y: b.s.y, z: b.z }, w)
    }
  }
  g.hScale = minRate
  return g
}

export interface MFStop { floorId: string; x: number; y: number }
export interface MFRoute { points: Pt3[]; totalDistance: number; totalTime: number; degraded: boolean }

export function polyDist3(pts: Pt3[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += dist3(pts[i - 1]!, pts[i]!)
  return d
}

/** 取某层标高（O(1) 查 g.floorZ；含无巷道/无 stop 的层，避免退化端点落 z=0）。 */
function zOfFloor(g: MFGraph, fid: string): number {
  return g.floorZ.get(fid) ?? 0
}

/** 沿 astar 返回的 key 序累计边时间（adj 含临时 FA/FB）。 */
function pathCost(adj: Map<string, Array<{ to: string; w: number }>>, keys: string[]): number {
  let c = 0
  for (let i = 0; i + 1 < keys.length; i++) {
    const e = adj.get(keys[i]!)?.find((x) => x.to === keys[i + 1]!)
    if (e) c += e.w
  }
  return c
}

/** 跨层相邻两拣货点：各端投影本层巷道接入（FA/FB），astar 跑多层时间图。不连通→直连 degraded（时间=直线÷步速）。 */
export function pathBetweenMF(g: MFGraph, a: MFStop, b: MFStop): { points: Pt3[]; time: number; degraded: boolean } {
  const za = zOfFloor(g, a.floorId), zb = zOfFloor(g, b.floorId)
  const pa: Pt3 = { x: a.x, y: a.y, z: za }, pb: Pt3 = { x: b.x, y: b.y, z: zb }

  const accA = nearestAccessOnSegments(g.segments.filter((s) => s.floorId === a.floorId), { x: a.x, y: a.y })
  const accB = nearestAccessOnSegments(g.segments.filter((s) => s.floorId === b.floorId), { x: b.x, y: b.y })
  if (!accA || !accB) return { points: [pa, pb], time: mmToSec(dist3(pa, pb)), degraded: true }

  const adj = new Map<string, Array<{ to: string; w: number }>>()
  for (const [k, list] of g.adj) adj.set(k, list.slice())
  const FA = 'FA', FB = 'FB'
  const link = (n: string, p: MFStop, segA: Pt, segB: Pt) => {
    const ka = `${p.floorId}:${key(segA)}`, kb = `${p.floorId}:${key(segB)}`
    adj.set(n, [{ to: ka, w: mmToSec(Math.hypot(p.x - segA.x, p.y - segA.y)) }, { to: kb, w: mmToSec(Math.hypot(p.x - segB.x, p.y - segB.y)) }])
    adj.get(ka)?.push({ to: n, w: mmToSec(Math.hypot(p.x - segA.x, p.y - segA.y)) })
    adj.get(kb)?.push({ to: n, w: mmToSec(Math.hypot(p.x - segB.x, p.y - segB.y)) })
  }
  link(FA, a, accA.segA, accA.segB)
  link(FB, b, accB.segA, accB.segB)

  const nodePt = (k: string): Pt3 => (k === FA ? pa : k === FB ? pb : g.nodes.get(k)!)
  const path = astar(adj, FA, FB, nodePt, g.hScale)
  if (!path) return { points: [pa, pb], time: mmToSec(dist3(pa, pb)), degraded: true }
  return { points: path.map(nodePt), time: pathCost(adj, path), degraded: false }
}

/** 拣货点两两时间矩阵（秒；degraded 段记直线÷步速）。对称。 */
export function costMatrixMF(g: MFGraph, stops: MFStop[], degradedPairs?: { count: number }): number[][] {
  const n = stops.length
  const m: number[][] = Array.from({ length: n }, () => new Array<number>(n).fill(0))
  for (let i = 0; i < n; i++) for (let j = i + 1; j < n; j++) {
    const seg = pathBetweenMF(g, stops[i]!, stops[j]!)
    m[i]![j] = seg.time; m[j]![i] = seg.time
    if (seg.degraded && degradedPairs) degradedPairs.count++
  }
  return m
}

export interface MFComparison {
  actual: MFRoute; optimized: MFRoute; order: number[]
  actualMm: number; optimizedMm: number          // 距离（几何，参考）
  actualSec: number; optimizedSec: number        // 时间（优化目标）
  timeSavingsPct: number; degradedPairCount: number
}

function planRouteOnMFGraph(g: MFGraph, stops: MFStop[]): MFRoute {
  if (stops.length < 2) {
    return { points: stops.map((s) => ({ x: s.x, y: s.y, z: zOfFloor(g, s.floorId) })), totalDistance: 0, totalTime: 0, degraded: false }
  }
  const points: Pt3[] = []
  let degraded = false, totalTime = 0
  for (let i = 0; i + 1 < stops.length; i++) {
    const seg = pathBetweenMF(g, stops[i]!, stops[i + 1]!)
    degraded = degraded || seg.degraded
    totalTime += seg.time
    const pts = i === 0 ? seg.points : seg.points.slice(1)
    points.push(...pts)
  }
  return { points, totalDistance: polyDist3(points), totalTime, degraded }
}

/** what-if 跨层重排对比：actual=LineNo 序，optimized=NN+2opt（时间矩阵，以 actual 为 baseline 兜底，强保证 ≤ actual 时间）。 */
export function planPickComparisonMF(
  floors: FloorMeta[], aislesByFloor: Map<string, AisleVOLite[]>, connectors: ConnectorPath[], stops: MFStop[],
): MFComparison {
  const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
  const actual = planRouteOnMFGraph(g, stops)
  if (stops.length < 2) {
    return { actual, optimized: actual, order: stops.map((_, i) => i), actualMm: actual.totalDistance, optimizedMm: actual.totalDistance, actualSec: actual.totalTime, optimizedSec: actual.totalTime, timeSavingsPct: 0, degradedPairCount: 0 }
  }
  const degradedPairs = { count: 0 }
  const matrix = costMatrixMF(g, stops, degradedPairs)
  const actualOrder = stops.map((_, i) => i)
  const candidate = optimizeOrder(matrix)
  const order = routeLengthByOrder(matrix, candidate) + 1e-9 < routeLengthByOrder(matrix, actualOrder) ? candidate : actualOrder
  const optimized = planRouteOnMFGraph(g, order.map((i) => stops[i]!))
  const actualSec = actual.totalTime, optimizedSec = optimized.totalTime
  const timeSavingsPct = actualSec === 0 ? 0 : Math.max(0, ((actualSec - optimizedSec) / actualSec) * 100)
  return { actual, optimized, order, actualMm: actual.totalDistance, optimizedMm: optimized.totalDistance, actualSec, optimizedSec, timeSavingsPct, degradedPairCount: degradedPairs.count }
}
