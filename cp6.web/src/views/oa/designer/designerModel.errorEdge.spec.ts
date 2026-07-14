import { describe, it, expect } from 'vitest'
import { validateClient, TIMEOUT_ACTIONS, ERROR_EDGE_SOURCE_TYPES } from './designerModel'
import type { FlowSchemaDto } from './designerModel'

// ── B-T2：设计器镜像 B-T1 静态规则（errorEdge 超时动作 + E-WF-027 + ErrorEdgeSourceTypes 放宽）──

describe('timeout action enum 含 errorEdge（超时走失败边）', () => {
  it('TIMEOUT_ACTIONS 含 errorEdge 项，标签走 oa.designer.timeout.errorEdge（F-T1 入库前 t() 回退）', () => {
    const item = TIMEOUT_ACTIONS.find(a => a.value === 'errorEdge')
    expect(item).toBeTruthy()
    expect(item!.label).toBe('oa.designer.timeout.errorEdge')
  })
  it('保留既有四项 remind/approve/reject/escalate', () => {
    expect(TIMEOUT_ACTIONS.map(a => a.value)).toEqual(
      expect.arrayContaining(['remind', 'approve', 'reject', 'escalate', 'errorEdge']),
    )
  })
})

describe('ERROR_EDGE_SOURCE_TYPES 前端等价常量（镜像后端 {serviceTask, approval, subFlow}）', () => {
  it('含三型（小写归一，OrdinalIgnoreCase 姿态）', () => {
    expect(ERROR_EDGE_SOURCE_TYPES.has('servicetask')).toBe(true)
    expect(ERROR_EDGE_SOURCE_TYPES.has('approval')).toBe(true)
    expect(ERROR_EDGE_SOURCE_TYPES.has('subflow')).toBe(true)
  })
})

describe('validateClient E-WF-027 镜像：errorEdge 超时动作须有 IsError 出边', () => {
  const base = (): FlowSchemaDto => ({
    start: 's',
    nodes: [
      { id: 's', type: 'start' },
      { id: 'a', type: 'approval', approverStrategy: 'Starter', timeoutAction: 'errorEdge' },
      { id: 'e', type: 'end' },
    ],
    edges: [
      { from: 's', to: 'a' },
      { from: 'a', to: 'e' }, // 无 IsError 出边
    ],
  })

  it('配 errorEdge 但节点无 IsError 出边 → 含 errTimeoutErrorEdge（E-WF-027）', () => {
    expect(validateClient(base())).toContain('oa.designer.errTimeoutErrorEdge')
  })

  it('配 errorEdge 且挂了 IsError 出边 → 不报 E-WF-027', () => {
    const s = base()
    s.nodes.push({ id: 'h', type: 'end' })
    s.edges.push({ from: 'a', to: 'h', isError: true })
    expect(validateClient(s)).not.toContain('oa.designer.errTimeoutErrorEdge')
  })
})

describe('approval 节点可挂 IsError 边（不再报 017；来源集含 approval）', () => {
  it('approval 来源的 IsError 边不触发 errErrorEdgeSource', () => {
    const s: FlowSchemaDto = {
      start: 's',
      nodes: [
        { id: 's', type: 'start' },
        { id: 'a', type: 'approval', approverStrategy: 'Starter' },
        { id: 'e', type: 'end' },
        { id: 'h', type: 'end' },
      ],
      edges: [
        { from: 's', to: 'a' },
        { from: 'a', to: 'e' },
        { from: 'a', to: 'h', isError: true }, // approval 来源失败边——放行
      ],
    }
    expect(validateClient(s)).not.toContain('oa.designer.errErrorEdgeSource')
  })

  it('来源类型不在集合内（如 start）的 IsError 边 → 报 errErrorEdgeSource', () => {
    const s: FlowSchemaDto = {
      start: 's',
      nodes: [
        { id: 's', type: 'start' },
        { id: 'e', type: 'end' },
      ],
      edges: [
        { from: 's', to: 'e', isError: true }, // start 来源不在 {serviceTask,approval,subFlow}
      ],
    }
    expect(validateClient(s)).toContain('oa.designer.errErrorEdgeSource')
  })
})
