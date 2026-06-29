import { describe, it, expect } from 'vitest'
import { arrayFootprint } from './arrayFootprint'

const TPL = { cols: 4, cellW: 1000, depthCount: 1, cellD: 800 }

describe('arrayFootprint', () => {
  it('1×1 = 单架尺寸', () => {
    const f = arrayFootprint(TPL, { rows: 1, racksPerRow: 1, rowGap: 2000, rackGap: 1000 })
    expect(f.w).toBe(4000)
    expect(f.d).toBe(800)
  })

  it('rows×racksPerRow 含间隙累加', () => {
    const f = arrayFootprint(TPL, { rows: 3, racksPerRow: 2, rowGap: 2000, rackGap: 1000 })
    expect(f.w).toBe(9000)
    expect(f.d).toBe(6400)
  })

  it('与 genZoneArray 末架终点一致', () => {
    const params = { rows: 2, racksPerRow: 3, rowGap: 1500, rackGap: 500 }
    const f = arrayFootprint(TPL, params)
    const rackWidth = TPL.cols * TPL.cellW, rackDepth = TPL.depthCount * TPL.cellD
    const lastX = (params.racksPerRow - 1) * (rackWidth + params.rackGap) + rackWidth
    const lastY = (params.rows - 1) * (rackDepth + params.rowGap) + rackDepth
    expect(f.w).toBe(lastX)
    expect(f.d).toBe(lastY)
  })
})
