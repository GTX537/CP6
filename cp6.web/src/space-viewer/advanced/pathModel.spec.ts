import { describe, it, expect } from 'vitest'
import { polylineLength, pointAtDistance } from './pathModel'

const L = [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }]

describe('pathModel', () => {
  it('polylineLength sums segments', () => {
    expect(polylineLength(L)).toBe(200)
    expect(polylineLength([{ x: 0, y: 0 }])).toBe(0)
    expect(polylineLength([])).toBe(0)
  })

  it('pointAtDistance walks arc-length and clamps to ends', () => {
    expect(pointAtDistance(L, 0)).toEqual({ x: 0, y: 0 })
    expect(pointAtDistance(L, 50)).toEqual({ x: 50, y: 0 })   // 沿第一段
    expect(pointAtDistance(L, 150)).toEqual({ x: 100, y: 50 }) // 沿第二段
    expect(pointAtDistance(L, 999)).toEqual({ x: 100, y: 100 }) // 超尾 → 末点
    expect(pointAtDistance(L, -5)).toEqual({ x: 0, y: 0 })      // 负 → 首点
  })
})
