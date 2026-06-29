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
