import { describe, expect, it } from 'vitest'
import {
  findFirstEmptyRackCell,
  prefillRackBindings,
} from './wmsAdoptionPlan'
import type {
  ISpaceSceneLocationDto,
  ISpaceSceneRackLevelDto,
  ISpaceWmsAdoptionDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

describe('WMS adoption planning', () => {
  it('prefills reviewable bindings in stable code and cell order', () => {
    const adoptions = [
      { id: 'wms-b', status: 'Unbound', wmsLocationCode: 'B' },
      { id: 'wms-a', status: 'Unbound', wmsLocationCode: 'A' },
      { id: 'bound', status: 'Bound', wmsLocationCode: 'C' },
    ] as ISpaceWmsAdoptionDto[]
    const locations = [
      location('location-2', 2, 1, 1),
      location('location-1', 1, 2, 1),
      location('location-0', 1, 1, 1),
    ]

    expect(prefillRackBindings(adoptions, locations, 'rack-1')).toEqual({
      'wms-a': 'location-0',
      'wms-b': 'location-1',
    })
  })

  it('finds the first active unoccupied rack cell', () => {
    const levels = [
      {
        rackLogicalId: 'rack-1',
        levelNo: 1,
        binCount: 2,
        depthCount: 2,
        revision: { lifecycleState: 'Active' },
      },
    ] as ISpaceSceneRackLevelDto[]
    const locations = [
      location('location-1', 1, 1, 1),
      location('location-2', 1, 1, 2),
      location('retired', 1, 2, 1, 'Retired'),
    ]

    expect(findFirstEmptyRackCell('rack-1', levels, locations)).toEqual({
      column: 2,
      level: 1,
      depth: 1,
    })
  })
})

function location(
  logicalId: string,
  levelNo: number,
  columnNo: number,
  depthNo: number,
  lifecycleState = 'Active',
): ISpaceSceneLocationDto {
  return {
    rackLogicalId: 'rack-1',
    levelNo,
    columnNo,
    depthNo,
    externalBindingState: 'Unbound',
    revision: {
      logicalId,
      lifecycleState,
    },
  } as ISpaceSceneLocationDto
}
