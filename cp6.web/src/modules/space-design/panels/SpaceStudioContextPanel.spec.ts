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

  it('exposes the missing underlay calibration action after a source is attached', async () => {
    const wrapper = mount(SpaceStudioContextPanel, {
      props: {
        hasUnderlay: true,
        calibrated: false,
        readonly: false,
      },
    })

    const calibrate = wrapper.get('[data-test="calibrate-underlay"]')
    expect(calibrate.text()).toBe('标定底图')
    await calibrate.trigger('click')
    expect(wrapper.emitted('calibrateUnderlay')).toHaveLength(1)

    await wrapper.setProps({ calibrated: true, readonly: true })
    expect(calibrate.text()).toBe('重新标定底图')
    expect(calibrate.attributes('disabled')).toBeDefined()
  })
})
