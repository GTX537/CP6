import { describe, it, expect } from 'vitest'
import { parseCenterline, buildCenterlineGraph, planPickRoute } from './PickPathPlanner'

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
})
