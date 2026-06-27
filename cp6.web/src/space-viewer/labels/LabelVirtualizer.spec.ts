import { describe, it, expect } from 'vitest'
import { gridDedup, LabelPool } from './LabelVirtualizer'

describe('gridDedup', () => {
  it('keeps first candidate per cell and deduplicates the rest', () => {
    // 400×300 screen, 4 cells → each cell 100×75
    const candidates = [
      { id: 'A', screenX: 10, screenY: 10 },   // cell (0,0)
      { id: 'B', screenX: 50, screenY: 30 },   // cell (0,0) — same as A
      { id: 'C', screenX: 200, screenY: 200 },  // cell (2,2) — different
    ]
    const result = gridDedup(candidates, 400, 300, 4, 1, 200)
    expect(result).toContain('A')
    expect(result).not.toContain('B')   // same cell as A, deduped
    expect(result).toContain('C')
    expect(result.length).toBe(2)
  })

  it('allows maxPerCell > 1 in same cell', () => {
    const candidates = [
      { id: 'A', screenX: 10, screenY: 10 },
      { id: 'B', screenX: 20, screenY: 20 },
      { id: 'C', screenX: 30, screenY: 30 },
    ]
    const result = gridDedup(candidates, 400, 300, 4, 2, 200)
    expect(result).toContain('A')
    expect(result).toContain('B')   // 2 allowed per cell
    expect(result).not.toContain('C')  // 3rd in same cell blocked
  })

  it('respects maxTotal regardless of screen positions', () => {
    const candidates = Array.from({ length: 100 }, (_, i) => ({
      id: `L${i}`,
      screenX: i * 30,
      screenY: 5,
    }))
    const result = gridDedup(candidates, 3000, 300, 30, 1, 5)
    expect(result.length).toBeLessThanOrEqual(5)
  })

  it('returns empty when candidates is empty', () => {
    expect(gridDedup([], 400, 300, 4, 1, 200)).toEqual([])
  })
})

describe('LabelPool', () => {
  it('acquire returns non-null up to maxSize then null', () => {
    const pool = new LabelPool(3)
    expect(pool.maxSize).toBe(3)
    const a = pool.acquire()
    const b = pool.acquire()
    const c = pool.acquire()
    const d = pool.acquire()   // pool exhausted
    expect(a).not.toBeNull()
    expect(b).not.toBeNull()
    expect(c).not.toBeNull()
    expect(d).toBeNull()
    expect(pool.activeCount).toBe(3)
  })

  it('released object becomes available again', () => {
    const pool = new LabelPool(2)
    const a = pool.acquire()!
    pool.acquire()
    expect(pool.acquire()).toBeNull()   // full
    pool.release(a)
    expect(pool.acquire()).not.toBeNull()   // a is back
  })

  it('releaseAll resets activeCount to 0', () => {
    const pool = new LabelPool(5)
    pool.acquire(); pool.acquire(); pool.acquire()
    expect(pool.activeCount).toBe(3)
    pool.releaseAll()
    expect(pool.activeCount).toBe(0)
  })

  it('objects array size equals maxSize', () => {
    const pool = new LabelPool(7)
    expect(pool.objects.length).toBe(7)
  })
})
