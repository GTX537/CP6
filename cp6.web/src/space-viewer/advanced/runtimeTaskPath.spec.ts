import { describe, expect, it } from 'vitest'
import type {
  SpaceRuntimeTaskItem,
  SpaceRuntimeTaskPathResponse,
} from '@/types/space/runtime'
import { planRuntimeTaskPath } from './runtimeTaskPath'

const stop = (
  sequenceNo: number,
  floorLogicalId: string,
  x: number | null,
  y: number | null,
): SpaceRuntimeTaskItem => ({
  taskId: 'TASK-1', taskType: 'Pick', status: 'Released', sequenceNo,
  locationLogicalId: `L-${sequenceNo}`, wmsLogicalId: `W-${sequenceNo}`,
  spaceLocationCode: `LOC-${sequenceNo}`, wmsLocationCode: `LOC-${sequenceNo}`,
  codeMatches: true, floorLogicalId, floorCode: floorLogicalId,
  floorName: floorLogicalId, floorLevel: Number(floorLogicalId.slice(1)),
  zoneLogicalId: `Z-${floorLogicalId}`, zoneCode: `ZONE-${floorLogicalId}`,
  rackLogicalId: null, rackCode: null,
  anchorXMillimeters: x, anchorYMillimeters: y, anchorZMillimeters: 0,
  quantity: sequenceNo, materialNumber: `SKU-${sequenceNo}`,
})

const response = (
  actualStops: SpaceRuntimeTaskItem[],
  crossFloor = false,
): SpaceRuntimeTaskPathResponse => ({
  siteId: 'S', publishedVersionId: 'V', warehouseCode: 'WH',
  source: {
    kind: 'Real', adapterId: 'A', dataSourceId: 'D', observedAtUtc: '2026-08-01T00:00:00Z',
    receivedAtUtc: '2026-08-01T00:00:01Z', delayMilliseconds: 1000,
    clockSkewMilliseconds: 0, isSimulated: false, isAvailable: true,
  },
  taskId: 'TASK-1', stopCount: actualStops.length,
  locatedStopCount: actualStops.filter(item => item.anchorXMillimeters != null && item.anchorYMillimeters != null).length,
  floorCount: new Set(actualStops.map(item => item.floorLogicalId)).size,
  zoneCount: new Set(actualStops.map(item => item.zoneLogicalId)).size,
  floorTransitionCount: crossFloor ? 1 : 0, zoneTransitionCount: crossFloor ? 1 : 0,
  totalQuantity: actualStops.reduce((sum, item) => sum + (item.quantity ?? 0), 0),
  crossFloor, crossZone: crossFloor, actualStops,
  floors: [...new Set(actualStops.map(item => item.floorLogicalId))].map((floorId, index) => ({
    floorLogicalId: floorId, floorCode: floorId, floorName: floorId,
    floorLevel: index + 1, elevationMillimeters: index * 5000,
    heightMillimeters: 5000, stopCount: actualStops.filter(item => item.floorLogicalId === floorId).length,
    totalQuantity: 0,
  })),
  workloads: [],
  aisles: [...new Set(actualStops.map(item => item.floorLogicalId))].map(floorId => ({
    floorLogicalId: floorId, zoneLogicalId: `Z-${floorId}`, aisleLogicalId: `A-${floorId}`,
    aisleCode: `AISLE-${floorId}`, centerlineJson: '[[0,0],[10000,0]]',
  })),
})

describe('planRuntimeTaskPath', () => {
  it('plans actual and presentation-only optimized orders on one floor', () => {
    const plan = planRuntimeTaskPath(response([
      stop(1, 'F1', 0, 100),
      stop(2, 'F1', 9000, 100),
      stop(3, 'F1', 1000, 100),
    ]))

    expect(plan).not.toBeNull()
    expect(plan!.optimizationBasis).toBe('distance')
    expect(plan!.optimizedSeconds).toBeLessThanOrEqual(plan!.actualSeconds)
    expect(plan!.optimizedStops).toHaveLength(3)
  })

  it('uses multi-floor time planning and marks missing connector topology as degraded', () => {
    const plan = planRuntimeTaskPath(response([
      stop(1, 'F1', 0, 100),
      stop(2, 'F2', 9000, 100),
    ], true))

    expect(plan).not.toBeNull()
    expect(plan!.optimizationBasis).toBe('time')
    expect(plan!.degraded).toBe(true)
  })

  it('does not invent a route when any authoritative stop lacks coordinates', () => {
    expect(planRuntimeTaskPath(response([
      stop(1, 'F1', 0, 100),
      stop(2, 'F1', null, null),
    ]))).toBeNull()
  })
})
