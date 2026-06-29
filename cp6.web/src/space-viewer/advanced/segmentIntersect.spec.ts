import { describe, it, expect } from 'vitest'
import { pointOnSegment, segSegIntersection, splitPointsOnSegment } from './segmentIntersect'

const near = (p: { x: number; y: number } | null, x: number, y: number) => {
  expect(p).not.toBeNull()
  expect(p!.x).toBeCloseTo(x, 3)
  expect(p!.y).toBeCloseTo(y, 3)
}

describe('pointOnSegment', () => {
  const a = { x: 0, y: 0 }, b = { x: 100, y: 0 }
  it('endpoints and midpoint are on', () => {
    expect(pointOnSegment(a, a, b)).toBe(true)
    expect(pointOnSegment(b, a, b)).toBe(true)
    expect(pointOnSegment({ x: 50, y: 0 }, a, b)).toBe(true)
  })
  it('off-segment by perpendicular distance is not on', () => {
    expect(pointOnSegment({ x: 50, y: 10 }, a, b)).toBe(false)
  })
  it('beyond the extent (on the line) is not on', () => {
    expect(pointOnSegment({ x: 150, y: 0 }, a, b)).toBe(false)
  })
  it('degenerate point segment matches only within eps', () => {
    expect(pointOnSegment({ x: 0, y: 0 }, a, a)).toBe(true)
    expect(pointOnSegment({ x: 5, y: 0 }, a, a)).toBe(false)
  })
})

describe('segSegIntersection', () => {
  it('cross at midpoint', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 50, y: -50 }, { x: 50, y: 50 }), 50, 0)
  })
  it('T-junction: endpoint of seg2 on seg1', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 50, y: 0 }, { x: 50, y: 50 }), 50, 0)
  })
  it('intersection exactly at an endpoint', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }), 100, 0)
  })
  it('parallel separated -> null', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 0, y: 50 }, { x: 100, y: 50 })).toBeNull()
  })
  it('collinear non-touching -> null', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 200, y: 0 }, { x: 300, y: 0 })).toBeNull()
  })
  it('endpoint extended (does not reach) -> null', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 200, y: -50 }, { x: 200, y: 50 })).toBeNull()
  })
  it('zero-length segment -> null (no crash)', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 50, y: 0 }, { x: 50, y: 50 })).toBeNull()
  })
  it('collinear endpoint touch A-B / B-C -> B', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 0 }, { x: 200, y: 0 }), 100, 0)
  })
  it('near endpoint touch within eps (0.4mm) -> merge', () => {
    near(segSegIntersection({ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100.4, y: 0 }, { x: 200, y: 0 }), 100, 0)
  })
  it('long near-parallel segments that meet beyond extent -> null', () => {
    expect(segSegIntersection({ x: 0, y: 0 }, { x: 10000, y: 0 }, { x: 0, y: 10 }, { x: 10000, y: 5 })).toBeNull()
  })
})

describe('splitPointsOnSegment', () => {
  it('orders cuts along the segment and dedups, keeping a and b', () => {
    const a = { x: 0, y: 0 }, b = { x: 1000, y: 0 }
    const pts = splitPointsOnSegment(a, b, [{ x: 500, y: 0 }, { x: 200, y: 0 }, { x: 500, y: 0 }])
    expect(pts.map((p) => p.x)).toEqual([0, 200, 500, 1000])
  })
  it('ignores cuts not on the segment', () => {
    const a = { x: 0, y: 0 }, b = { x: 1000, y: 0 }
    const pts = splitPointsOnSegment(a, b, [{ x: 500, y: 80 }, { x: 1500, y: 0 }])
    expect(pts.map((p) => p.x)).toEqual([0, 1000])
  })
})
