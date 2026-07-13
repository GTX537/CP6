// @vitest-environment jsdom
// D-T2：属性面板「分支驳回策略」段（parallelSplit / inclusiveSplit 专属）。
// 契约：默认 cascade 不落 schema（onBranchReject=undefined，旧流程零污染，与后端 null=cascade 同义）；
//       选 prune 才写 onBranchReject='prune'。
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { nextTick } from 'vue'
import type { SchemaNode } from './designerModel'

vi.mock('@/api/oa/designer', () => ({
  designerApi: {
    getServiceCatalog: vi.fn().mockResolvedValue({ actions: [], connectors: [] }),
  },
}))

import NodePropertyPanel from './NodePropertyPanel.vue'

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function mountPanel(node: SchemaNode) {
  return mount(NodePropertyPanel, {
    props: { node },
    global: { plugins: [i18n, ElementPlus] },
  })
}

function state(w: ReturnType<typeof mountPanel>) {
  return (w.vm as unknown as { $: { setupState: Record<string, any> } }).$.setupState
}

describe('NodePropertyPanel 分支驳回策略段（D-T2）', () => {
  it('parallelSplit → isSplitGateway 真、渲染分支驳回下拉', async () => {
    const w = mountPanel({ id: 'g', type: 'parallelSplit' } as SchemaNode)
    await flushPromises()
    expect(state(w).isSplitGateway).toBe(true)
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).toContain('oa.designer.gw.branchReject')
  })

  it('inclusiveSplit → 渲染分支驳回下拉', async () => {
    const w = mountPanel({ id: 'g', type: 'inclusiveSplit' } as SchemaNode)
    await flushPromises()
    expect(state(w).isSplitGateway).toBe(true)
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).toContain('oa.designer.gw.branchReject')
  })

  it('approval（非 split）→ 不渲染分支驳回段', async () => {
    const w = mountPanel({ id: 'a', type: 'approval', approverStrategy: 'Starter' } as SchemaNode)
    await flushPromises()
    expect(state(w).isSplitGateway).toBe(false)
    const labels = w.findAll('.el-form-item__label').map(n => n.text())
    expect(labels).not.toContain('oa.designer.gw.branchReject')
  })

  it('parallelJoin（非 split）→ 不渲染分支驳回段', async () => {
    const w = mountPanel({ id: 'j', type: 'parallelJoin' } as SchemaNode)
    await flushPromises()
    expect(state(w).isSplitGateway).toBe(false)
  })

  it('branchReject 默认 cascade（onBranchReject 未落）', async () => {
    const w = mountPanel({ id: 'g', type: 'parallelSplit' } as SchemaNode)
    await flushPromises()
    expect(state(w).branchReject).toBe('cascade')
    expect(state(w).local.onBranchReject).toBeUndefined()
  })

  it('选 prune → 写 onBranchReject=prune', async () => {
    const w = mountPanel({ id: 'g', type: 'parallelSplit' } as SchemaNode)
    await flushPromises()
    state(w).branchReject = 'prune'
    await nextTick()
    expect(state(w).local.onBranchReject).toBe('prune')
  })

  it('选回 cascade → 清 onBranchReject（旧流程零污染）', async () => {
    const w = mountPanel({ id: 'g', type: 'parallelSplit', onBranchReject: 'prune' } as SchemaNode)
    await flushPromises()
    expect(state(w).branchReject).toBe('prune')
    state(w).branchReject = 'cascade'
    await nextTick()
    expect(state(w).local.onBranchReject).toBeUndefined()
  })
})
