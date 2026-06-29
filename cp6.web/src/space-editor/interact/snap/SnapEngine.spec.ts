// H-5 SnapEngine 单测（ch02 §6）
import { describe, it, expect } from 'vitest'
import { SnapEngine } from './SnapEngine'
import type { RackVO, AisleVO } from '@/types/space/scene'

const emptyCtx = { zoom: 0.1, racks: [] as RackVO[], aisles: [] as AisleVO[] }

describe('SnapEngine — 网格吸附', () => {
  it('点在阈值内吸附到最近网格交点', () => {
    // snapStep=1000mm，zoom=0.1px/mm → threshold=8/0.1=80mm
    // 点(1040,980)，最近格点(1000,1000)，距=√(40²+20²)≈44.7mm < 80mm → 吸附
    const eng = new SnapEngine({ snapStep: 1000 })
    const res = eng.snap({ x: 1040, y: 980 }, { zoom: 0.1, racks: [], aisles: [] })
    expect(res).toEqual({ x: 1000, y: 1000, snapped: true })
  })

  it('点超阈值不吸附，返回原坐标', () => {
    // 点 (1400, 500)，最近格点 (1000, 1000) 距=√(400²+500²)≈640mm > 80mm → 不吸附
    const eng = new SnapEngine({ snapStep: 1000 })
    const res = eng.snap({ x: 1400, y: 500 }, { zoom: 0.1, racks: [], aisles: [] })
    expect(res).toEqual({ x: 1400, y: 500, snapped: false })
  })

  it('原点附近吸附到 (0,0)', () => {
    const eng = new SnapEngine({ snapStep: 1000 })
    const res = eng.snap({ x: 30, y: -20 }, { zoom: 0.1, racks: [], aisles: [] })
    expect(res).toEqual({ x: 0, y: 0, snapped: true })
  })

  it('zoom 较大时阈值缩小', () => {
    // zoom=1px/mm → threshold=8mm；点(1010,1000)→距10mm > 8mm → 不吸附
    const eng = new SnapEngine({ snapStep: 1000 })
    const res = eng.snap({ x: 1010, y: 1000 }, { zoom: 1, racks: [], aisles: [] })
    expect(res).toEqual({ x: 1010, y: 1000, snapped: false })
  })
})

describe('SnapEngine — 货架边角吸附', () => {
  it('货架锚点在阈值内应优先吸附', () => {
    const rack: RackVO = {
      id: 'r1', zoneId: 'z', floorId: 'f', rackCode: 'R1',
      x: 2000, y: 3000, z: 0, rotationZ: 0,
      cols: 2, levels: 3, depthCount: 1,
      cellW: 500, cellH: 500, cellD: 500,
    }
    const eng = new SnapEngine({ snapStep: 5000 }) // 大 snap 步长避免网格干扰
    // 点在货架锚点(2000,3000)附近 30mm 处
    const res = eng.snap({ x: 2030, y: 2990 }, { zoom: 0.1, racks: [rack], aisles: [] })
    expect(res.snapped).toBe(true)
    // 应吸附到最近角（锚点 x=2000, y=3000）
    expect(res.x).toBe(2000)
    expect(res.y).toBe(3000)
  })
})

describe('SnapEngine — 自定义阈值', () => {
  it('thresholdPx 可覆盖默认 8px', () => {
    // thresholdPx=4 @ zoom=0.1 → 40mm；点离格点 44mm → 不吸附
    const eng = new SnapEngine({ snapStep: 1000, thresholdPx: 4 })
    const res = eng.snap({ x: 1040, y: 980 }, emptyCtx)
    expect(res.snapped).toBe(false)
  })
})

describe('SnapEngine — distributeEqual', () => {
  it('等距分布返回偏移数组（长度=对象数）', () => {
    const eng = new SnapEngine({ snapStep: 1000 })
    // 3 个位置各需偏移到均匀分布
    const positions = [{ x: 0, y: 0 }, { x: 500, y: 0 }, { x: 1200, y: 0 }]
    const offsets = eng.distributeEqual(positions, 'x')
    expect(offsets).toHaveLength(3)
    // 首末不动，中间点移到 600
    expect(offsets[0]).toBeCloseTo(0, 0)
    expect(offsets[1]).toBeCloseTo(100, 0)  // 500 → 600，偏移 +100
    expect(offsets[2]).toBeCloseTo(0, 0)
  })
})
