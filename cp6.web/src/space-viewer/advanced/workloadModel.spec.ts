import { describe, it, expect } from 'vitest'
import { normalizeOpCounts, workloadToHex } from './workloadModel'
import { utilizationToHex } from '@/space-viewer/overlay/stockModel'

describe('workloadModel', () => {
  it('normalizeOpCounts maps to [0,1] by max', () => {
    const m = normalizeOpCounts([
      { locationCode: 'A', opCount: 10 },
      { locationCode: 'B', opCount: 5 },
      { locationCode: 'C', opCount: 0 },
    ])
    expect(m.get('A')).toBe(1)
    expect(m.get('B')).toBe(0.5)
    expect(m.get('C')).toBe(0)
  })

  it('normalizeOpCounts all-zero → all 0 (no divide-by-zero)', () => {
    const m = normalizeOpCounts([{ locationCode: 'A', opCount: 0 }])
    expect(m.get('A')).toBe(0)
  })

  it('workloadToHex reuses 07 cold→warm ramp', () => {
    expect(workloadToHex(0)).toBe(utilizationToHex(0))
    expect(workloadToHex(1)).toBe(utilizationToHex(1))
  })
})
