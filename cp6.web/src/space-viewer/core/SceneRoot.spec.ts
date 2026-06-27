import { describe, it, expect } from 'vitest'
import { SceneRoot } from './SceneRoot'

describe('SceneRoot coord adapter', () => {
  it('dataToWorld maps Z-up/mm to Y-up/meters', () => {
    const root = new SceneRoot()
    const w = root.dataToWorld({ x: 1000, y: 2000, z: 3000 })
    // scale 0.001 + rotation.x -90°: data(x,y,z) → world(x*0.001, z*0.001, -y*0.001)
    expect(w.x).toBeCloseTo(1.0)
    expect(w.y).toBeCloseTo(3.0)
    expect(w.z).toBeCloseTo(-2.0)
  })

  it('worldToData is inverse', () => {
    const root = new SceneRoot()
    const d0 = { x: 3456, y: 7890, z: 1234 }
    const d1 = root.worldToData(root.dataToWorld(d0))
    expect(d1.x).toBeCloseTo(d0.x, 0)
    expect(d1.y).toBeCloseTo(d0.y, 0)
    expect(d1.z).toBeCloseTo(d0.z, 0)
  })

  it('origin maps to world origin', () => {
    const root = new SceneRoot()
    const w = root.dataToWorld({ x: 0, y: 0, z: 0 })
    expect(w.x).toBeCloseTo(0)
    expect(w.y).toBeCloseTo(0)
    expect(w.z).toBeCloseTo(0)
  })
})
