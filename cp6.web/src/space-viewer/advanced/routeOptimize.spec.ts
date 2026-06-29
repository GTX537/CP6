import { describe, it, expect } from 'vitest'
import { routeLengthByOrder, optimizeOrder } from './routeOptimize'

// 单位正方形四角的距离矩阵：0=(0,0) 1=(0,10) 2=(10,0) 3=(10,10)
const S = Math.SQRT2 * 10 // ≈14.142 对角
const SQUARE = [
  [0, 10, 10, S],
  [10, 0, S, 10],
  [10, S, 0, 10],
  [S, 10, 10, 0],
]

describe('routeLengthByOrder', () => {
  it('open-path sum of adjacent matrix entries', () => {
    expect(routeLengthByOrder([[0, 1, 2], [1, 0, 1], [2, 1, 0]], [0, 1, 2])).toBeCloseTo(2)
  })
  it('empty / single -> 0', () => {
    expect(routeLengthByOrder([], [])).toBe(0)
    expect(routeLengthByOrder([[0]], [0])).toBe(0)
  })
})

describe('optimizeOrder', () => {
  it('empty -> []', () => { expect(optimizeOrder([])).toEqual([]) })
  it('single -> [0]', () => { expect(optimizeOrder([[0]])).toEqual([0]) })
  it('two -> [0,1]', () => { expect(optimizeOrder([[0, 5], [5, 0]])).toEqual([0, 1]) })
  it('fixes start at 0 and is a permutation', () => {
    const order = optimizeOrder(SQUARE)
    expect(order[0]).toBe(0)
    expect([...order].sort()).toEqual([0, 1, 2, 3])
  })
  it('result is no worse than the natural [0,1,2,3] order', () => {
    const order = optimizeOrder(SQUARE)
    const natural = routeLengthByOrder(SQUARE, [0, 1, 2, 3]) // 10+14.142+10 ≈ 34.142
    expect(routeLengthByOrder(SQUARE, order)).toBeLessThanOrEqual(natural + 1e-9)
    expect(routeLengthByOrder(SQUARE, order)).toBeCloseTo(30) // 走三条边长10
  })
})
