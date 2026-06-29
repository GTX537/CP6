import { describe, it, expect } from 'vitest'
import { obbIntersectsRect } from './lassoHit'
import { rackCorners } from '../collide/CollisionHint'
import type { RackVO } from '@/types/space/scene'

function rack(partial: Partial<RackVO>): RackVO {
  return {
    id: 'r', zoneId: 'z', floorId: 'f', rackCode: 'R',
    x: 0, y: 0, z: 0, rotationZ: 0,
    cols: 1, levels: 1, depthCount: 1, cellW: 1000, cellH: 1000, cellD: 1000,
    ...partial,
  }
}

describe('obbIntersectsRect', () => {
  it('轴对齐货架与重叠矩形相交', () => {
    const r = rack({ x: 0, y: 0 })
    expect(obbIntersectsRect(rackCorners(r), { minX: 500, minY: 500, maxX: 1500, maxY: 1500 })).toBe(true)
  })

  it('轴对齐货架与远处矩形不相交', () => {
    const r = rack({ x: 0, y: 0 })
    expect(obbIntersectsRect(rackCorners(r), { minX: 2000, minY: 2000, maxX: 3000, maxY: 3000 })).toBe(false)
  })

  it('矩形完全包含货架则相交', () => {
    const r = rack({ x: 100, y: 100 })
    expect(obbIntersectsRect(rackCorners(r), { minX: -100, minY: -100, maxX: 2000, maxY: 2000 })).toBe(true)
  })

  it('45°旋转货架：擦过其AABB角但不碰OBB → 不相交（AABB会误判）', () => {
    const r = rack({ x: 0, y: 0, rotationZ: 45 })
    const corners = rackCorners(r)
    const rect = { minX: -707, minY: 0, maxX: -600, maxY: 100 }
    expect(obbIntersectsRect(corners, rect)).toBe(false)
  })

  it('45°旋转货架：矩形真实覆盖其中心 → 相交', () => {
    const r = rack({ x: 0, y: 0, rotationZ: 45 })
    const corners = rackCorners(r)
    expect(obbIntersectsRect(corners, { minX: -50, minY: 650, maxX: 50, maxY: 760 })).toBe(true)
  })

  it('边缘紧贴视为分离（与碰撞口径一致）', () => {
    const r = rack({ x: 0, y: 0 })
    expect(obbIntersectsRect(rackCorners(r), { minX: 1000, minY: 0, maxX: 2000, maxY: 1000 })).toBe(false)
  })
})
