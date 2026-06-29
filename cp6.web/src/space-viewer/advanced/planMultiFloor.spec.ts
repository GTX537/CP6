import { describe, it, expect } from 'vitest'
import { buildMultiFloorGraph } from './planMultiFloor'

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
