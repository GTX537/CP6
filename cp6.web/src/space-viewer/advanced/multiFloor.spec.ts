import { describe, it, expect } from 'vitest'
import { mfKey, dist3 } from './multiFloor'

describe('multiFloor', () => {
  it('mfKey namespaces by floorId + 1mm rounded xy', () => {
    expect(mfKey('F1', { x: 100.4, y: 200.6 })).toBe('F1:100,201')
  })
  it('dist3 is 3D euclidean; z=0 reduces to 2D', () => {
    expect(dist3({ x: 0, y: 0, z: 0 }, { x: 3, y: 4, z: 0 })).toBeCloseTo(5)
    expect(dist3({ x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: 7 })).toBeCloseTo(7)
    expect(dist3({ x: 0, y: 0, z: 0 }, { x: 2, y: 3, z: 6 })).toBeCloseTo(7)
  })
})
