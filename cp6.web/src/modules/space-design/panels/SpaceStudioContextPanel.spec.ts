// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import SpaceStudioContextPanel from './SpaceStudioContextPanel.vue'

describe('SpaceStudioContextPanel', () => {
  it('exposes the business layout editor only in the single active asset context', async () => {
    const wrapper = mount(SpaceStudioContextPanel, {
      props: {
        hasUnderlay: false,
        calibrated: false,
        readonly: false,
      },
      slots: {
        assets: '<div data-test="layout-editor">layout editor</div>',
      },
    })

    expect(wrapper.find('[data-test="layout-editor"]').exists()).toBe(false)
    await wrapper.findAll('.studio-modebar button')[1]!.trigger('click')
    expect(wrapper.get('[data-test="layout-editor"]').text()).toBe('layout editor')
  })
})
