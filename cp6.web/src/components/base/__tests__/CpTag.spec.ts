// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CpTag, { STATUS_TONE } from '../CpTag.vue'

describe('CpTag', () => {
  it('maps known status to tone class', () => {
    const w = mount(CpTag, { props: { status: '已出库' }, slots: { default: '已出库' } })
    expect(w.classes()).toContain('t-ok')
  })
  it('falls back to muted for unknown status', () => {
    const w = mount(CpTag, { props: { status: '莫名状态' } })
    expect(w.classes()).toContain('t-muted')
  })
  it('explicit tone overrides status', () => {
    const w = mount(CpTag, { props: { status: '已出库', tone: 'danger' } })
    expect(w.classes()).toContain('t-danger')
  })
  it('exports STATUS_TONE map', () => { expect(STATUS_TONE['拣货中']).toBe('info') })
})
