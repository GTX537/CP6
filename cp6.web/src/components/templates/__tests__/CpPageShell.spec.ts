// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CpPageShell from '../CpPageShell.vue'

describe('CpPageShell', () => {
  it('renders the title', () => {
    const w = mount(CpPageShell, { props: { title: '出庫指示一覧' } })
    expect(w.get('.cp-page-head h1').text()).toContain('出庫指示一覧')
  })
  it('renders the count pill only when count is provided (including 0)', () => {
    const without = mount(CpPageShell, { props: { title: '出庫指示一覧' } })
    expect(without.find('.cp-page-head .cnt').exists()).toBe(false)
    const zero = mount(CpPageShell, { props: { title: '出庫指示一覧', count: 0 } })
    expect(zero.get('.cp-page-head .cnt').text()).toBe('0')
    const some = mount(CpPageShell, { props: { title: '出庫指示一覧', count: 28 } })
    expect(some.get('.cp-page-head .cnt').text()).toBe('28')
  })
  it('exposes actions slot and default content slot outlets', () => {
    const w = mount(CpPageShell, {
      props: { title: '出庫指示一覧' },
      slots: {
        actions: '<button class="act">新規</button>',
        default: '<div class="body">内容</div>'
      }
    })
    expect(w.get('.cp-page-actions .act').text()).toBe('新規')
    expect(w.get('.cp-page .body').text()).toBe('内容')
  })
})
