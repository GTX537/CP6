import { genRack, type RackTemplate } from './genRack'
import type { RackVO, LocationVO, AisleVO } from '@/types/space/scene'

export interface ZoneArrayParams {
  rows: number
  racksPerRow: number
  rowGap: number
  rackGap: number
  aisleBetweenRows: boolean
  originX: number
  originY: number
  rotation: number
}

export function genZoneArray(
  tpl: RackTemplate,
  zoneId: string,
  floorId: string,
  params: ZoneArrayParams,
): { racks: RackVO[]; locs: LocationVO[]; aisles: AisleVO[] } {
  const racks: RackVO[] = []
  const locs: LocationVO[] = []
  const aisles: AisleVO[] = []

  const rackWidth = tpl.cols * tpl.cellW
  const rackDepth = tpl.depthCount * tpl.cellD

  for (let row = 0; row < params.rows; row++) {
    const rowY = params.originY + row * (rackDepth + params.rowGap)
    for (let r = 0; r < params.racksPerRow; r++) {
      const rackX = params.originX + r * (rackWidth + params.rackGap)
      const idx = row * params.racksPerRow + r + 1
      const rackCode = `R${idx.toString().padStart(3, '0')}`
      const { rack, locs: rackLocs } = genRack(tpl, zoneId, floorId, rackX, rowY, params.rotation, rackCode)
      racks.push(rack)
      locs.push(...rackLocs)
    }

    if (params.aisleBetweenRows && row < params.rows - 1) {
      const aisleY = rowY + rackDepth
      const aisleEndY = aisleY + params.rowGap
      const totalWidth = params.racksPerRow * rackWidth + (params.racksPerRow - 1) * params.rackGap
      const aisleStartX = params.originX
      const aisleEndX = params.originX + totalWidth
      const centerY = (aisleY + aisleEndY) / 2

      aisles.push({
        id: crypto.randomUUID(),
        zoneId,
        aisleCode: `A${(row + 1).toString().padStart(3, '0')}`,
        polygon: JSON.stringify([
          [aisleStartX, aisleY],
          [aisleEndX, aisleY],
          [aisleEndX, aisleEndY],
          [aisleStartX, aisleEndY],
        ]),
        centerline: JSON.stringify([
          [aisleStartX, centerY],
          [aisleEndX, centerY],
        ]),
      })
    }
  }

  return { racks, locs, aisles }
}
