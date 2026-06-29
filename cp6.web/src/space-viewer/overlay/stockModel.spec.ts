// cp6.web/src/space-viewer/overlay/stockModel.spec.ts
import { describe, it, expect } from 'vitest'
import { binStatusToHex, NO_DATA_HEX, locationUtilization, utilizationToHex, aggregateUtilization } from './stockModel'

describe('stockModel', () => {
  it('binStatusToHex maps 5 states', () => {
    expect(binStatusToHex(0)).toBe(0x4caf50) // 空 绿
    expect(binStatusToHex(1)).toBe(0x2196f3) // 有货 蓝
    expect(binStatusToHex(2)).toBe(0xf44336) // 满 红
    expect(binStatusToHex(3)).toBe(0x9e9e9e) // 锁定 灰
    expect(binStatusToHex(4)).toBe(0xffc107) // 在拣 黄
    expect(binStatusToHex(99)).toBe(NO_DATA_HEX) // 未知/无数据 中性
  })

  it('locationUtilization: qty/capacity, fallback to status coarse', () => {
    expect(locationUtilization({ qty: 5, capacity: 10, binStatus: 1 } as any)).toBeCloseTo(0.5)
    // 无容量 → 按 BinStatus 粗估：空0 / 有货0.5 / 满1
    expect(locationUtilization({ qty: 3, capacity: null, binStatus: 0 } as any)).toBe(0)
    expect(locationUtilization({ qty: 3, capacity: null, binStatus: 1 } as any)).toBe(0.5)
    expect(locationUtilization({ qty: 3, capacity: null, binStatus: 2 } as any)).toBe(1)
  })

  it('utilizationToHex: cold→warm at 0/0.5/1', () => {
    expect(utilizationToHex(0)).toBe(0x2196f3)   // 蓝
    expect(utilizationToHex(1)).toBe(0xf44336)   // 红
    expect(typeof utilizationToHex(0.5)).toBe('number')
  })

  it('aggregateUtilization sums qty/capacity (capacity-bearing only)', () => {
    const agg = aggregateUtilization([
      { qty: 5, capacity: 10, binStatus: 1 },
      { qty: 10, capacity: 10, binStatus: 2 },
      { qty: 3, capacity: null, binStatus: 1 }, // 无容量不计入分母
    ] as any)
    expect(agg).toBeCloseTo(15 / 20)
  })
})
