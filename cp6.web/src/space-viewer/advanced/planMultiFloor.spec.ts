import { describe, it, expect } from 'vitest'
import { buildMultiFloorGraph, pathBetweenMF, costMatrixMF, planPickComparisonMF } from './planMultiFloor'
import { mmToSec, verticalSec, WALK_SPEED_MMPS } from './cost'

const F1 = 'F1', F2 = 'F2'
const floors = [{ floorId: F1, z: 0, level: 1 }, { floorId: F2, z: 6000, level: 2 }]
const aislesByFloor = new Map([
  [F1, [{ aisleCode: 'H1', centerline: '[[0,500],[1000,500]]' }]],
  [F2, [{ aisleCode: 'H2', centerline: '[[0,500],[1000,500]]' }]],
])
const E1 = { connectorCode: 'E1', type: 1, waitSec: 20, travelSecPerFloor: 6, stops: [{ floorId: F1, x: 500, y: 500 }, { floorId: F2, x: 500, y: 500 }] }
const connectors = [E1]

describe('buildMultiFloorGraph (time weights)', () => {
  it('vertical connector edge = verticalSec(wait,perFloor,|Δlevel|)', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const up = g.adj.get('F1:500,500')!.find((e) => e.to === 'F2:500,500')
    expect(up).toBeTruthy()
    expect(up!.w).toBeCloseTo(verticalSec(20, 6, 1)) // 26s
  })
  it('horizontal aisle edge = mmToSec(distance)', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const e = g.adj.get('F1:0,500')!.find((x) => x.to === 'F1:1000,500')
    expect(e!.w).toBeCloseTo(mmToSec(1000))
  })
  it('hScale = Kmin = global min(time/physLen); horizontal dominates here', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    expect(g.hScale).toBeCloseTo(1 / WALK_SPEED_MMPS)
  })
  it('slanted connector: Kmin uses true 3D edge length, not |Δz| (A* admissibility)', () => {
    // 倾斜连接体：F1(500,500)→F2(1100,500)，Δxy=600、Δz=6000；时间=verticalSec(0,1,1)=1s（rate 极低 → 全图 Kmin）
    const slanted = { connectorCode: 'S1', type: 1, waitSec: 0, travelSecPerFloor: 1, stops: [{ floorId: F1, x: 500, y: 500 }, { floorId: F2, x: 1100, y: 500 }] }
    const g = buildMultiFloorGraph(floors, aislesByFloor, [slanted])
    const physLen = Math.hypot(1100 - 500, 6000 - 0) // 真 3D 边长 = hypot(600,6000)，非 |Δz|=6000
    // Kmin 必须用真边长；用 |Δz| 会高估最小 rate → 启发式不可采纳(inadmissible) → A* 可能次优
    expect(g.hScale).toBeCloseTo(verticalSec(0, 1, 1) / physLen, 6)
  })
})

describe('pathBetweenMF (time)', () => {
  it('crosses floors via connector; z spans 0→6000; time > vertical 26s', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const r = pathBetweenMF(g, { floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 })
    expect(r.degraded).toBe(false)
    expect(Math.min(...r.points.map((p) => p.z))).toBeCloseTo(0)
    expect(Math.max(...r.points.map((p) => p.z))).toBeCloseTo(6000)
    expect(r.time).toBeGreaterThan(verticalSec(20, 6, 1))
  })
  it('costMatrixMF symmetric + includes vertical time', () => {
    const g = buildMultiFloorGraph(floors, aislesByFloor, connectors)
    const stops = [{ floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 }]
    const m = costMatrixMF(g, stops)
    expect(m[0]![1]).toBeCloseTo(m[1]![0]!)
    expect(m[0]![1]).toBeGreaterThan(verticalSec(20, 6, 1))
  })
})

describe('planPickComparisonMF (dual distance+time)', () => {
  const stops = [
    { floorId: F1, x: 100, y: 520 }, { floorId: F2, x: 900, y: 520 },
    { floorId: F1, x: 900, y: 520 }, { floorId: F2, x: 100, y: 520 },
  ]
  it('returns both Mm and Sec; optimizedSec ≤ actualSec; timeSavings ≥ 0; order[0]=0', () => {
    const cmp = planPickComparisonMF(floors, aislesByFloor, connectors, stops)
    expect(cmp.order[0]).toBe(0)
    expect(cmp.optimizedSec).toBeLessThanOrEqual(cmp.actualSec + 1e-6)
    expect(cmp.timeSavingsPct).toBeGreaterThanOrEqual(0)
    expect(cmp.actualMm).toBeGreaterThan(0)
    expect(cmp.actualSec).toBeGreaterThan(0)
    expect(cmp.actual.points.some((p) => p.z > 0)).toBe(true)
  })
  it('pricier elevator raises actualSec (cost wired through)', () => {
    const cheap = planPickComparisonMF(floors, aislesByFloor, [E1], stops)
    const dear = planPickComparisonMF(floors, aislesByFloor, [{ ...E1, waitSec: 120, travelSecPerFloor: 60 }], stops)
    expect(dear.actualSec).toBeGreaterThan(cheap.actualSec)
  })
  it('single stop → zero distance/time, savings 0', () => {
    const cmp = planPickComparisonMF(floors, aislesByFloor, connectors, [{ floorId: F1, x: 100, y: 520 }])
    expect(cmp.actualMm).toBe(0)
    expect(cmp.actualSec).toBe(0)
    expect(cmp.timeSavingsPct).toBe(0)
  })
})
