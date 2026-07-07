// zoneGeom — Zone 工具的纯几何（无 Konva 依赖，供单测）
import type { WorldRect } from '../select/lassoHit'

/**
 * 世界系轴对齐矩形 → 库区多边形四点。
 * 顺序 [x0,y0],[x1,y0],[x1,y1],[x0,y1]（首点不重复），mm，floor 局部系。
 * 与 FloorEditor.placementValid 的 corners 顺序一致。
 */
export function rectToPolygon(rect: WorldRect): [number, number][] {
  return [
    [rect.minX, rect.minY],
    [rect.maxX, rect.minY],
    [rect.maxX, rect.maxY],
    [rect.minX, rect.maxY],
  ]
}

/** 矩形短边长度（mm）——用于「太小」校验。 */
export function rectShortEdge(rect: WorldRect): number {
  return Math.min(rect.maxX - rect.minX, rect.maxY - rect.minY)
}
