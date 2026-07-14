// @vitest-environment jsdom
// E-T2：属性面板「子流程配置」段（subFlow 专属）。
// 契约：目标流程下拉懒加载 designerApi.list() 过滤 enable && flowKey!==当前；父→子/子→父映射编辑；
//       多实例开关 subMulti = backing ref（防「纯 computed 版鸡生蛋」——开启即 collectionVar='' → getter 弹回 false，
//       同波⑤ timerActionKind 终审教训 631f0e2）；关闭多实例须清 collectionVar+policy（防静默残留配置）。
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { nextTick } from 'vue'
import type { SchemaNode } from './designerModel'

vi.mock('@/api/oa/designer', () => ({
  designerApi: {
    getServiceCatalog: vi.fn().mockResolvedValue({ actions: [], connectors: [] }),
    // 已发布流程目录：含自身(self)、停用(disabled)、合法(childA/childB)
    list: vi.fn().mockResolvedValue([
      { flowKey: 'self',   flowName: '当前流程', formKey: 'f', version: 1, enable: true },
      { flowKey: 'disabled', flowName: '停用流程', formKey: 'f', version: 1, enable: false },
      { flowKey: 'childA', flowName: '子流程A', formKey: 'f', version: 1, enable: true },
      { flowKey: 'childB', flowName: '子流程B', formKey: 'f', version: 1, enable: true },
    ]),
  },
}))

import NodePropertyPanel from './NodePropertyPanel.vue'

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function subFlowNode(over: Partial<SchemaNode> = {}): SchemaNode {
  return { id: 'sf1', type: 'subFlow', ...over } as SchemaNode
}

function mountPanel(node: SchemaNode, currentFlowKey = 'self') {
  return mount(NodePropertyPanel, {
    props: { node, currentFlowKey },
    global: { plugins: [i18n, ElementPlus] },
  })
}

function state(w: ReturnType<typeof mountPanel>) {
  return (w.vm as unknown as { $: { setupState: Record<string, any> } }).$.setupState
}

describe('NodePropertyPanel 子流程配置段（E-T2）', () => {
  beforeEach(() => vi.clearAllMocks())

  it('subFlow 节点 → isSubFlow 真、渲染目标/映射/多实例入口', async () => {
    const w = mountPanel(subFlowNode())
    await flushPromises()
    expect(state(w).isSubFlow).toBe(true)
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).toContain('oa.designer.subflow.target')
    expect(labels).toContain('oa.designer.subflow.varsIn')
    expect(labels).toContain('oa.designer.subflow.varsOut')
    expect(labels).toContain('oa.designer.subflow.multi')
  })

  it('非 subFlow 节点 → 不渲染子流程段', async () => {
    const w = mountPanel({ id: 'a', type: 'approval', approverStrategy: 'Starter' } as SchemaNode)
    await flushPromises()
    expect(state(w).isSubFlow).toBe(false)
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).not.toContain('oa.designer.subflow.target')
  })

  it('目标下拉懒加载：过滤 enable && flowKey!==当前流程', async () => {
    const w = mountPanel(subFlowNode(), 'self')
    await flushPromises()
    const keys = state(w).publishedFlows.map((d: { flowKey: string }) => d.flowKey)
    expect(keys).toEqual(['childA', 'childB'])   // self 排除（自身）、disabled 排除（停用）
    expect(keys).not.toContain('self')
    expect(keys).not.toContain('disabled')
  })

  it('subMulti backing ref：开启多实例不被 getter 弹回 false（鸡生蛋防护）', async () => {
    const w = mountPanel(subFlowNode())
    await flushPromises()
    expect(state(w).subMulti).toBe(false)
    state(w).subMulti = true
    await nextTick()
    expect(state(w).subMulti).toBe(true)   // 纯 computed 版此处会弹回 false（collectionVar='' → getter false）
    // 多实例子字段渲染
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).toContain('oa.designer.subflow.collectionVar')
    expect(labels).toContain('oa.designer.subflow.policy')
  })

  it('开启多实例：policy 默认 all，且不强塞 collectionVar 空串（待用户填）', async () => {
    const w = mountPanel(subFlowNode())
    await flushPromises()
    state(w).subMulti = true
    await nextTick()
    expect(state(w).local.subCompletionPolicy).toBe('all')
  })

  it('关闭多实例：清 subCollectionVar + subCompletionPolicy（防静默残留配置）', async () => {
    const w = mountPanel(subFlowNode({ subCollectionVar: 'items', subCompletionPolicy: 'any' }))
    await flushPromises()
    expect(state(w).subMulti).toBe(true)    // 初始化按字段派生
    state(w).subMulti = false
    await nextTick()
    expect(state(w).local.subCollectionVar).toBeUndefined()
    expect(state(w).local.subCompletionPolicy).toBeUndefined()
  })

  it('subMulti 初始化：据 subCollectionVar 派生（有值 true / 空 false）', async () => {
    const wOn = mountPanel(subFlowNode({ subCollectionVar: 'lines' }))
    await flushPromises()
    expect(state(wOn).subMulti).toBe(true)

    const wOff = mountPanel(subFlowNode())
    await flushPromises()
    expect(state(wOff).subMulti).toBe(false)
  })

  it('subFlowKey 经 v-model 落 local（面板编辑 → data 字段，round-trip 由 E-T1 spread 承载）', async () => {
    const w = mountPanel(subFlowNode())
    await flushPromises()
    state(w).local.subFlowKey = 'childA'
    await nextTick()
    expect(state(w).local.subFlowKey).toBe('childA')
  })
})
