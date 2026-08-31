// H-6 碰撞与越界单测（ch02 §8）
import { describe, it, expect } from 'vitest'
import { obbIntersect, pointInPolygon, rackInZone } from './CollisionHint'
import type { RackVO, ZoneVO } from '@/types/space/scene'

// ─── 辅助：构造 RackVO ─────────────────────────────────────────────────────────
function mkRack(overrides: Partial<RackVO> & { id: string }): RackVO {
  return {
    zoneId: 'z', floorId: 'f', rackCode: 'R',
    x: 0, y: 0, z: 0, rotationZ: 0,
    cols: 2, levels: 3, depthCount: 1,
    cellW: 500, cellH: 500, cellD: 500,
    ...overrides,
  }
}

// ─── OBB 重叠 ─────────────────────────────────────────────────────────────────
describe('obbIntersect', () => {
  it('完全重叠→ true', () => {
    const a = mkRack({ id: 'a', x: 0, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    const b = mkRack({ id: 'b', x: 0, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(obbIntersect(a, b)).toBe(true)
  })

  it('无旋转时 x 轴分离→ false', () => {
    // a: x=0..1000 y=0..1000; b: x=2000..3000 y=0..1000 → 明显分离
    const a = mkRack({ id: 'a', x: 0, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    const b = mkRack({ id: 'b', x: 2000, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(obbIntersect(a, b)).toBe(false)
  })

  it('无旋转时 y 轴分离→ false', () => {
    const a = mkRack({ id: 'a', x: 0, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    const b = mkRack({ id: 'b', x: 0, y: 2000, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(obbIntersect(a, b)).toBe(false)
  })

  it('无旋转时紧贴（共边）→ false（SAT 以 < 严格）', () => {
    // a: x=[0,1000] y=[0,1000]; b 从 x=1000 开始，恰好紧贴
    const a = mkRack({ id: 'a', x: 0, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    const b = mkRack({ id: 'b', x: 1000, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(obbIntersect(a, b)).toBe(false)
  })

  it('部分重叠（无旋转）→ true', () => {
    // a: x=[0,1000] y=[0,1000]; b: x=[500,1500] → 500mm 重叠
    const a = mkRack({ id: 'a', x: 0, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    const b = mkRack({ id: 'b', x: 500, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(obbIntersect(a, b)).toBe(true)
  })

  it('旋转 45° 的货架与右侧分离货架→ false', () => {
    // a 旋转 45°，包围盒最大宽度 ≈ W*√2；b 在 x=3000 → 足够远
    const a = mkRack({ id: 'a', x: 0, y: 0, rotationZ: 45, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    const b = mkRack({ id: 'b', x: 3000, y: 0, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(obbIntersect(a, b)).toBe(false)
  })

  it('旋转 45° 重叠→ true', () => {
    // 两个完全相同位置但旋转不同的货架必然重叠
    const a = mkRack({ id: 'a', x: 500, y: 500, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    const b = mkRack({ id: 'b', x: 500, y: 500, rotationZ: 45, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(obbIntersect(a, b)).toBe(true)
  })
})

// ─── 点在多边形内 ──────────────────────────────────────────────────────────────
describe('pointInPolygon', () => {
  // 单位正方形 [0,0]→[1,0]→[1,1]→[0,1]
  const square: [number, number][] = [[0, 0], [1, 0], [1, 1], [0, 1]]

  it('内部点→ true', () => {
    expect(pointInPolygon(0.5, 0.5, square)).toBe(true)
  })

  it('外部点→ false', () => {
    expect(pointInPolygon(2, 0.5, square)).toBe(false)
  })

  it('负侧点→ false', () => {
    expect(pointInPolygon(-0.1, 0.5, square)).toBe(false)
  })
})

// ─── rackInZone ───────────────────────────────────────────────────────────────
describe('rackInZone', () => {
  // Zone 多边形：(0,0)→(10000,0)→(10000,10000)→(0,10000)
  const zone: ZoneVO = {
    id: 'z', floorId: 'f', zoneCode: 'Z1', zoneName: 'Z1', zoneType: 1,
    polygon: JSON.stringify([[0, 0], [10000, 0], [10000, 10000], [0, 10000]]),
  }

  it('货架完全在 Zone 内→ true', () => {
    const rack = mkRack({ id: 'r', x: 1000, y: 1000, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(rackInZone(rack, zone)).toBe(true)
  })

  it('货架部分越出 Zone→ false', () => {
    // 货架从 x=9800 开始，宽 1000mm，右侧越出
    const rack = mkRack({ id: 'r', x: 9800, y: 1000, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(rackInZone(rack, zone)).toBe(false)
  })

  it('货架完全在 Zone 外→ false', () => {
    const rack = mkRack({ id: 'r', x: 11000, y: 1000, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })
    expect(rackInZone(rack, zone)).toBe(false)
  })

  it('版本化 Zone 几何仍可用于越界判定', () => {
    const versionedZone: ZoneVO = {
      ...zone,
      polygon: JSON.stringify({
        schemaVersion: 1,
        points: [[0, 0], [10000, 0], [10000, 10000], [0, 10000]],
      }),
    }
    const rack = mkRack({ id: 'r', x: 1000, y: 1000, rotationZ: 0, cols: 2, depthCount: 2, cellW: 500, cellD: 500 })

    expect(rackInZone(rack, versionedZone)).toBe(true)
  })
})
