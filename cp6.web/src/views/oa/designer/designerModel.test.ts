import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, NODE_PALETTE } from './designerModel'

const schema = {
  start: 's',
  nodes: [
    { id: 's', type: 'start', name: '填單', x: 0, y: 0 },
    { id: 'a', type: 'approval', name: '审批', approverStrategy: 'Specified', x: 0, y: 120 },
    { id: 'e', type: 'end', name: '结束', x: 0, y: 240 },
  ],
  edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e', condition: 'days>3' }],
}

describe('designerModel', () => {
  it('schemaToGraph maps nodes+edges with positions', () => {
    const g = schemaToGraph(schema as any)
    expect(g.nodes).toHaveLength(3)
    expect(g.nodes[0]!.position).toEqual({ x: 0, y: 0 })
    expect(g.nodes[1]!.type).toBe('approval')
    expect(g.edges).toHaveLength(2)
    expect(g.edges[1]!.source).toBe('a')
    expect(g.edges[1]!.target).toBe('e')
  })

  it('graphToSchema is the inverse (roundtrip preserves ids/positions/start)', () => {
    const g = schemaToGraph(schema as any)
    const back = graphToSchema(g.nodes, g.edges)
    expect(back.start).toBe('s')                       // start = type==='start' 节点
    expect(back.nodes.map(n => n.id).sort()).toEqual(['a', 'e', 's'])
    expect(back.nodes.find(n => n.id === 's')!.x).toBe(0)
    expect(back.edges.find(e => e.from === 'a')!.condition).toBe('days>3')
  })

  it('validateClient flags missing start + edge to unknown node', () => {
    expect(validateClient(schema as any)).toEqual([])
    const noStart = { ...schema, nodes: schema.nodes.filter(n => n.type !== 'start') }
    expect(validateClient(noStart as any).length).toBeGreaterThan(0)
    const ghost = { ...schema, edges: [...schema.edges, { from: 'a', to: 'zzz' }] }
    expect(validateClient(ghost as any).length).toBeGreaterThan(0)
  })

  it('NODE_PALETTE lists the 5 engine node types', () => {
    expect(NODE_PALETTE.map(p => p.type).sort())
      .toEqual(['approval', 'end', 'parallelJoin', 'parallelSplit', 'start'])
  })
})
