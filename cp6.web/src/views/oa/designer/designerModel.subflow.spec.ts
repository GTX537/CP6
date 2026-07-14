import { describe, it, expect } from 'vitest'
import {
  NODE_PALETTE, schemaToGraph, graphToSchema, validateClient,
  type FlowSchemaDto, type SchemaNode,
} from './designerModel'

const subNode = (over: Partial<SchemaNode> = {}): SchemaNode => ({
  id: 'sub', type: 'subFlow', subFlowKey: 'fk-child', ...over,
})

const schemaWith = (n: SchemaNode, edges?: FlowSchemaDto['edges']): FlowSchemaDto => ({
  start: 's',
  nodes: [{ id: 's', type: 'start' }, n, { id: 'e', type: 'end' }],
  edges: edges ?? [{ from: 's', to: 'sub' }, { from: 'sub', to: 'e' }],
})

describe('designerModel subFlow', () => {
  it('palette 含 subFlow 入口', () => {
    expect(NODE_PALETTE.some(p => p.type === 'subFlow')).toBe(true)
  })

  it('round-trip 保全五字段', () => {
    const schema = schemaWith(subNode({
      subVarsInJson: '{"a":"$.x"}', subVarsOutJson: '{"y":"$.b"}',
      subCollectionVar: 'items', subCompletionPolicy: 'any',
    }))
    const back = graphToSchema(schemaToGraph(schema))
    const sub = back.nodes.find(n => n.id === 'sub')!
    expect(sub.subFlowKey).toBe('fk-child')
    expect(sub.subVarsInJson).toBe('{"a":"$.x"}')
    expect(sub.subVarsOutJson).toBe('{"y":"$.b"}')
    expect(sub.subCollectionVar).toBe('items')
    expect(sub.subCompletionPolicy).toBe('any')
  })

  it('validateClient: 合法配置零错误', () => {
    expect(validateClient(schemaWith(subNode()))).toEqual([])
  })

  it('validateClient: 缺 subFlowKey → errSubFlowConfig', () => {
    expect(validateClient(schemaWith(subNode({ subFlowKey: '' }))))
      .toContain('oa.designer.errSubFlowConfig')
  })

  it('validateClient: 非法完成策略 → errSubFlowConfig', () => {
    expect(validateClient(schemaWith(subNode({ subCompletionPolicy: 'quorum' }))))
      .toContain('oa.designer.errSubFlowConfig')
  })

  it('validateClient: 策略大小写/空白归一化放行（镜像后端 Trim+ToLowerInvariant，审查探针转正）', () => {
    expect(validateClient(schemaWith(subNode({ subCompletionPolicy: 'All' })))).toEqual([])
    expect(validateClient(schemaWith(subNode({ subCompletionPolicy: ' all ' })))).toEqual([])
  })

  it('validateClient: 集合变量空串 → errSubFlowConfig', () => {
    expect(validateClient(schemaWith(subNode({ subCollectionVar: '  ' }))))
      .toContain('oa.designer.errSubFlowConfig')
  })

  it('validateClient: 映射 JSON 非法/含下标 → errSubFlowConfig', () => {
    expect(validateClient(schemaWith(subNode({ subVarsInJson: '{bad' }))))
      .toContain('oa.designer.errSubFlowConfig')
    expect(validateClient(schemaWith(subNode({ subVarsOutJson: '{"a":"$.items[0]"}' }))))
      .toContain('oa.designer.errSubFlowConfig')
  })

  it('validateClient: 值内普通点串带方括号放行（镜像后端 ContainsUnsupportedSubscript 双正则，审查探针转正）', () => {
    expect(validateClient(schemaWith(subNode({ subVarsInJson: '{"note":"file.test[old]"}' })))).toEqual([])
  })

  it('validateClient: 无非错误出边 → errSubFlowConfig（镜像后端 E-WF-025 静态部分）', () => {
    const bad = schemaWith(subNode(), [
      { from: 's', to: 'sub' },
      { from: 'sub', to: 'e', isError: true },
    ])
    expect(validateClient(bad)).toContain('oa.designer.errSubFlowConfig')
  })
})
