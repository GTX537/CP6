import { describe, it, expect } from 'vitest'
import { rotateAboutCenter, snapAngle } from './rotateGeometry'

const SQUARE = { cols: 1, cellW: 1000, depthCount: 1, cellD: 1000 }

describe('rotateAboutCenter', () => {
  it('0°→90° 保持几何中心不变（锚点随之位移）', () => {
    const rack = { x: 0, y: 0, rotationZ: 0, ...SQUARE }
    const a = rotateAboutCenter(rack, 90)
    expect(a.x).toBeCloseTo(1000, 6)
    expect(a.y).toBeCloseTo(0, 6)
  })

  it('几何中心在旋转前后一致（不变量）', () => {
    const rack = { x: 137, y: -42, rotationZ: 23, cols: 4, cellW: 1000, depthCount: 2, cellD: 800 }
    const W = rack.cols * rack.cellW, D = rack.depthCount * rack.cellD
    const center = (x: number, y: number, deg: number) => {
      const th = (deg * Math.PI) / 180, cos = Math.cos(th), sin = Math.sin(th)
      return { x: x + (W / 2) * cos - (D / 2) * sin, y: y + (W / 2) * sin + (D / 2) * cos }
    }
    const c0 = center(rack.x, rack.y, rack.rotationZ)
    const a = rotateAboutCenter(rack, 137)
    const c1 = center(a.x, a.y, 137)
    expect(c1.x).toBeCloseTo(c0.x, 6)
    expect(c1.y).toBeCloseTo(c0.y, 6)
  })

  it('角度不变则锚点不变', () => {
    const rack = { x: 50, y: 60, rotationZ: 30, ...SQUARE }
    const a = rotateAboutCenter(rack, 30)
    expect(a.x).toBeCloseTo(50, 6)
    expect(a.y).toBeCloseTo(60, 6)
  })
})

describe('snapAngle', () => {
  it('阈内吸附到 15° 倍数', () => {
    expect(snapAngle(14)).toBe(15)
    expect(snapAngle(31)).toBe(30)
    expect(snapAngle(2)).toBe(0)
  })
  it('阈外保持原角', () => {
    expect(snapAngle(20)).toBe(20)
    expect(snapAngle(37)).toBe(37)
  })
  it('358° 环绕吸附到 0', () => {
    expect(snapAngle(358)).toBe(0)
  })
  it('负角规范化', () => {
    expect(snapAngle(-1)).toBe(0)
    expect(snapAngle(-46)).toBe(315)
  })
})
