import { describe, it, expect } from 'vitest'
import { parseCenterline, buildCenterlineGraph, planPickRoute, astar } from './PickPathPlanner'

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
})
