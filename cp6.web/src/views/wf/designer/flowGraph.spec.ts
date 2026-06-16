import { describe, it, expect } from 'vitest'
import { defaultNode, addEdge, removeNodeCascade, reachableEndIds } from './flowGraph'
import { validateFlowSchema } from './designValidate'
import type { FlowDesignNode, FlowDesignEdge, FlowDesignSchema } from '@/types/wf/wf'

// OA 章09 §3 流程图拓扑 + 设计时校验。
describe('flowGraph', () => {
  it('defaultNode 生成不重复 id', () => {
    const nodes: FlowDesignNode[] = []
    const s = defaultNode('start', 0, 0, nodes); nodes.push(s)
    const n1 = defaultNode('approval', 0, 0, nodes); nodes.push(n1)
    const n2 = defaultNode('approval', 0, 0, nodes); nodes.push(n2)
    const e = defaultNode('end', 0, 0, nodes)
    expect(s.id).toBe('start')
    expect(n1.id).toBe('n1')
    expect(n2.id).toBe('n2')
    expect(e.id).toBe('end')
    expect(n1.countersign).toBe('all')
    expect(n1.approverStrategy).toBe('Specified')
  })

  it('addEdge 去自环去重', () => {
    const edges: FlowDesignEdge[] = []
    expect(addEdge(edges, 'a', 'a')).toBe(false) // 自环
    expect(addEdge(edges, 'a', 'b')).toBe(true)
    expect(addEdge(edges, 'a', 'b')).toBe(false) // 重复
    expect(edges.length).toBe(1)
  })

  it('removeNodeCascade 级联删边', () => {
    const nodes: FlowDesignNode[] = [
      { id: 'a', type: 'approval', x: 0, y: 0 },
      { id: 'b', type: 'end', x: 0, y: 0 },
    ]
    const edges: FlowDesignEdge[] = [{ from: 'a', to: 'b' }]
    removeNodeCascade(nodes, edges, 'a')
    expect(nodes.length).toBe(1)
    expect(edges.length).toBe(0) // 相关边一并删
  })

  it('reachableEndIds 反向可达', () => {
    const nodes: FlowDesignNode[] = [
      { id: 'start', type: 'start', x: 0, y: 0 },
      { id: 'n1', type: 'approval', x: 0, y: 0 },
      { id: 'orphan', type: 'approval', x: 0, y: 0 },
      { id: 'end', type: 'end', x: 0, y: 0 },
    ]
    const edges: FlowDesignEdge[] = [
      { from: 'start', to: 'n1' },
      { from: 'n1', to: 'end' },
    ]
    const can = reachableEndIds(nodes, edges)
    expect(can.has('n1')).toBe(true)
    expect(can.has('start')).toBe(true)
    expect(can.has('orphan')).toBe(false) // 断头
  })
})

describe('validateFlowSchema', () => {
  const good: FlowDesignSchema = {
    start: 'start',
    nodes: [
      { id: 'start', type: 'start', x: 0, y: 0 },
      { id: 'n1', type: 'approval', x: 0, y: 0, approverStrategy: 'Specified', approverUserId: 'u1', countersign: 'all' },
      { id: 'end', type: 'end', x: 0, y: 0 },
    ],
    edges: [
      { from: 'start', to: 'n1' },
      { from: 'n1', to: 'end' },
    ],
  }

  it('合法流程无错', () => {
    expect(validateFlowSchema(good)).toEqual([])
  })

  it('缺结束节点报错', () => {
    const s: FlowDesignSchema = { nodes: [{ id: 'n1', type: 'approval', x: 0, y: 0, approverStrategy: 'Specified', approverUserId: 'u' }], edges: [] }
    expect(validateFlowSchema(s).some((e) => e.includes('结束节点'))).toBe(true)
  })

  it('断头审批节点报错', () => {
    const s: FlowDesignSchema = {
      nodes: [
        { id: 'n1', type: 'approval', x: 0, y: 0, approverStrategy: 'Specified', approverUserId: 'u' },
        { id: 'end', type: 'end', x: 0, y: 0 },
      ],
      edges: [], // n1 连不到 end
    }
    expect(validateFlowSchema(s).some((e) => e.includes('无法到达结束'))).toBe(true)
  })

  it('审批人不完整报错', () => {
    const s: FlowDesignSchema = {
      nodes: [
        { id: 'n1', type: 'approval', x: 0, y: 0, approverStrategy: 'Specified' }, // 缺 approverUserId
        { id: 'end', type: 'end', x: 0, y: 0 },
      ],
      edges: [{ from: 'n1', to: 'end' }],
    }
    expect(validateFlowSchema(s).some((e) => e.includes('指定审批人为空'))).toBe(true)
  })
})
