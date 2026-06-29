import { describe, it, expect } from 'vitest'
import {
  parseCenterline, buildCenterlineGraph, planPickRoute, astar,
  distanceMatrixFromGraph, planPickComparison,
} from './PickPathPlanner'

describe('PickPathPlanner', () => {
  it('parseCenterline parses valid and tolerates garbage', () => {
    expect(parseCenterline('[[0,0],[100,0]]')).toEqual([{ x: 0, y: 0 }, { x: 100, y: 0 }])
    expect(parseCenterline('')).toEqual([])
    expect(parseCenterline('not json')).toEqual([])
    expect(parseCenterline('[]')).toEqual([])
  })

  it('buildCenterlineGraph merges shared endpoints into one node', () => {
    // 两条中心线在 (1000,0) 共端点 → 该点应只有一个图节点
    const g = buildCenterlineGraph([
      { aisleCode: 'H', centerline: '[[0,0],[1000,0]]' },
      { aisleCode: 'V', centerline: '[[1000,0],[1000,1000]]' },
    ])
    expect(g.nodes.has('0,0')).toBe(true)
    expect(g.nodes.has('1000,0')).toBe(true)
    expect(g.nodes.has('1000,1000')).toBe(true)
    expect(g.nodes.size).toBe(3)              // 共端点合并，不是 4
    expect(g.adj.get('1000,0')!.length).toBe(2)  // 连 (0,0) 与 (1000,1000)
  })

  it('planPickRoute routes around the L-corner, not straight diagonal', () => {
    const aisles = [
      { aisleCode: 'H', centerline: '[[0,0],[1000,0]]' },
      { aisleCode: 'V', centerline: '[[1000,0],[1000,1000]]' },
    ]
    const stops = [{ x: 0, y: 100 }, { x: 900, y: 1100 }]
    const route = planPickRoute(aisles, stops)
    expect(route.degraded).toBe(false)
    // 路径经过拐角节点 (1000,0)
    expect(route.points.some((p) => Math.round(p.x) === 1000 && Math.round(p.y) === 0)).toBe(true)
    // 首点=起库位、末点=止库位
    expect(route.points[0]).toEqual({ x: 0, y: 100 })
    expect(route.points[route.points.length - 1]).toEqual({ x: 900, y: 1100 })
    expect(route.totalDistance).toBeGreaterThan(0)
  })

  it('planPickRoute degrades to straight connect when no aisles', () => {
    const route = planPickRoute([], [{ x: 0, y: 0 }, { x: 500, y: 500 }])
    expect(route.degraded).toBe(true)
    expect(route.points).toEqual([{ x: 0, y: 0 }, { x: 500, y: 500 }])
    expect(route.totalDistance).toBeCloseTo(Math.hypot(500, 500))
  })

  it('planPickRoute with <2 stops returns the stops unchanged', () => {
    expect(planPickRoute([], [{ x: 1, y: 2 }]).points).toEqual([{ x: 1, y: 2 }])
    expect(planPickRoute([], []).points).toEqual([])
  })

  it('buildCenterlineGraph splits at a mid-segment crossing (plus shape)', () => {
    // H 在 y=500、V 在 x=500，中段 (500,500) 相交，互不共端点
    const g = buildCenterlineGraph([
      { aisleCode: 'H', centerline: '[[0,500],[1000,500]]' },
      { aisleCode: 'V', centerline: '[[500,0],[500,1000]]' },
    ])
    expect(g.nodes.has('500,500')).toBe(true)       // 交叉口成为公共节点
    expect(g.nodes.size).toBe(5)                     // 4 端点 + 1 交叉口
    expect(g.adj.get('500,500')!.length).toBe(4)     // 四向连通
  })

  it('planPickRoute connects across a mid-segment crossing (v1 would degrade)', () => {
    const aisles = [
      { aisleCode: 'H', centerline: '[[0,500],[1000,500]]' },
      { aisleCode: 'V', centerline: '[[500,0],[500,1000]]' },
    ]
    // 起点贴 V 巷下段、终点贴 H 巷右段 → 必须经交叉口连通
    const route = planPickRoute(aisles, [{ x: 480, y: 100 }, { x: 900, y: 520 }])
    expect(route.degraded).toBe(false)
    expect(route.points.some((p) => Math.round(p.x) === 500 && Math.round(p.y) === 500)).toBe(true)
  })

  it('astar finds the optimal path and respects the admissible heuristic', () => {
    // S(0,0)—M(50,100)—E(100,0) 三角：直连 S-E=100 短于 S-M-E≈223.6
    const adj = new Map<string, Array<{ to: string; w: number }>>([
      ['0,0', [{ to: '50,100', w: Math.hypot(50, 100) }, { to: '100,0', w: 100 }]],
      ['50,100', [{ to: '0,0', w: Math.hypot(50, 100) }, { to: '100,0', w: Math.hypot(50, 100) }]],
      ['100,0', [{ to: '0,0', w: 100 }, { to: '50,100', w: Math.hypot(50, 100) }]],
    ])
    const coords: Record<string, { x: number; y: number }> = {
      '0,0': { x: 0, y: 0 }, '50,100': { x: 50, y: 100 }, '100,0': { x: 100, y: 0 },
    }
    const path = astar(adj, '0,0', '100,0', (k) => coords[k]!)
    expect(path).toEqual(['0,0', '100,0'])
  })

  it('astar returns null when disconnected', () => {
    const adj = new Map<string, Array<{ to: string; w: number }>>([
      ['0,0', [{ to: '10,0', w: 10 }]],
      ['10,0', [{ to: '0,0', w: 10 }]],
      ['99,99', []],
    ])
    const coords: Record<string, { x: number; y: number }> = {
      '0,0': { x: 0, y: 0 }, '10,0': { x: 10, y: 0 }, '99,99': { x: 99, y: 99 },
    }
    expect(astar(adj, '0,0', '99,99', (k) => coords[k]!)).toBeNull()
  })

  it('distanceMatrixFromGraph is symmetric; degraded pairs use euclidean', () => {
    const g = buildCenterlineGraph([{ aisleCode: 'H', centerline: '[[0,0],[1000,0]]' }])
    const stops = [{ x: 0, y: 50 }, { x: 200, y: 50 }]
    const m = distanceMatrixFromGraph(g, stops)
    expect(m[0]![0]).toBe(0)
    expect(m[0]![1]).toBeCloseTo(m[1]![0]!)        // 对称
    expect(m[0]![1]).toBeCloseTo(300)              // 50 下 + 200 巷 + 50 上

    const empty = buildCenterlineGraph([])         // 无段 → degraded 欧氏
    const md = distanceMatrixFromGraph(empty, stops)
    expect(md[0]![1]).toBeCloseTo(200)             // 直连欧氏
  })

  it('planPickComparison: optimized never longer than actual; savings >= 0', () => {
    const aisles = [{ aisleCode: 'H', centerline: '[[0,0],[1000,0]]' }]
    // LineNo 序 0->1000->200->800 来回绕路；优化序应 0->200->800->1000
    const stops = [{ x: 0, y: 50 }, { x: 1000, y: 50 }, { x: 200, y: 50 }, { x: 800, y: 50 }]
    const cmp = planPickComparison(aisles, stops)
    expect(cmp.order[0]).toBe(0)
    expect(cmp.order).toEqual([0, 2, 3, 1])
    expect(cmp.actualMm).toBeCloseTo(2700)
    expect(cmp.optimizedMm).toBeCloseTo(1300)
    expect(cmp.optimizedMm).toBeLessThanOrEqual(cmp.actualMm + 1e-6)
    expect(cmp.savingsPct).toBeGreaterThan(0)
    expect(cmp.actual.degraded).toBe(false)
    expect(cmp.optimized.degraded).toBe(false)
    expect(cmp.degradedPairCount).toBe(0)
  })

  it('planPickComparison: already-optimal order falls back, savings = 0', () => {
    const aisles = [{ aisleCode: 'H', centerline: '[[0,0],[1000,0]]' }]
    const stops = [{ x: 0, y: 50 }, { x: 200, y: 50 }, { x: 800, y: 50 }, { x: 1000, y: 50 }]
    const cmp = planPickComparison(aisles, stops)
    expect(cmp.order).toEqual([0, 1, 2, 3])        // 回退原序
    expect(cmp.savingsPct).toBe(0)
    expect(cmp.optimizedMm).toBeCloseTo(cmp.actualMm)
  })

  it('planPickComparison: single stop -> zero distances, savings 0', () => {
    const cmp = planPickComparison([{ aisleCode: 'H', centerline: '[[0,0],[1000,0]]' }], [{ x: 0, y: 50 }])
    expect(cmp.order).toEqual([0])
    expect(cmp.actualMm).toBe(0)
    expect(cmp.optimizedMm).toBe(0)
    expect(cmp.savingsPct).toBe(0)
  })

  it('astar heuristic is 3D-tolerant: routes A->B->C up the z axis', () => {
    const adj = new Map<string, Array<{ to: string; w: number }>>([
      ['A', [{ to: 'B', w: 10 }]],
      ['B', [{ to: 'A', w: 10 }, { to: 'C', w: 10 }]],
      ['C', [{ to: 'B', w: 10 }]],
    ])
    const coords: Record<string, { x: number; y: number; z?: number }> = {
      A: { x: 0, y: 0, z: 0 }, B: { x: 0, y: 0, z: 10 }, C: { x: 0, y: 0, z: 20 },
    }
    expect(astar(adj, 'A', 'C', (k) => coords[k]!)).toEqual(['A', 'B', 'C'])
  })
})
