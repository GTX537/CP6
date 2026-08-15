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
        underlayVisible: true,
        underlayOpacity: 55,
        underlayLocked: true,
      },
      slots: {
        assets: '<div data-test="layout-editor">layout editor</div>',
      },
    })

    expect(wrapper.find('[data-test="layout-editor"]').exists()).toBe(false)
    await wrapper.findAll('.studio-modebar button')[1]!.trigger('click')
    expect(wrapper.get('[data-test="layout-editor"]').text()).toBe('layout editor')
  })

  it('offers the frozen pallet and six static-equipment presets', async () => {
    const wrapper = mount(SpaceStudioContextPanel, {
      props: {
        hasUnderlay: false,
        calibrated: false,
        readonly: false,
        underlayVisible: true,
        underlayOpacity: 55,
        underlayLocked: true,
      },
    })

    await wrapper.findAll('.studio-modebar button')[1]!.trigger('click')
    expect(wrapper.findAll('.component-grid button').map(button => button.text()))
      .toEqual([
        '+ 墙体',
        '+ 柱',
        '+ 门',
        '+ 月台',
        '+ 托盘',
        '+ 输送线',
        '+ AGV',
        '+ 叉车',
        '+ 工作台',
        '+ 电子秤',
        '+ 充电站',
      ])

    await wrapper.get('[data-test="component-preset-agv"]').trigger('click')
    expect(wrapper.emitted('createComponent')).toEqual([['agv']])

    await wrapper.setProps({ readonly: true })
    expect(wrapper.findAll('.component-grid button').every(
      button => button.attributes('disabled') !== undefined,
    )).toBe(true)
  })

  it('exposes the missing underlay calibration action after a source is attached', async () => {
    const wrapper = mount(SpaceStudioContextPanel, {
      props: {
        hasUnderlay: true,
        calibrated: false,
        readonly: false,
        underlayVisible: true,
        underlayOpacity: 55,
        underlayLocked: false,
      },
    })

    const calibrate = wrapper.get('[data-test="calibrate-underlay"]')
    expect(calibrate.text()).toBe('标定底图')
    await calibrate.trigger('click')
    expect(wrapper.emitted('calibrateUnderlay')).toHaveLength(1)
    const remove = wrapper.get('[data-test="remove-underlay"]')
    await remove.trigger('click')
    expect(wrapper.emitted('removeUnderlay')).toHaveLength(1)

    await wrapper.setProps({ calibrated: true, readonly: true, underlayLocked: true })
    expect(calibrate.text()).toBe('重新标定底图')
    expect(calibrate.attributes('disabled')).toBeDefined()
    expect(remove.attributes('disabled')).toBeDefined()
  })

  it('prevents calibration while the underlay view layer is locked', async () => {
    const wrapper = mount(SpaceStudioContextPanel, {
      props: {
        hasUnderlay: true,
        calibrated: true,
        readonly: false,
        underlayVisible: true,
        underlayOpacity: 55,
        underlayLocked: true,
      },
    })

    const calibrate = wrapper.get('[data-test="calibrate-underlay"]')
    expect(calibrate.attributes('disabled')).toBeDefined()
    expect(calibrate.attributes('title')).toBe('请先在图层中解锁底图')
    await wrapper.setProps({ underlayLocked: false })
    expect(calibrate.attributes('disabled')).toBeUndefined()
    expect(calibrate.attributes('title')).toBeUndefined()
  })

  it('exposes working underlay visibility, opacity and lock controls', async () => {
    const wrapper = mount(SpaceStudioContextPanel, {
      props: {
        hasUnderlay: true,
        calibrated: true,
        readonly: false,
        underlayVisible: true,
        underlayOpacity: 55,
        underlayLocked: true,
      },
    })

    await wrapper.findAll('.studio-modebar button')[2]!.trigger('click')
    const visible = wrapper.get('[data-test="underlay-visible"]')
    const opacity = wrapper.get('[data-test="underlay-opacity"]')
    const locked = wrapper.get('[data-test="underlay-locked"]')
    await visible.setValue(false)
    await opacity.setValue(30)
    await locked.setValue(false)

    expect(wrapper.emitted('underlayVisibilityChange')).toEqual([[false]])
    expect(wrapper.emitted('underlayOpacityChange')).toEqual([[30]])
    expect(wrapper.emitted('underlayLockChange')).toEqual([[false]])

    await wrapper.setProps({ hasUnderlay: false })
    expect(visible.attributes('disabled')).toBeDefined()
    expect(opacity.attributes('disabled')).toBeDefined()
    expect(locked.attributes('disabled')).toBeDefined()
  })
})
