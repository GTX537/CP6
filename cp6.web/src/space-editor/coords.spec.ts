import { describe, it, expect } from 'vitest'
import { worldToScreen, screenToWorld, computeAbs } from './coords'

describe('coords', () => {
  it('worldToScreen flips Y (world +Y is up, screen Y down)', () => {
    const view = { panX: 0, panY: 0, zoom: 0.1, height: 1000 }
    const p = worldToScreen({ x: 1000, y: 2000 }, view)
    expect(p.x).toBeCloseTo(100)
    expect(p.y).toBeCloseTo(1000 - 200)  // Y 翻转
  })

  it('screenToWorld is inverse of worldToScreen', () => {
    const view = { panX: 500, panY: 300, zoom: 0.2, height: 800 }
    const w = { x: 3456, y: 7890 }
    const back = screenToWorld(worldToScreen(w, view), view)
    expect(back.x).toBeCloseTo(w.x, 1)
    expect(back.y).toBeCloseTo(w.y, 1)
  })

  it('computeAbs matches backend formula (anchor + rotate around corner)', () => {
    const rack = { x: 1000, y: 2000, z: 0, rotationZ: 0, cellW: 1200, cellH: 1500, cellD: 1000 }
    expect(computeAbs(rack, 2, 2, 1)).toEqual({ x: 1000 + 1800, y: 2000 + 500, z: 2250 })
  })
})
