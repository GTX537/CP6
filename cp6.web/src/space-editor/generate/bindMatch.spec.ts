import { describe, it, expect } from 'vitest'
import { enumerateSlots, autoPair, computeMismatch, computeOrphans } from './bindMatch'
import type { UnplacedLocationDto } from './bindMatch'

// ── helpers ───────────────────────────────────────────────────────────────────

function makeCode(id: string, code: string): UnplacedLocationDto {
  return { id, locationCode: code, status: 1 }
}

// ── enumerateSlots ────────────────────────────────────────────────────────────

describe('enumerateSlots', () => {
  it('returns cols × levels × depthCount entries', () => {
    expect(enumerateSlots(3, 4, 2)).toHaveLength(24)
  })

  it('iterates col→level→depth order', () => {
    const slots = enumerateSlots(2, 2, 1)
    expect(slots[0]).toEqual({ col: 1, level: 1, depth: 1 })
    expect(slots[1]).toEqual({ col: 1, level: 2, depth: 1 })
    expect(slots[2]).toEqual({ col: 2, level: 1, depth: 1 })
    expect(slots[3]).toEqual({ col: 2, level: 2, depth: 1 })
  })

  it('includes depth dimension', () => {
    const slots = enumerateSlots(1, 1, 3)
    expect(slots).toHaveLength(3)
    expect(slots.map(s => s.depth)).toEqual([1, 2, 3])
  })

  it('single slot for 1×1×1', () => {
    const slots = enumerateSlots(1, 1, 1)
    expect(slots).toHaveLength(1)
    expect(slots[0]).toEqual({ col: 1, level: 1, depth: 1 })
  })
})

// ── autoPair ──────────────────────────────────────────────────────────────────

describe('autoPair', () => {
  const slots = enumerateSlots(2, 2, 1) // 4 slots

  it('pairs each slot with the code at the same index', () => {
    const codes = [makeCode('a', 'L-001'), makeCode('b', 'L-002'), makeCode('c', 'L-003'), makeCode('d', 'L-004')]
    const pairs = autoPair(slots, codes)
    expect(pairs[0]).toMatchObject({ col: 1, level: 1, depth: 1, locationId: 'a', locationCode: 'L-001' })
    expect(pairs[1]).toMatchObject({ col: 1, level: 2, depth: 1, locationId: 'b', locationCode: 'L-002' })
    expect(pairs[2]).toMatchObject({ col: 2, level: 1, depth: 1, locationId: 'c', locationCode: 'L-003' })
    expect(pairs[3]).toMatchObject({ col: 2, level: 2, depth: 1, locationId: 'd', locationCode: 'L-004' })
  })

  it('slotNoCode: excess slots get locationId=null', () => {
    const codes = [makeCode('a', 'L-001'), makeCode('b', 'L-002')]
    const pairs = autoPair(slots, codes)
    expect(pairs[0]!.locationId).toBe('a')
    expect(pairs[1]!.locationId).toBe('b')
    expect(pairs[2]!.locationId).toBeNull()
    expect(pairs[3]!.locationId).toBeNull()
  })

  it('codeNoSlot: extra codes not reflected in pairs (pairs length = slots length)', () => {
    const codes = [makeCode('a', 'L-001'), makeCode('b', 'L-002'), makeCode('c', 'L-003'), makeCode('d', 'L-004'), makeCode('e', 'L-005')]
    const pairs = autoPair(slots, codes)
    expect(pairs).toHaveLength(4) // slots.length, not codes.length
    expect(pairs.every(p => p.locationId !== null)).toBe(true)
  })

  it('empty codes → all slots unmatched', () => {
    const pairs = autoPair(slots, [])
    expect(pairs).toHaveLength(4)
    expect(pairs.every(p => p.locationId === null)).toBe(true)
  })

  it('empty slots → empty pairs', () => {
    const pairs = autoPair([], [makeCode('a', 'L-001')])
    expect(pairs).toHaveLength(0)
  })
})

// ── computeMismatch ───────────────────────────────────────────────────────────

describe('computeMismatch', () => {
  it('exact when counts equal', () => {
    const m = computeMismatch(4, 4)
    expect(m.type).toBe('exact')
    expect(m.diff).toBe(0)
    expect(m.slotCount).toBe(4)
    expect(m.codeCount).toBe(4)
  })

  it('slotNoCode when slots > codes', () => {
    const m = computeMismatch(6, 4)
    expect(m.type).toBe('slotNoCode')
    expect(m.diff).toBe(2)
  })

  it('codeNoSlot when codes > slots', () => {
    const m = computeMismatch(3, 7)
    expect(m.type).toBe('codeNoSlot')
    expect(m.diff).toBe(4)
  })

  it('diff is always non-negative', () => {
    expect(computeMismatch(10, 0).diff).toBe(10)
    expect(computeMismatch(0, 10).diff).toBe(10)
  })
})

// ── computeOrphans ────────────────────────────────────────────────────────────

describe('computeOrphans', () => {
  it('returns codes not assigned to any pair', () => {
    const codes = [makeCode('a', 'L-001'), makeCode('b', 'L-002'), makeCode('c', 'L-003')]
    const slots = enumerateSlots(1, 1, 1) // only 1 slot
    const pairs = autoPair(slots, codes) // pairs[0].locationId = 'a'
    const orphans = computeOrphans(codes, pairs)
    expect(orphans).toHaveLength(2)
    expect(orphans.map(o => o.id)).toEqual(['b', 'c'])
  })

  it('no orphans when all codes assigned', () => {
    const codes = [makeCode('a', 'L-001'), makeCode('b', 'L-002')]
    const slots = enumerateSlots(1, 2, 1)
    const pairs = autoPair(slots, codes)
    expect(computeOrphans(codes, pairs)).toHaveLength(0)
  })

  it('all codes are orphans when no pairs have assignments', () => {
    const codes = [makeCode('a', 'L-001'), makeCode('b', 'L-002')]
    // Manually cleared pairs (simulating user clearing all selections)
    const pairs = [
      { col: 1, level: 1, depth: 1, locationId: null, locationCode: null },
      { col: 1, level: 2, depth: 1, locationId: null, locationCode: null },
    ]
    expect(computeOrphans(codes, pairs)).toHaveLength(2)
  })
})
