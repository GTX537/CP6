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
