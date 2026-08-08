import type {
  SpaceRuntimeTaskItem,
  SpaceRuntimeTaskPathResponse,
} from '@/types/space/runtime'
import { mmToSec } from './cost'
import { planPickComparison } from './PickPathPlanner'
import { planPickComparisonMF } from './planMultiFloor'

export interface RuntimeTaskPathPoint {
  x: number
  y: number
  z?: number
}

export interface RuntimeTaskPathPlan {
  actualPoints: RuntimeTaskPathPoint[]
  optimizedPoints: RuntimeTaskPathPoint[]
  optimizedStops: SpaceRuntimeTaskItem[]
  actualMillimeters: number
  optimizedMillimeters: number
  actualSeconds: number
  optimizedSeconds: number
  savingsPercent: number
  degraded: boolean
  optimizationBasis: 'distance' | 'time'
}

/**
 * Turns the authoritative runtime task sequence into a read-only what-if route.
 * The optimized order is presentation-only and is never written back to WMS.
 */
export function planRuntimeTaskPath(
  response: SpaceRuntimeTaskPathResponse,
): RuntimeTaskPathPlan | null {
  const stops = response.actualStops
  if (stops.length < 2 || stops.some(stop =>
    stop.anchorXMillimeters == null || stop.anchorYMillimeters == null)) {
    return null
  }

  if (response.crossFloor) {
    const floors = response.floors.map(floor => ({
      floorId: floor.floorLogicalId,
      level: floor.floorLevel,
      z: floor.elevationMillimeters,
    }))
    const aislesByFloor = new Map<string, Array<{ aisleCode: string; centerline: string }>>()
    for (const aisle of response.aisles) {
      const list = aislesByFloor.get(aisle.floorLogicalId) ?? []
      list.push({ aisleCode: aisle.aisleCode, centerline: aisle.centerlineJson })
      aislesByFloor.set(aisle.floorLogicalId, list)
    }
    const comparison = planPickComparisonMF(
      floors,
      aislesByFloor,
      [],
      stops.map(stop => ({
        floorId: stop.floorLogicalId,
        x: stop.anchorXMillimeters!,
        y: stop.anchorYMillimeters!,
      })),
    )
    return {
      actualPoints: comparison.actual.points,
      optimizedPoints: comparison.optimized.points,
      optimizedStops: comparison.order.map(index => stops[index]!),
      actualMillimeters: comparison.actualMm,
      optimizedMillimeters: comparison.optimizedMm,
      actualSeconds: comparison.actualSec,
      optimizedSeconds: comparison.optimizedSec,
      savingsPercent: comparison.timeSavingsPct,
      degraded: comparison.actual.degraded || comparison.degradedPairCount > 0,
      optimizationBasis: 'time',
    }
  }

  const floorId = stops[0]!.floorLogicalId
  const comparison = planPickComparison(
    response.aisles
      .filter(aisle => aisle.floorLogicalId === floorId)
      .map(aisle => ({ centerline: aisle.centerlineJson })),
    stops.map(stop => ({
      x: stop.anchorXMillimeters!,
      y: stop.anchorYMillimeters!,
    })),
  )
  return {
    actualPoints: comparison.actual.points,
    optimizedPoints: comparison.optimized.points,
    optimizedStops: comparison.order.map(index => stops[index]!),
    actualMillimeters: comparison.actualMm,
    optimizedMillimeters: comparison.optimizedMm,
    actualSeconds: mmToSec(comparison.actualMm),
    optimizedSeconds: mmToSec(comparison.optimizedMm),
    savingsPercent: comparison.savingsPct,
    degraded: comparison.actual.degraded || comparison.degradedPairCount > 0,
    optimizationBasis: 'distance',
  }
}
