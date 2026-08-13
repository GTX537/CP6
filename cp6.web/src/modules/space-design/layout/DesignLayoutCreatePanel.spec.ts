// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import DesignLayoutCreatePanel from './DesignLayoutCreatePanel.vue'
import type { LayoutCreateIntent } from './layoutCreate'

const zones = [{
  logicalId: '11111111-1111-1111-1111-111111111111',
  code: 'Z-A',
  name: '存储区 A',
}]
const aisles = [{
  logicalId: '22222222-2222-2222-2222-222222222222',
  code: 'A-01',
  zoneLogicalId: zones[0]!.logicalId,
}]

function mountPanel(props: Record<string, unknown> = {}) {
  return mount(DesignLayoutCreatePanel, {
    props: {
      zones,
      aisles,
      readonly: false,
      busy: false,
      pointer: { x: 1_250.4, y: 2_499.6 },
      ...props,
    },
  })
}

describe('DesignLayoutCreatePanel', () => {
  it('emits a typed zone intent with deterministic polygon geometry', async () => {
    const wrapper = mountPanel()
    await wrapper.get('[data-test="zone-code"]').setValue(' Z-NEW ')
    await wrapper.get('button.pointer').trigger('click')
    await wrapper.get('[data-test="submit-layout"]').trigger('submit')

    const intent = wrapper.emitted('create')?.[0]?.[0] as LayoutCreateIntent
    expect(intent).toMatchObject({
      type: 'CreateZone',
      payload: { zoneCode: 'Z-NEW', zoneType: 1 },
    })
    expect(intent.type).toBe('CreateZone')
    if (intent.type !== 'CreateZone') throw new Error('Expected a zone intent')
    expect(JSON.parse(intent.payload.polygonJson).points[0]).toEqual([1_250, 2_500])
  })

  it('requires an existing zone before aisle or rack creation', async () => {
    const wrapper = mountPanel({ zones: [], aisles: [] })
    await wrapper.findAll('[role="tab"]')[1]!.trigger('click')
    expect(wrapper.text()).toContain('请先创建库区')
    expect(wrapper.get('[data-test="submit-layout"]').attributes('disabled')).toBeDefined()

    await wrapper.findAll('[role="tab"]')[2]!.trigger('click')
    expect(wrapper.get('[data-test="submit-layout"]').attributes('disabled')).toBeDefined()
  })

  it('emits rack per-level specifications and previews generated locations', async () => {
    const wrapper = mountPanel()
    await wrapper.findAll('[role="tab"]')[2]!.trigger('click')
    await wrapper.get('[data-test="rack-code"]').setValue('R-001')

    expect(wrapper.text()).toContain('将生成 4 个库位')
    await wrapper.get('[data-test="submit-layout"]').trigger('submit')

    const intent = wrapper.emitted('create')?.[0]?.[0] as LayoutCreateIntent
    expect(intent).toMatchObject({
      type: 'CreateRack',
      payload: {
        zoneLogicalId: zones[0]!.logicalId,
        rackCode: 'R-001',
        width: 2_400,
        depth: 1_000,
        height: 4_000,
      },
    })
    expect(intent.type).toBe('CreateRack')
    if (intent.type !== 'CreateRack') throw new Error('Expected a rack intent')
    expect(intent.payload.levels).toHaveLength(2)
    expect(intent.payload.levels[1]).toMatchObject({
      levelNo: 2,
      bottomZ: 1_700,
      binCount: 2,
      depthCount: 1,
    })
  })

  it('keeps all creates disabled while readonly or saving', async () => {
    const readonlyWrapper = mountPanel({ readonly: true })
    await readonlyWrapper.get('[data-test="zone-code"]').setValue('Z-1')
    expect(readonlyWrapper.get('[data-test="submit-layout"]').attributes('disabled')).toBeDefined()

    const busyWrapper = mountPanel({ busy: true })
    await busyWrapper.get('[data-test="zone-code"]').setValue('Z-1')
    expect(busyWrapper.get('[data-test="submit-layout"]').attributes('disabled')).toBeDefined()
  })

  it('blocks rack specifications that cannot fit the authoritative envelope', async () => {
    const wrapper = mountPanel()
    await wrapper.findAll('[role="tab"]')[2]!.trigger('click')
    await wrapper.get('[data-test="rack-code"]').setValue('R-INVALID')
    const width = wrapper.findAll('input[type="number"]')
      .find((input) => input.element.parentElement?.textContent?.includes('宽 mm'))
    await width!.setValue('1000')

    expect(wrapper.text()).toContain('列宽超过货架宽度')
    expect(wrapper.get('[data-test="submit-layout"]').attributes('disabled')).toBeDefined()
  })
})
