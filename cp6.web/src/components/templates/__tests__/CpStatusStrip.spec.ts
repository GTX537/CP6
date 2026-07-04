// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CpStatusStrip from '../CpStatusStrip.vue'

const items = [
  { key: 'all', label: '全部', count: 28 },
  { key: 'wait', label: '未出库', count: 9, tone: 'warn' },
  { key: 'done', label: '已出库', count: 12, tone: 'ok' }
]

describe('CpStatusStrip', () => {
  it('renders one card per item', () => {
    const w = mount(CpStatusStrip, { props: { items, modelValue: 'all' } })
    expect(w.findAll('.ss')).toHaveLength(3)
  })
  it('renders label and count for each card', () => {
    const w = mount(CpStatusStrip, { props: { items, modelValue: 'all' } })
    const first = w.findAll('.ss')[1]
    expect(first.text()).toContain('未出库')
    expect(first.text()).toContain('9')
  })
  it('emits update:modelValue with the clicked item key', async () => {
    const w = mount(CpStatusStrip, { props: { items, modelValue: 'all' } })
    await w.findAll('.ss')[2].trigger('click')
    expect(w.emitted('update:modelValue')).toEqual([['done']])
  })
  it('gives the active card (key === modelValue) the on class', () => {
    const w = mount(CpStatusStrip, { props: { items, modelValue: 'wait' } })
    const cards = w.findAll('.ss')
    expect(cards[0].classes()).not.toContain('on')
    expect(cards[1].classes()).toContain('on')
    expect(cards[2].classes()).not.toContain('on')
  })
  it('renders a card whose count is 0', () => {
    const w = mount(CpStatusStrip, {
      props: { items: [{ key: 'x', label: '空', count: 0 }], modelValue: 'x' }
    })
    expect(w.get('.ss').text()).toContain('0')
  })
})
