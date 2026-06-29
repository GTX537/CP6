import { describe, it, expect } from 'vitest'
import { buildMultiFloorGraph, pathBetweenMF, distanceMatrixMF, polyDist3 } from './planMultiFloor'
import { planPickComparisonMF } from './planMultiFloor'

const F1 = 'F1', F2 = 'F2'
const floors = [{ floorId: F1, z: 0 }, { floorId: F2, z: 6000 }]
const aislesByFloor = new Map([
  [F1, [{ aisleCode: 'H1', centerline: '[[0,500],[1000,500]]' }]],
  [F2, [{ aisleCode: 'H2', centerline: '[[0,500],[1000,500]]' }]],
])
const connectors = [{ connectorCode: 'E1', type: 1, stops: [{ floorId: F1, x: 500, y: 500 }, { floorId: F2, x: 500, y: 500 }] }]

describe('buildMultiFloorGraph', () => {
  it('namespaces nodes per floor and adds a vertical connector edge of weight |Δz|', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    expect(g.nodes.has('F1:0,500')).toBe(true)
    expect(g.nodes.has('F2:0,500')).toBe(true)
    expect(g.nodes.has('F1:500,500')).toBe(true)
    expect(g.nodes.has('F2:500,500')).toBe(true)
    const up = g.adj.get('F1:500,500')!.find((e) => e.to === 'F2:500,500')
    expect(up).toBeTruthy()
    expect(up!.w).toBeCloseTo(6000)
  })
})

describe('pathBetweenMF', () => {
  it('routes across floors via the connector (path has a z change 0→6000)', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const r = pathBetweenMF(g, { floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 })
    expect(r.degraded).toBe(false)
    const zs = r.points.map((p) => p.z)
    expect(Math.min(...zs)).toBeCloseTo(0)
    expect(Math.max(...zs)).toBeCloseTo(6000)
  })
  it('distanceMatrixMF symmetric + includes vertical leg (>6000)', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const stops = [{ floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 }]
    const m = distanceMatrixMF(g, stops)
    expect(m[0]![1]).toBeCloseTo(m[1]![0]!)
    expect(m[0]![1]).toBeGreaterThan(6000)
  })
})

describe('planPickComparisonMF', () => {
  it('optimized never longer than actual; savings>=0; points carry z', () => {
    const stops = [
      { floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 },
      { floorId: F1, x: 900, y: 520 }, { floorId: F2, x: 100, y: 520 },
    ]
    const cmp = planPickComparisonMF(floors, aislesByFloor, connectors, stops)
    expect(cmp.order[0]).toBe(0)
    expect(cmp.optimizedMm).toBeLessThanOrEqual(cmp.actualMm + 1e-6)
    expect(cmp.savingsPct).toBeGreaterThanOrEqual(0)
    expect(cmp.actual.points.some((p) => p.z > 0)).toBe(true)
  })
  it('single stop -> zero, savings 0', () => {
    const cmp = planPickComparisonMF(floors, aislesByFloor, connectors, [{ floorId: F1, x: 100, y: 520 }])
    expect(cmp.actualMm).toBe(0)
    expect(cmp.savingsPct).toBe(0)
  })
})
