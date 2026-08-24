import { describe, expect, it } from 'vitest'
import { buildElementRedrawOverlayPlan } from './ElementRedrawOverlayLayer'

describe('buildElementRedrawOverlayPlan', () => {
  it('maps authoritative world points and the cursor preview into screen space', () => {
    expect(buildElementRedrawOverlayPlan(
      [{ x: 1_000, y: 2_000 }, { x: 3_000, y: 4_000 }],
      { x: 5_000, y: 6_000 },
      800,
      { panX: 500, panY: 1_000, zoom: 0.1 },
    )).toEqual({
      vertices: [{ x: 50, y: 700 }, { x: 250, y: 500 }],
      preview: { x: 450, y: 300 },
    })
  })

  it('returns an empty plan before the canvas has a usable height', () => {
    expect(buildElementRedrawOverlayPlan(
      [{ x: 1, y: 2 }],
      null,
      0,
      { panX: 0, panY: 0, zoom: 0.05 },
    )).toEqual({ vertices: [] })
  })
})
