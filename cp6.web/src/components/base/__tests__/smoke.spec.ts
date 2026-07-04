// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'

describe('component test infra', () => {
  it('mounts a component under jsdom', () => {
    const w = mount(defineComponent({ template: '<p>ok</p>' }))
    expect(w.text()).toBe('ok')
  })
})
