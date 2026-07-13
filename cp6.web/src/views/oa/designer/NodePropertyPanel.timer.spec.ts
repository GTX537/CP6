// @vitest-environment jsdom
// 票8：timer（定时器）到点动作补 webApi 连接器/路径变体 + 互斥清理。
// 核心正确性：ServiceTaskActionRef.Snapshot 优先判 ConnectorName（timer + ConnectorName → webApi），
// 故选「回写/无」时必须清空 serviceConnectorName，否则到点静默外呼用户以为已删的连接器。
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { nextTick } from 'vue'
import type { SchemaNode } from './designerModel'

vi.mock('@/api/oa/designer', () => ({
  designerApi: {
    getServiceCatalog: vi.fn().mockResolvedValue({
      actions: [{ name: 'writeOrder', label: '写订单' }],
      connectors: [{ name: 'erpEcho', label: 'ERP回声' }],
    }),
  },
}))

import NodePropertyPanel from './NodePropertyPanel.vue'

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function timerNode(over: Partial<SchemaNode> = {}): SchemaNode {
  return { id: 's1', type: 'serviceTask', serviceKind: 'timer', ...over } as SchemaNode
}

function mountPanel(node: SchemaNode) {
  return mount(NodePropertyPanel, {
    props: { node },
    global: { plugins: [i18n, ElementPlus] },
  })
}

function state(w: ReturnType<typeof mountPanel>) {
  return (w.vm as unknown as { $: { setupState: Record<string, any> } }).$.setupState
}

describe('NodePropertyPanel timer 到点动作变体（票8）', () => {
  beforeEach(() => vi.clearAllMocks())

  // ── 审查修复回归：backing ref 使 write/api 可达（纯 computed 版 setter 只清字段，
  //    getter 因 connector/action 仍空立即弹回 'none'，子表单永不渲染=鸡生蛋不可达）──

  it('fresh timer 选 api：模式驻留（不弹回 none）且 connector/path 表单渲染', async () => {
    const w = mountPanel(timerNode()) // 全空字段的新建 timer
    await flushPromises()
    expect(state(w).timerActionKind).toBe('none')
    state(w).timerActionKind = 'api'
    await nextTick()
    expect(state(w).timerActionKind).toBe('api') // 纯 computed 版此处 'none'
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).toContain('oa.designer.svc.connector')
    expect(labels).toContain('oa.designer.svc.path')
  })

  it('fresh timer 选 write：模式驻留且到点动作下拉渲染', async () => {
    const w = mountPanel(timerNode())
    await flushPromises()
    state(w).timerActionKind = 'write'
    await nextTick()
    expect(state(w).timerActionKind).toBe('write')
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).toContain('oa.designer.svc.timerAction')
    expect(labels).not.toContain('oa.designer.svc.connector')
  })

  it('选 api（已填 connector）后再切 write：connector/path 被清且 action 表单出现', async () => {
    const w = mountPanel(timerNode({ serviceConnectorName: 'erpEcho', servicePath: '/o' }))
    await flushPromises()
    expect(state(w).timerActionKind).toBe('api') // 初始化按字段派生
    state(w).timerActionKind = 'write'
    await nextTick()
    expect(state(w).timerActionKind).toBe('write')
    expect(state(w).local.serviceConnectorName).toBeUndefined()
    expect(state(w).local.servicePath).toBeUndefined()
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).toContain('oa.designer.svc.timerAction')
    expect(labels).not.toContain('oa.designer.svc.connector')
  })

  it('timerActionKind 初始化：据已填字段派生 none/write/api', async () => {
    const wNone = mountPanel(timerNode())
    await flushPromises()
    expect(state(wNone).timerActionKind).toBe('none')

    const wWrite = mountPanel(timerNode({ serviceActionName: 'writeOrder' }))
    await flushPromises()
    expect(state(wWrite).timerActionKind).toBe('write')

    const wApi = mountPanel(timerNode({ serviceConnectorName: 'erpEcho', servicePath: '/o' }))
    await flushPromises()
    // Snapshot 优先 ConnectorName → 有连接器即 api（即便 actionName 也在也按 api）
    expect(state(wApi).timerActionKind).toBe('api')
  })

  it('setter：选 write 清连接器/路径（防 Snapshot 误判 webApi 到点外呼）', async () => {
    const w = mountPanel(timerNode({ serviceConnectorName: 'erpEcho', servicePath: '/o' }))
    await flushPromises()
    state(w).timerActionKind = 'write'
    await nextTick()
    expect(state(w).local.serviceConnectorName).toBeUndefined()
    expect(state(w).local.servicePath).toBeUndefined()
  })

  it('setter：选 api 清回写动作（互斥）', async () => {
    const w = mountPanel(timerNode({ serviceActionName: 'writeOrder' }))
    await flushPromises()
    state(w).timerActionKind = 'api'
    await nextTick()
    expect(state(w).local.serviceActionName).toBeUndefined()
  })

  it('setter：选 none 清全部三字段', async () => {
    const w = mountPanel(timerNode({ serviceConnectorName: 'erpEcho', servicePath: '/o', serviceActionName: 'writeOrder' }))
    await flushPromises()
    state(w).timerActionKind = 'none'
    await nextTick()
    expect(state(w).local.serviceConnectorName).toBeUndefined()
    expect(state(w).local.servicePath).toBeUndefined()
    expect(state(w).local.serviceActionName).toBeUndefined()
  })

  it('清理 watch：切到 timer 不清连接器/路径/动作（timer 三者可能合法）', async () => {
    const w = mountPanel(timerNode({ serviceKind: 'webApi', serviceConnectorName: 'erpEcho', servicePath: '/o' }))
    await flushPromises()
    state(w).local.serviceKind = 'timer'
    await nextTick()
    expect(state(w).local.serviceConnectorName).toBe('erpEcho')
    expect(state(w).local.servicePath).toBe('/o')
  })

  it('清理 watch：切到 dataWriteback 清连接器/路径；切到 webApi 清到点动作', async () => {
    const w = mountPanel(timerNode({ serviceConnectorName: 'erpEcho', servicePath: '/o' }))
    await flushPromises()
    state(w).local.serviceKind = 'dataWriteback'
    await nextTick()
    expect(state(w).local.serviceConnectorName).toBeUndefined()
    expect(state(w).local.servicePath).toBeUndefined()

    const w2 = mountPanel(timerNode({ serviceActionName: 'writeOrder' }))
    await flushPromises()
    state(w2).local.serviceKind = 'webApi'
    await nextTick()
    expect(state(w2).local.serviceActionName).toBeUndefined()
  })

  it('模板：timerActionKind=api 渲染连接器 + 路径入口', async () => {
    const w = mountPanel(timerNode({ serviceConnectorName: 'erpEcho', servicePath: '/o' }))
    await flushPromises()
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    // 连接器/路径 label（i18n key，messages 空 → 回落 key 文本）
    expect(labels).toContain('oa.designer.svc.connector')
    expect(labels).toContain('oa.designer.svc.path')
  })
})
