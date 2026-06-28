// rotateGeometry — 旋转纯逻辑（SP2 ①②）：绕几何中心回算锚点 + 角度吸附
// 不引 Konva。坐标约定与 CollisionHint.rackCorners / coords.computeAbs 一致：
//   x' = x·cosθ − y·sinθ ; y' = x·sinθ + y·cosθ（θ = rotationZ，世界坐标 mm）

const SNAP_STEP = 15      // 度
const SNAP_THRESHOLD = 3  // ±度

interface RackPoseDims {
  x: number
  y: number
  rotationZ: number
  cols: number
  cellW: number
  depthCount: number
  cellD: number
}

function rotateVec(deg: number, px: number, py: number): { x: number; y: number } {
  const th = (deg * Math.PI) / 180
  const cos = Math.cos(th), sin = Math.sin(th)
  return { x: px * cos - py * sin, y: px * sin + py * cos }
}

/**
 * 保持货架几何中心不变，把 rotationZ 改为 newRotationZ，返回新锚点 (x,y)。
 * C = anchor + R(rotationZ)·(W/2,D/2) ；anchor' = C − R(newRotationZ)·(W/2,D/2)
 */
export function rotateAboutCenter(rack: RackPoseDims, newRotationZ: number): { x: number; y: number } {
  const W = rack.cols * rack.cellW
  const D = rack.depthCount * rack.cellD
  const r0 = rotateVec(rack.rotationZ, W / 2, D / 2)
  const cx = rack.x + r0.x, cy = rack.y + r0.y
  const r1 = rotateVec(newRotationZ, W / 2, D / 2)
  return { x: cx - r1.x, y: cy - r1.y }
}

/** 把角度吸附到 15° 倍数（±3° 阈内，含环绕），返回 [0,360) 规范化角。 */
export function snapAngle(deg: number): number {
  const normalized = ((deg % 360) + 360) % 360
  const nearest = Math.round(normalized / SNAP_STEP) * SNAP_STEP
  const delta = Math.abs(normalized - nearest)
  if (delta <= SNAP_THRESHOLD || delta >= 360 - SNAP_THRESHOLD) {
    return nearest % 360
  }
  return normalized
}
