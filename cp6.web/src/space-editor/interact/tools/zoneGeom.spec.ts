// zoneGeom 纯函数单测（Task 6：矩形→polygon 四点顺序 + 短边校验）
import { describe, it, expect } from 'vitest'
import { rectToPolygon, rectShortEdge } from './zoneGeom'
import type { WorldRect } from '../select/lassoHit'

describe('rectToPolygon', () => {
  it('输出四点顺序 [x0,y0],[x1,y0],[x1,y1],[x0,y1]（不重复首点）', () => {
    const rect: WorldRect = { minX: 100, minY: 200, maxX: 900, maxY: 700 }
    const poly = rectToPolygon(rect)
    expect(poly).toEqual([
      [100, 200],
      [900, 200],
      [900, 700],
      [100, 700],
    ])
    expect(poly).toHaveLength(4)
  })
})

describe('rectShortEdge', () => {
  it('返回宽高中的较短边', () => {
    expect(rectShortEdge({ minX: 0, minY: 0, maxX: 1000, maxY: 400 })).toBe(400)
    expect(rectShortEdge({ minX: 0, minY: 0, maxX: 300, maxY: 900 })).toBe(300)
  })

  it('正方形时宽高相等', () => {
    expect(rectShortEdge({ minX: 0, minY: 0, maxX: 500, maxY: 500 })).toBe(500)
  })
})
