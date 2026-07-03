import { describe, it, expect } from 'vitest'
import { mmToSec, verticalSec, WALK_SPEED_MMPS, TYPE_DEFAULT_COST } from './cost'

describe('cost', () => {
  it('mmToSec converts mm to seconds at walk speed', () => {
    expect(mmToSec(1200)).toBeCloseTo(1)
    expect(mmToSec(6000)).toBeCloseTo(5)
    expect(WALK_SPEED_MMPS).toBe(1200)
  })
  it('verticalSec = wait + perFloor * |span|', () => {
    expect(verticalSec(20, 6, 1)).toBe(26)
    expect(verticalSec(20, 6, 3)).toBe(38)
    expect(verticalSec(0, 15, 2)).toBe(30)
    expect(verticalSec(20, 6, -2)).toBe(32)
  })
  it('type defaults present for elevator/stairs/ramp', () => {
    expect(TYPE_DEFAULT_COST[1]).toEqual({ waitSec: 20, travelSecPerFloor: 6 })
    expect(TYPE_DEFAULT_COST[2]).toEqual({ waitSec: 0, travelSecPerFloor: 15 })
    expect(TYPE_DEFAULT_COST[3]).toEqual({ waitSec: 0, travelSecPerFloor: 10 })
  })
})
