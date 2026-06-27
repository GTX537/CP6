import { describe, it, expect } from 'vitest'
import { LodController } from './LodController'

// Thresholds: near↔medium=80±8, medium↔far=150±8
// near→medium: distance > 88
// medium→near: distance < 72
// medium→far:  distance > 158
// far→medium:  distance < 142

describe('LodController', () => {
  it('starts in near tier', () => {
    expect(new LodController().tier).toBe('near')
  })

  it('near → medium when distance > 88', () => {
    const ctrl = new LodController()
    ctrl.update(88)
    expect(ctrl.tier).toBe('near')   // exactly at boundary — no switch
    ctrl.update(89)
    expect(ctrl.tier).toBe('medium')
  })

  it('medium → far when distance > 158', () => {
    const ctrl = new LodController()
    ctrl.update(89)   // → medium
    ctrl.update(158)
    expect(ctrl.tier).toBe('medium') // exactly at boundary
    ctrl.update(159)
    expect(ctrl.tier).toBe('far')
  })

  it('far → medium when distance < 142', () => {
    const ctrl = new LodController()
    ctrl.update(89); ctrl.update(159) // → far
    ctrl.update(142)
    expect(ctrl.tier).toBe('far')    // exactly at boundary
    ctrl.update(141)
    expect(ctrl.tier).toBe('medium')
  })

  it('medium → near when distance < 72', () => {
    const ctrl = new LodController()
    ctrl.update(89)   // → medium
    ctrl.update(72)
    expect(ctrl.tier).toBe('medium') // exactly at boundary
    ctrl.update(71)
    expect(ctrl.tier).toBe('near')
  })

  it('hysteresis: distance at 80 does not cause oscillation', () => {
    const ctrl = new LodController()
    // near at distance 80 — stays near (need > 88 to switch)
    ctrl.update(80)
    expect(ctrl.tier).toBe('near')
    // push past the upper gate → medium
    ctrl.update(89)
    expect(ctrl.tier).toBe('medium')
    // back to 80 — medium stays (need < 72 to switch back)
    ctrl.update(80)
    expect(ctrl.tier).toBe('medium')
  })
})
