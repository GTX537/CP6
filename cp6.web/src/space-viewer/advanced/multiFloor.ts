// cp6.web/src/space-viewer/advanced/multiFloor.ts —— 多层图基元（mm，floorId 命名空间）
import type { Pt } from './PickPathPlanner'

export interface Pt3 { x: number; y: number; z: number }
export interface FloorMeta { floorId: string; z: number }  // z=堆叠标高 mm

/** 多层节点键：楼层命名空间 + 1mm 取整 XY。 */
export const mfKey = (floorId: string, p: { x: number; y: number }): string =>
  `${floorId}:${Math.round(p.x)},${Math.round(p.y)}`

export const dist3 = (a: Pt3, b: Pt3): number => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z)

/** 把 2D 点 + 层 z 升 Pt3。 */
export const lift = (p: Pt, z: number): Pt3 => ({ x: p.x, y: p.y, z })
