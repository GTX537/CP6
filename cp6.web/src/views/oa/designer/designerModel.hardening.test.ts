import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, NODE_PALETTE } from './designerModel'
import type { FlowSchemaDto } from './designerModel'

describe('inclusive gateway & branch-reject (kernel hardening)', () => {
  it('palette has inclusiveSplit/inclusiveJoin entries', () => {
    expect(NODE_PALETTE.some(p => p.type === 'inclusiveSplit')).toBe(true)
    expect(NODE_PALETTE.some(p => p.type === 'inclusiveJoin')).toBe(true)
  })

  it('onBranchReject round-trips through graph conversion', () => {
    const schema: FlowSchemaDto = {
      start: 's',
      nodes: [
        { id: 's', type: 'start' },
        { id: 'g', type: 'parallelSplit', onBranchReject: 'prune' },
      ],
      edges: [{ from: 's', to: 'g' }],
    }
    const back = graphToSchema(schemaToGraph(schema))
    expect(back.nodes.find(n => n.id === 'g')?.onBranchReject).toBe('prune')
  })

  const incBase = (): FlowSchemaDto => ({
    start: 's',
    nodes: [
      { id: 's', type: 'start' },
      { id: 'g', type: 'inclusiveSplit' },
      { id: 'a', type: 'approval', approverStrategy: 'Starter' },
      { id: 'd', type: 'approval', approverStrategy: 'Starter' },
      { id: 'j', type: 'inclusiveJoin' },
      { id: 'e', type: 'end' },
    ],
    edges: [
      { from: 's', to: 'g' },
      { from: 'g', to: 'a', condition: 'x > 0' },
      { from: 'g', to: 'd' },                          // default
      { from: 'a', to: 'j' }, { from: 'd', to: 'j' },
      { from: 'j', to: 'e' },
    ],
  })

  it('valid inclusive pair passes', () => {
    expect(validateClient(incBase())).toEqual([])
  })

  it('missing default edge -> errInclusiveDefault (E-WF-020 mirror)', () => {
    const s = incBase()
    s.edges.find(e => e.from === 'g' && e.to === 'd')!.condition = 'y > 0'
    expect(validateClient(s)).toContain('oa.designer.errInclusiveDefault')
  })

  it('two default edges -> errInclusiveDefault', () => {
    const s = incBase()
    s.edges.find(e => e.from === 'g' && e.to === 'a')!.condition = undefined
    expect(validateClient(s)).toContain('oa.designer.errInclusiveDefault')
  })

  it('paired with parallelJoin -> errInclusivePair (E-WF-021 mirror)', () => {
    const s = incBase()
    s.nodes.find(n => n.id === 'j')!.type = 'parallelJoin'
    expect(validateClient(s)).toContain('oa.designer.errInclusivePair')
  })

  it('orphan inclusiveJoin -> errInclusivePair', () => {
    const s: FlowSchemaDto = {
      start: 's',
      nodes: [
        { id: 's', type: 'start' },
        { id: 'a', type: 'approval', approverStrategy: 'Starter' },
        { id: 'j', type: 'inclusiveJoin' },
        { id: 'e', type: 'end' },
      ],
      edges: [
        { from: 's', to: 'a' }, { from: 'a', to: 'j' }, { from: 's', to: 'j' },
        { from: 'j', to: 'e' },
      ],
    }
    expect(validateClient(s)).toContain('oa.designer.errInclusivePair')
  })

  it('onBranchReject bad value / wrong node -> errBranchReject (E-WF-021c mirror)', () => {
    const s1 = incBase()
    s1.nodes.find(n => n.id === 'g')!.onBranchReject = 'explode' as any
    expect(validateClient(s1)).toContain('oa.designer.errBranchReject')
    const s2 = incBase()
    s2.nodes.find(n => n.id === 'a')!.onBranchReject = 'prune'
    expect(validateClient(s2)).toContain('oa.designer.errBranchReject')
  })
})
