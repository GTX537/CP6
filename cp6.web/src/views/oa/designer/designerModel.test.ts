import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, NODE_PALETTE } from './designerModel'
import type { FlowSchemaDto } from './designerModel'

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

  it('NODE_PALETTE lists the engine node types (incl. serviceTask)', () => {
    expect([...new Set(NODE_PALETTE.map(p => p.type))].sort())
      .toEqual(['approval', 'end', 'parallelJoin', 'parallelSplit', 'serviceTask', 'start'])
  })

  it('round-trips serial stages', () => {
    const schema: FlowSchemaDto = { start: 'ap', nodes: [
      { id: 'ap', type: 'approval', stages: [
        { kind: 'fixed', approverStrategy: 'Specified', countersign: 'all', name: '档1' },
        { kind: 'managerChain', maxLevels: 2, countersign: 'all', name: '逐级' },
      ] },
      { id: 'end', type: 'end' },
    ], edges: [{ from: 'ap', to: 'end' }] }
    const g = schemaToGraph(schema)
    const back = graphToSchema(g.nodes, g.edges)
    const ap = back.nodes.find(n => n.id === 'ap')!
    const stages = ap.stages!
    expect(stages).toHaveLength(2)
    expect(stages[1]!.maxLevels).toBe(2)
    expect(stages[0]!.approverStrategy).toBe('Specified')
  })

  it('validateClient flags invalid stage (managerChain without maxLevels)', () => {
    const schema: FlowSchemaDto = { start: 'start', nodes: [
      { id: 'start', type: 'start' },
      { id: 'ap', type: 'approval', stages: [{ kind: 'managerChain', countersign: 'all' }] },
      { id: 'end', type: 'end' },
    ], edges: [{ from: 'start', to: 'ap' }, { from: 'ap', to: 'end' }] }
    expect(validateClient(schema)).toContain('oa.designer.errStageInvalid')
  })

  it('validateClient passes valid serial stages', () => {
    const schema: FlowSchemaDto = { start: 'start', nodes: [
      { id: 'start', type: 'start' },
      { id: 'ap', type: 'approval', stages: [
        { kind: 'fixed', approverStrategy: 'Specified', countersign: 'all' },
        { kind: 'managerChain', maxLevels: 2, countersign: 'any' },
      ] },
      { id: 'end', type: 'end' },
    ], edges: [{ from: 'start', to: 'ap' }, { from: 'ap', to: 'end' }] }
    expect(validateClient(schema)).not.toContain('oa.designer.errStageInvalid')
  })
})

describe('approver advanced strategies', () => {
  it('round-trips formField/dataMap/group fields', () => {
    const schema: FlowSchemaDto = {
      start: 's',
      nodes: [
        { id: 's', type: 'start' },
        { id: 'a', type: 'approval', approverStrategy: 'Group',
          approverWhen: 'amount > 10', approverFilter: 'user.enable == true',
          approverMembers: [{ strategy: 'Starter' }, { strategy: 'Specified', approverUserId: 'u1' }] },
        { id: 'e', type: 'end' },
      ],
      edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e' }],
    }
    const { nodes, edges } = schemaToGraph(schema)
    const back = graphToSchema(nodes, edges)
    const a = back.nodes.find(n => n.id === 'a')!
    expect(a.approverMembers?.length).toBe(2)
    expect(a.approverWhen).toBe('amount > 10')
  })

  it('flags group node with empty members', () => {
    const schema: FlowSchemaDto = {
      start: 's',
      nodes: [{ id: 's', type: 'start' }, { id: 'a', type: 'approval', approverStrategy: 'Group' }, { id: 'e', type: 'end' }],
      edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e' }],
    }
    expect(validateClient(schema)).toContain('oa.designer.errApproverConfig')
  })
})
