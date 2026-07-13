import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, NODE_PALETTE } from './designerModel'

describe('serviceTask round-trip', () => {
  it('palette has 3 serviceTask entries', () => {
    const st = NODE_PALETTE.filter(p => p.type === 'serviceTask')
    expect(st.map(p => (p as any).kind).sort()).toEqual(['dataWriteback','timer','webApi'])
  })
  it('schemaToGraph/graphToSchema preserves Service* fields', () => {
    const schema = { nodes:[{ id:'s', type:'serviceTask', serviceKind:'webApi', serviceMode:'async',
      serviceConnectorName:'erpEcho', servicePath:'/o', serviceParamsJson:'{}', serviceMaxRetries:3 }],
      edges:[{ from:'s', to:'e', isError:true }] }
    const back = graphToSchema(schemaToGraph(schema as any))
    expect(back.nodes[0]!.serviceKind).toBe('webApi')
    expect(back.nodes[0]!.serviceConnectorName).toBe('erpEcho')
    expect(back.edges[0]!.isError).toBe(true)
  })
  it('validateClient flags incomplete serviceTask', () => {
    const schema = { nodes:[{ id:'s', type:'serviceTask', serviceKind:'webApi' /* 缺 connector/path */ }], edges:[] }
    const errs = validateClient(schema as any)
    expect(errs.some(e => e.includes('errServiceConfig') || e.includes('服务'))).toBe(true)
  })
  it('validateClient flags timer with delayValue but no delayMode (radio 未点，镜像后端 E-WF-016)', () => {
    const schema = { nodes:[{ id:'s', type:'serviceTask', serviceKind:'timer', serviceDelayValue:'3d' /* 缺 delayMode */ }], edges:[] }
    const errs = validateClient(schema as any)
    expect(errs).toContain('oa.designer.errServiceConfig')
  })
})

describe('error edge visual', () => {
  it('isError edge gets danger dashed style; normal edge does not', () => {
    const g = schemaToGraph({
      nodes: [
        { id: 'svc', type: 'serviceTask' } as any,
        { id: 'end', type: 'end' } as any,
        { id: 'h', type: 'approval' } as any,
      ],
      edges: [
        { from: 'svc', to: 'end' },              // 普通边
        { from: 'svc', to: 'h', isError: true }, // 失败边
      ],
    } as any)

    const normal = g.edges.find(e => e.target === 'end')!
    const err = g.edges.find(e => e.target === 'h')!

    // 普通边无自定义 stroke；失败边用 danger token 虚线
    expect((err.style as any)?.stroke).toBe('var(--cp-danger)')
    expect((err.style as any)?.strokeDasharray).toBeTruthy()
    expect((normal as any).style?.stroke).toBeUndefined()
    // data.isError round-trip 不受影响
    expect((err.data as any)?.isError).toBe(true)
  })
})
