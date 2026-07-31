import type {
  ISpaceSceneLocationDto,
  ISpaceSceneRackLevelDto,
  ISpaceWmsAdoptionDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

export interface RackCell {
  column: number
  level: number
  depth: number
}

export function prefillRackBindings(
  adoptions: readonly ISpaceWmsAdoptionDto[],
  locations: readonly ISpaceSceneLocationDto[],
  rackLogicalId: string,
): Record<string, string> {
  const candidates = adoptions
    .filter(
      (item) =>
        item.id &&
        item.status === 'Unbound' &&
        item.wmsIsActive !== false &&
        !item.locationLogicalId,
    )
    .slice()
    .sort((left, right) =>
      (left.wmsLocationCode ?? '').localeCompare(
        right.wmsLocationCode ?? '',
      ),
    )
  const geometry = activeRackLocations(locations, rackLogicalId)
    .filter(
      (location) =>
        location.externalBindingState === 'Unbound' &&
        location.revision?.logicalId,
    )
    .slice()
    .sort(compareLocation)
  const result: Record<string, string> = {}
  for (let index = 0; index < Math.min(candidates.length, geometry.length); index++) {
    result[candidates[index]!.id!] = geometry[index]!.revision!.logicalId!
  }
  return result
}

export function findFirstEmptyRackCell(
  rackLogicalId: string,
  levels: readonly ISpaceSceneRackLevelDto[],
  locations: readonly ISpaceSceneLocationDto[],
): RackCell | null {
  const occupied = new Set(
    activeRackLocations(locations, rackLogicalId).map(
      (location) =>
        `${location.levelNo ?? 0}:${location.columnNo ?? 0}:` +
        `${location.depthNo ?? 0}`,
    ),
  )
  const activeLevels = levels
    .filter(
      (level) =>
        level.rackLogicalId === rackLogicalId &&
        level.revision?.lifecycleState === 'Active',
    )
    .slice()
    .sort((left, right) => (left.levelNo ?? 0) - (right.levelNo ?? 0))
  for (const level of activeLevels) {
    const levelNo = level.levelNo ?? 0
    for (let column = 1; column <= (level.binCount ?? 0); column++) {
      for (let depth = 1; depth <= (level.depthCount ?? 0); depth++) {
        if (!occupied.has(`${levelNo}:${column}:${depth}`)) {
          return { column, level: levelNo, depth }
        }
      }
    }
  }
  return null
}

export function activeRackLocations(
  locations: readonly ISpaceSceneLocationDto[],
  rackLogicalId: string,
): ISpaceSceneLocationDto[] {
  return locations.filter(
    (location) =>
      location.rackLogicalId === rackLogicalId &&
      location.revision?.lifecycleState === 'Active',
  )
}

function compareLocation(
  left: ISpaceSceneLocationDto,
  right: ISpaceSceneLocationDto,
): number {
  return (
    (left.levelNo ?? 0) - (right.levelNo ?? 0) ||
    (left.columnNo ?? 0) - (right.columnNo ?? 0) ||
    (left.depthNo ?? 0) - (right.depthNo ?? 0) ||
    (left.locationCode ?? '').localeCompare(right.locationCode ?? '')
  )
}
