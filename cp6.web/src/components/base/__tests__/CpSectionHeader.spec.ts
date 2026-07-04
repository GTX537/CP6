// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CpSectionHeader from '../CpSectionHeader.vue'

describe('CpSectionHeader', () => {
  it('renders the title', () => {
    const w = mount(CpSectionHeader, { props: { title: '最近受注' } })
    expect(w.get('.cp-sec-head__title').text()).toContain('最近受注')
  })
  it('exposes an extra slot outlet on the right', () => {
    const w = mount(CpSectionHeader, {
      props: { title: '最近受注' },
      slots: { extra: '<a class="more">查看全部 →</a>' }
    })
    expect(w.get('.cp-sec-head__extra .more').text()).toBe('查看全部 →')
  })
})
