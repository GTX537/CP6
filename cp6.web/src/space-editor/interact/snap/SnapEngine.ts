// SnapEngine — 捕捉吸附引擎（ch02 §6）
// 纯逻辑，不引 Konva；上层工具（DragTool）调用后包成 BatchCmd
import type { RackVO, AisleVO } from '@/types/space/scene'

export interface SnapContext {
  zoom: number       // px/mm（同 ViewState.zoom）
  racks: RackVO[]
  aisles: AisleVO[]
}

export interface SnapResult {
  x: number
  y: number
  snapped: boolean
}

interface Candidate {
  x: number
  y: number
}

// ─── 辅助：点到候选距离 ────────────────────────────────────────────────────────
function dist2(ax: number, ay: number, bx: number, by: number): number {
  const dx = ax - bx, dy = ay - by
  return dx * dx + dy * dy
}

// ─── 辅助：货架的 4 个世界角（无旋转的 OBB 角，供边角吸附） ──────────────────
function rackCorners(r: RackVO): Candidate[] {
  const W = r.cols * r.cellW
  const D = r.depthCount * r.cellD
  const th = (r.rotationZ * Math.PI) / 180
  const cos = Math.cos(th), sin = Math.sin(th)
  const locals: [number, number][] = [
    [0, 0], [W, 0], [W, D], [0, D],
  ]
  return locals.map(([lx, ly]) => ({
    x: r.x + lx * cos - ly * sin,
    y: r.y + lx * sin + ly * cos,
  }))
}

// ─── 辅助：解析巷道中心线中点作候选 ─────────────────────────────────────────
function aisleCenterCandidates(aisles: AisleVO[]): Candidate[] {
  const candidates: Candidate[] = []
  for (const a of aisles) {
    try {
      const pts = JSON.parse(a.centerline) as [number, number][]
      if (pts.length >= 2) {
        // 取首末中点
        const mid: Candidate = {
          x: (pts[0][0] + pts[pts.length - 1][0]) / 2,
          y: (pts[0][1] + pts[pts.length - 1][1]) / 2,
        }
        candidates.push(mid)
      }
    } catch {
      // 忽略解析失败的巷道
    }
  }
  return candidates
}

export class SnapEngine {
  private snapStep: number    // mm
  private thresholdPx: number // screen px

  constructor({ snapStep, thresholdPx = 8 }: { snapStep: number; thresholdPx?: number }) {
    this.snapStep = snapStep
    this.thresholdPx = thresholdPx
  }

  /**
   * 对给定世界坐标点进行吸附判定。
   * 候选优先级：货架边角 / 巷道中心线 / 网格交点。
   * 返回 argmin 距离候选；若距离 < 阈值则 snapped=true，否则返回原坐标。
   */
  snap(point: { x: number; y: number }, ctx: SnapContext): SnapResult {
    const thresholdMm = this.thresholdPx / ctx.zoom

    const candidates: Candidate[] = []

    // 1. 货架边角（4 角 × 货架数）
    for (const rack of ctx.racks) {
      candidates.push(...rackCorners(rack))
    }

    // 2. 巷道中心线候选
    candidates.push(...aisleCenterCandidates(ctx.aisles))

    // 3. 网格交点（在 point 附近 ±1 格采样 9 个候选）
    const s = this.snapStep
    const gx = Math.round(point.x / s) * s
    const gy = Math.round(point.y / s) * s
    for (let di = -1; di <= 1; di++) {
      for (let dj = -1; dj <= 1; dj++) {
        candidates.push({ x: gx + di * s, y: gy + dj * s })
      }
    }

    // argmin
    let bestDist2 = Infinity
    let best: Candidate = { x: point.x, y: point.y }
    for (const c of candidates) {
      const d2 = dist2(point.x, point.y, c.x, c.y)
      if (d2 < bestDist2) {
        bestDist2 = d2
        best = c
      }
    }

    if (bestDist2 < thresholdMm * thresholdMm) {
      return { x: best.x, y: best.y, snapped: true }
    }
    return { x: point.x, y: point.y, snapped: false }
  }

  /**
   * 等距分布：给定 n 个点的坐标，沿 axis 方向均匀分布（首末位置不变）。
   * 返回每个点在 axis 方向的偏移量（供上层组 BatchCmd）。
   */
  distributeEqual(
    positions: { x: number; y: number }[],
    axis: 'x' | 'y',
  ): number[] {
    if (positions.length < 2) return positions.map(() => 0)

    const vals = positions.map(p => p[axis])
    const sorted = [...vals].sort((a, b) => a - b)
    const min = sorted[0]
    const max = sorted[sorted.length - 1]
    const step = (max - min) / (positions.length - 1)

    // 按当前值排序：第 i 小的应移到 min + i*step
    const sortedIdx = vals
      .map((v, i) => ({ v, i }))
      .sort((a, b) => a.v - b.v)

    const offsets = new Array<number>(positions.length).fill(0)
    sortedIdx.forEach(({ i }, rank) => {
      offsets[i] = min + rank * step - vals[i]
    })
    return offsets
  }
}
