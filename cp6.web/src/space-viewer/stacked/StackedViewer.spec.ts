import { describe, it, expect } from 'vitest'
import { accumulateFloorZ } from './StackedViewer'

describe('accumulateFloorZ', () => {
  it('sorts by level asc, z from 0 cumulative by height', () => {
    const z = accumulateFloorZ([
      { id: 'B', level: 2, height: 5000 }, { id: 'A', level: 1, height: 6000 }, { id: 'C', level: 3, height: 4000 },
    ])
    expect(z.get('A')).toBe(0)
    expect(z.get('B')).toBe(6000)
    expect(z.get('C')).toBe(11000)
  })
})
