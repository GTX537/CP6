import { describe, it, expect } from 'vitest'
import { genRack } from './genRack'
import { computeAbs } from '../coords'

describe('genRack', () => {
  it('produces 1 rack + 4 locs for cols=2 levels=2 depthCount=1', () => {
    const tpl = { id: 't1', cols: 2, levels: 2, depthCount: 1, cellW: 1000, cellH: 1000, cellD: 1000 }
    const { rack, locs } = genRack(tpl, 'zone1', 'floor1', 0, 0, 0, 'R01')
    expect(locs).toHaveLength(4)
    expect(locs.every(l => l.locationCode === null && l.status === 0 && l.codeOrigin === 1 && l.placed)).toBe(true)
    expect(locs[0].absX).toBe(500) // computeAbs(col1, level1, depth1) at origin (0,0) rotation 0
    // all locs share rack id
    expect(locs.every(l => l.rackId === rack.id)).toBe(true)
  })

  it('locs[0].absX matches computeAbs(rack, 1, 1, 1)', () => {
    const tpl = { id: 't2', cols: 2, levels: 2, depthCount: 1, cellW: 1200, cellH: 1500, cellD: 800 }
    const { rack, locs } = genRack(tpl, 'zone1', 'floor1', 500, 1000, 0, 'R02')
    const expected = computeAbs(rack, 1, 1, 1)
    expect(locs[0].absX).toBe(expected.x)
    expect(locs[0].absY).toBe(expected.y)
    expect(locs[0].absZ).toBe(expected.z)
  })

  it('all locs have placed=true, version=0, codeOrigin=1', () => {
    const tpl = { id: 't3', cols: 3, levels: 4, depthCount: 2, cellW: 800, cellH: 1200, cellD: 600 }
    const { locs } = genRack(tpl, 'zone2', 'floor2', 0, 0, 0, 'R03')
    expect(locs).toHaveLength(3 * 4 * 2)
    expect(locs.every(l => l.placed && l.version === 0 && l.codeOrigin === 1)).toBe(true)
  })
})
