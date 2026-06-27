import { describe, it, expect } from 'vitest'
import { easeInOutCubic, easeLinear } from './CameraController'

describe('easeInOutCubic', () => {
  it('maps t=0 to 0', () => { expect(easeInOutCubic(0)).toBeCloseTo(0) })
  it('maps t=1 to 1', () => { expect(easeInOutCubic(1)).toBeCloseTo(1) })
  it('maps t=0.5 to 0.5', () => { expect(easeInOutCubic(0.5)).toBeCloseTo(0.5) })

  it('is monotonically increasing on [0,1]', () => {
    let prev = -Infinity
    for (let i = 0; i <= 20; i++) {
      const val = easeInOutCubic(i / 20)
      expect(val).toBeGreaterThanOrEqual(prev)
      prev = val
    }
  })

  it('output stays in [0, 1] for any t in [0, 1]', () => {
    for (let i = 0; i <= 20; i++) {
      const v = easeInOutCubic(i / 20)
      expect(v).toBeGreaterThanOrEqual(0)
      expect(v).toBeLessThanOrEqual(1)
    }
  })

  it('ease-out half accelerates slower than linear near t=1', () => {
    // at t=0.9, cubic should be closer to 1 than linear (early arrival)
    const cubic = easeInOutCubic(0.9)
    expect(cubic).toBeGreaterThan(0.9)
  })
})

describe('easeLinear', () => {
  it('identity function', () => {
    expect(easeLinear(0)).toBe(0)
    expect(easeLinear(0.5)).toBe(0.5)
    expect(easeLinear(1)).toBe(1)
  })
})
