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
})
