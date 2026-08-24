import type {
  ISpaceCreateLayoutAisleDto,
  ISpaceCreateLayoutRackDto,
  ISpaceCreateLayoutZoneDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

export interface LayoutParentOption {
  logicalId: string
  code: string
  name?: string
  zoneLogicalId?: string
}

export type LayoutCreateIntent =
  | { type: 'CreateZone'; payload: ISpaceCreateLayoutZoneDto }
  | { type: 'CreateAisle'; payload: ISpaceCreateLayoutAisleDto }
  | { type: 'CreateRack'; payload: ISpaceCreateLayoutRackDto }

export function rectanglePolygonJson(
  x: number,
  y: number,
  width: number,
  depth: number,
): string {
  return JSON.stringify({
    schemaVersion: 1,
    points: [
      [x, y],
      [x + width, y],
      [x + width, y + depth],
      [x, y + depth],
    ],
  })
}

export function rectangleCenterlineJson(
  x: number,
  y: number,
  width: number,
  depth: number,
  direction: number,
): string {
  const points = direction === 2
    ? [
        [x + width / 2, y],
        [x + width / 2, y + depth],
      ]
    : [
        [x, y + depth / 2],
        [x + width, y + depth / 2],
      ]
  return JSON.stringify({ schemaVersion: 1, points })
}

export function rackLocationCount(
  levels: readonly Pick<
    ISpaceCreateLayoutRackDto['levels'][number],
    'binCount' | 'depthCount'
  >[],
): number {
  return levels.reduce(
    (total, level) => total + level.binCount * level.depthCount,
    0,
  )
}
