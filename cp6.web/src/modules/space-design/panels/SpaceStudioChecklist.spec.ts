// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import SpaceStudioChecklist from './SpaceStudioChecklist.vue'

describe('SpaceStudioChecklist', () => {
  it('opens on first render and exposes all four task states without relying on color', () => {
    const wrapper = mount(SpaceStudioChecklist, {
      props: {
        imported: true,
        reviewed: false,
        coded: true,
        publishReady: false,
      },
    })

    const checklist = wrapper.get('[data-test="space-studio-checklist"]')
    expect(checklist.attributes('open')).toBeDefined()
    expect(checklist.attributes('aria-label')).toBe('首次建模四步任务清单')
    expect(checklist.get('summary').text()).toBe('首次建模任务 · 4 步')
    expect(checklist.findAll('li').map(item => item.attributes('aria-label'))).toEqual([
      '导入来源 · 已完成',
      '复核识别 · 待完成',
      '补齐编码 · 已完成',
      '校验发布 · 待完成',
    ])
    expect(checklist.findAll('li.done')).toHaveLength(2)
  })
})
