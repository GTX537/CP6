// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import DesignBatchToolsPanel from './DesignBatchToolsPanel.vue'

describe('DesignBatchToolsPanel', () => {
  it('exposes merge only for an eligible common-element selection', async () => {
    const wrapper = mount(DesignBatchToolsPanel, {
      props: {
        selectedCount: 2,
        canMerge: false,
        mergeHint: '元素类型必须一致',
      },
      global: {
        plugins: [ElementPlus],
        directives: { permission: {} },
      },
    })
    const merge = wrapper.get('[data-test="merge-elements"]')

    expect(merge.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('元素类型必须一致')

    await wrapper.setProps({ canMerge: true, mergeHint: undefined })
    await merge.trigger('click')

    expect(wrapper.emitted('merge')).toHaveLength(1)
  })

  it('exposes split only for an eligible group selection', async () => {
    const wrapper = mount(DesignBatchToolsPanel, {
      props: {
        selectedCount: 1,
        canSplit: false,
        splitHint: '请选择一个组合元素进行拆分',
      },
      global: {
        plugins: [ElementPlus],
        directives: { permission: {} },
      },
    })
    const split = wrapper.get('[data-test="split-element"]')

    expect(split.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('请选择一个组合元素进行拆分')

    await wrapper.setProps({ canSplit: true, splitHint: undefined })
    await split.trigger('click')

    expect(wrapper.emitted('split')).toHaveLength(1)
  })

  it('exposes copy only for an eligible editor selection', async () => {
    const wrapper = mount(DesignBatchToolsPanel, {
      props: {
        selectedCount: 1,
        canCopy: false,
        copyHint: '资产实例不能通过通用元素复制',
      },
      global: {
        plugins: [ElementPlus],
        directives: { permission: {} },
      },
    })
    const copy = wrapper.get('[data-test="copy-objects"]')

    expect(copy.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('资产实例不能通过通用元素复制')

    await wrapper.setProps({ canCopy: true, copyHint: undefined })
    await copy.trigger('click')

    expect(wrapper.emitted('copy')).toHaveLength(1)
  })
})
