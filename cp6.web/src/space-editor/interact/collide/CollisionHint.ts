// CollisionHint — OBB+SAT 碰撞与越界判定（ch02 §8）
// 纯逻辑，不引 Konva；上层渲染层读结果着色
import type { RackVO, ZoneVO } from '@/types/space/scene'

// ─── 类型 ─────────────────────────────────────────────────────────────────────

export interface Vec2 { x: number; y: number }

// ─── OBB 角点计算 ─────────────────────────────────────────────────────────────

/**
 * 返回货架 OBB 的 4 个世界角（顺序：锚点、+W、+W+D、+D）。
 * 锚点 (rack.x, rack.y) 是 OBB 原点角；旋转绕锚点。
 */
export function rackCorners(r: RackVO): Vec2[] {
  const W = r.cols * r.cellW
  const D = r.depthCount * r.cellD
  const th = (r.rotationZ * Math.PI) / 180
  const cos = Math.cos(th), sin = Math.sin(th)
  // 局部坐标 → 世界坐标
  const transform = (lx: number, ly: number): Vec2 => ({
    x: r.x + lx * cos - ly * sin,
    y: r.y + lx * sin + ly * cos,
  })
  return [
    transform(0, 0),
    transform(W, 0),
    transform(W, D),
    transform(0, D),
  ]
}

// ─── SAT 投影工具 ─────────────────────────────────────────────────────────────

/** 把一组点投影到轴向量上，返回 [min, max] */
export function project(points: Vec2[], axis: Vec2): [number, number] {
  let min = Infinity, max = -Infinity
  for (const p of points) {
    const dot = p.x * axis.x + p.y * axis.y
    if (dot < min) min = dot
    if (dot > max) max = dot
  }
  return [min, max]
}

/** 两区间严格分离（不含紧贴） */
export function separated(a: [number, number], b: [number, number]): boolean {
  return a[1] <= b[0] || b[1] <= a[0]
}

// ─── OBB+SAT 相交判定 ─────────────────────────────────────────────────────────

/**
 * 判断两个旋转货架的 OBB 是否相交（SAT，4 轴：各自的局部 X/Y 方向）。
 * 返回 true 表示相交（碰撞），false 表示分离（含紧贴视为分离）。
 */
export function obbIntersect(a: RackVO, b: RackVO): boolean {
  const cornersA = rackCorners(a)
  const cornersB = rackCorners(b)

  // 4 个分离轴：rackA 的局部 X/Y + rackB 的局部 X/Y
  const thA = (a.rotationZ * Math.PI) / 180
  const thB = (b.rotationZ * Math.PI) / 180

  const axes: Vec2[] = [
    { x: Math.cos(thA), y: Math.sin(thA) },   // A 的局部 X
    { x: -Math.sin(thA), y: Math.cos(thA) },  // A 的局部 Y
    { x: Math.cos(thB), y: Math.sin(thB) },   // B 的局部 X
    { x: -Math.sin(thB), y: Math.cos(thB) },  // B 的局部 Y
  ]

  for (const axis of axes) {
    const projA = project(cornersA, axis)
    const projB = project(cornersB, axis)
    if (separated(projA, projB)) return false  // 找到分离轴 → 不相交
  }
  return true  // 所有轴均重叠 → 相交
}

// ─── 射线法：点在多边形内 ─────────────────────────────────────────────────────

/**
 * 判断点 (px, py) 是否在多边形 poly 内（射线法）。
 * poly 为顶点数组 [[x,y], ...]，支持凸/凹多边形。
 */
export function pointInPolygon(px: number, py: number, poly: [number, number][]): boolean {
  const n = poly.length
  let inside = false
  for (let i = 0, j = n - 1; i < n; j = i++) {
    const pi = poly[i]
    const pj = poly[j]
    if (!pi || !pj) continue
    const xi = pi[0], yi = pi[1]
    const xj = pj[0], yj = pj[1]
    const intersect =
      yi > py !== yj > py &&
      px < ((xj - xi) * (py - yi)) / (yj - yi) + xi
    if (intersect) inside = !inside
  }
  return inside
}

// ─── 货架是否全在 Zone 多边形内 ──────────────────────────────────────────────

/**
 * 判断货架 4 个角是否全在 Zone 多边形内。
 * zone.polygon 须是 JSON 序列化的 [[x,y],...] 数组。
 * 任一角出界 → false（越界）。
 */
export function rackInZone(rack: RackVO, zone: ZoneVO): boolean {
  let poly: [number, number][]
  try {
    poly = JSON.parse(zone.polygon) as [number, number][]
  } catch {
    return false  // polygon 格式错误视为越界
  }
  const corners = rackCorners(rack)
  return corners.every(c => pointInPolygon(c.x, c.y, poly))
}

// ─── 批量碰撞扫描（供上层实时着色） ─────────────────────────────────────────

export interface CollisionResult {
  rackId: string
  /** 与哪些货架发生碰撞 */
  collidingWith: string[]
  /** 是否越出区域 */
  outOfZone: boolean
}

/**
 * 对场景中所有货架做 O(n²) 碰撞检测 + 越界检测，返回有碰撞/越界的结果集。
 * n 较小（<200）时足够实时；大规模可改空间分桶。
 */
export function scanCollisions(
  racks: RackVO[],
  zones: ZoneVO[],
): CollisionResult[] {
  const results: CollisionResult[] = []

  for (let i = 0; i < racks.length; i++) {
    const a = racks[i]
    if (!a) continue
    const collidingWith: string[] = []

    for (let j = i + 1; j < racks.length; j++) {
      const b = racks[j]
      if (!b) continue
      if (obbIntersect(a, b)) {
        collidingWith.push(b.id)
        // 反向记录（避免 O(n²) 结果对）
        const existing = results.find(r => r.rackId === b.id)
        if (existing) {
          existing.collidingWith.push(a.id)
        }
      }
    }

    // 越界：找到对应 zone，若无 zone 视为越界
    const zone = zones.find(z => z.id === a.zoneId)
    const outOfZone = zone ? !rackInZone(a, zone) : true

    if (collidingWith.length > 0 || outOfZone) {
      results.push({ rackId: a.id, collidingWith, outOfZone })
    }
  }

  return results
}
