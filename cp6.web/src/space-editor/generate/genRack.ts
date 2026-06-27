import { computeAbs } from '../coords'
import type { RackVO, LocationVO } from '@/types/space/scene'

export interface RackTemplate {
  id: string
  cols: number
  levels: number
  depthCount: number
  cellW: number
  cellH: number
  cellD: number
}

export function genRack(
  tpl: RackTemplate,
  zoneId: string,
  floorId: string,
  originX: number,
  originY: number,
  rotation: number,
  rackCode: string,
): { rack: RackVO; locs: LocationVO[] } {
  const rack: RackVO = {
    id: crypto.randomUUID(),
    zoneId,
    floorId,
    templateId: tpl.id,
    rackCode,
    x: originX,
    y: originY,
    z: 0,
    rotationZ: rotation,
    cols: tpl.cols,
    levels: tpl.levels,
    depthCount: tpl.depthCount,
    cellW: tpl.cellW,
    cellH: tpl.cellH,
    cellD: tpl.cellD,
  }

  const locs: LocationVO[] = []
  for (let c = 1; c <= tpl.cols; c++) {
    for (let l = 1; l <= tpl.levels; l++) {
      for (let d = 1; d <= tpl.depthCount; d++) {
        const a = computeAbs(rack, c, l, d)
        locs.push({
          id: crypto.randomUUID(),
          rackId: rack.id,
          floorId,
          locationCode: null,
          codeOrigin: 1,
          col: c,
          level: l,
          depth: d,
          absX: a.x,
          absY: a.y,
          absZ: a.z,
          sizeW: tpl.cellW,
          sizeH: tpl.cellH,
          sizeD: tpl.cellD,
          placed: true,
          status: 0,
          version: 0,
        })
      }
    }
  }

  return { rack, locs }
}
