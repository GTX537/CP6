// 坐标映射纯函数 — 镜像后端 LocationGeometryService.ComputeAbs（ch00 §6.1）

export interface ViewState {
  panX: number
  panY: number
  zoom: number   // px / mm
  height: number // stage height in px
}

export interface XY { x: number; y: number }

/** World mm → screen px（Y 翻转：world +Y 向上，screen +Y 向下） */
export function worldToScreen(w: XY, v: ViewState): XY {
  return {
    x: (w.x - v.panX) * v.zoom,
    y: v.height - (w.y - v.panY) * v.zoom,
  }
}

/** Screen px → world mm（worldToScreen 的逆变换） */
export function screenToWorld(s: XY, v: ViewState): XY {
  return {
    x: s.x / v.zoom + v.panX,
    y: (v.height - s.y) / v.zoom + v.panY,
  }
}

/** 镜像后端 ComputeAbs：货架锚点 + 格口 col/level/depth → 绝对世界坐标（mm） */
export function computeAbs(
  rack: { x: number; y: number; z: number; rotationZ: number; cellW: number; cellH: number; cellD: number },
  col: number,
  level: number,
  depth: number,
): { x: number; y: number; z: number } {
  const lx = (col - 0.5) * rack.cellW
  const lz = (level - 0.5) * rack.cellH
  const ly = (depth - 0.5) * rack.cellD
  const th = (rack.rotationZ * Math.PI) / 180
  const cos = Math.cos(th)
  const sin = Math.sin(th)
  return {
    x: rack.x + Math.round(lx * cos - ly * sin),
    y: rack.y + Math.round(lx * sin + ly * cos),
    z: rack.z + Math.round(lz),
  }
}
