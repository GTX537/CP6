// cp6.web/src/space-viewer/advanced/pathModel.ts
import type { Pt } from './PickPathPlanner'

const seg = (a: Pt, b: Pt): number => Math.hypot(a.x - b.x, a.y - b.y)

export function polylineLength(pts: Pt[]): number {
  let d = 0
  for (let i = 1; i < pts.length; i++) d += seg(pts[i - 1]!, pts[i]!)
  return d
}

/** 沿折线弧长取点；d 钳制到 [0, 总长]。 */
export function pointAtDistance(pts: Pt[], d: number): Pt {
  if (pts.length === 0) return { x: 0, y: 0 }
  if (pts.length === 1 || d <= 0) return { ...pts[0]! }
  let remain = d
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1]!, b = pts[i]!
    const l = seg(a, b)
    if (remain <= l) {
      const t = l === 0 ? 0 : remain / l
      return { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t }
    }
    remain -= l
  }
  return { ...pts[pts.length - 1]! }
}
