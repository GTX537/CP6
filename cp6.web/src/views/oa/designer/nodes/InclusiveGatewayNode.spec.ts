// @vitest-environment jsdom
// D-T2：包容网关节点组件（BPMN 惯例 菱形+内嵌空心圆，区别 parallel 实心菱形）。
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import InclusiveGatewayNode from './InclusiveGatewayNode.vue'

const i18n = createI18n({ legacy: false, locale: 'zh', missingWarn: false, fallbackWarn: false, messages: {} })

function mountNode(data: Record<string, unknown>) {
  return mount(InclusiveGatewayNode, {
    props: { id: 'g1', type: 'inclusiveSplit', selected: false, data } as any,
    global: { plugins: [i18n], stubs: { Handle: true } },
  })
}

describe('InclusiveGatewayNode（D-T2）', () => {
  it('inclusiveSplit → 渲染 split 文案键', () => {
    const w = mountNode({ type: 'inclusiveSplit' })
    expect(w.text()).toContain('oa.designer.gw.inclusiveSplit')
  })

  it('inclusiveJoin → 渲染 join 文案键', () => {
    const w = mountNode({ type: 'inclusiveJoin' })
    expect(w.text()).toContain('oa.designer.gw.inclusiveJoin')
  })

  it('渲染 BPMN 空心圆记号（区别 parallel 实心菱形）', () => {
    const w = mountNode({ type: 'inclusiveSplit' })
    expect(w.find('.inc-circle').exists()).toBe(true)
  })

  it('selected → 菱形壳挂选中态类', () => {
    const w = mount(InclusiveGatewayNode, {
      props: { id: 'g1', type: 'inclusiveSplit', selected: true, data: { type: 'inclusiveSplit' } } as any,
      global: { plugins: [i18n], stubs: { Handle: true } },
    })
    expect(w.find('.vf-node--selected').exists()).toBe(true)
  })
})
