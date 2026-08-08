// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createI18n } from 'vue-i18n'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ApprovalPanel from './ApprovalPanel.vue'

const mocks = vi.hoisted(() => ({
  detail: vi.fn(),
  decide: vi.fn(),
}))

vi.mock('@/api/oa/approval', () => ({
  approvalApi: mocks,
}))

describe('ApprovalPanel P0 contract', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.detail.mockResolvedValue({
      data: {
        bizType: 'PUR_PR',
        bizId: 'PR1',
        businessStatus: 'Submitted',
        approvalStatus: 'running',
        instanceId: 'instance-1',
        myTask: { taskId: 'task-1', nodeId: 'approve', actions: ['approve', 'reject'] },
        timeline: [{
          stepSeq: 1, nodeId: 'approve', nodeName: 'Manager',
          expectedHandlerName: 'Alice', status: 0, sentAt: '2026-07-23T12:00:00Z',
        }],
        canSubmit: false,
      },
    })
    mocks.decide.mockResolvedValue({ data: {} })
  })

  it('P0_AC_P08 renders authorized projected actions and decides by server task id', async () => {
    const wrapper = mount(ApprovalPanel, {
      props: { bizType: 'PUR_PR', bizId: 'PR1' },
      global: {
        plugins: [
          ElementPlus,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ],
      },
    })
    await flushPromises()

    expect(mocks.detail).toHaveBeenCalledWith('PUR_PR', 'PR1')
    expect(wrapper.text()).toContain('Manager')
    const approve = wrapper.findAll('button').find(x => x.text().includes('通过'))
    await approve!.trigger('click')
    await flushPromises()

    expect(mocks.decide).toHaveBeenCalledWith('task-1', 'approve', undefined)
  })

  it('prevents repeated decisions while the first request is pending', async () => {
    let resolve!: (value: unknown) => void
    mocks.decide.mockReturnValue(new Promise(r => { resolve = r }))
    const wrapper = mount(ApprovalPanel, {
      props: { bizType: 'PUR_PR', bizId: 'PR1' },
      global: {
        plugins: [
          ElementPlus,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ],
      },
    })
    await flushPromises()
    const approve = wrapper.findAll('button').find(x => x.text().includes('通过'))!
    await approve.trigger('click')
    await approve.trigger('click')
    expect(mocks.decide).toHaveBeenCalledTimes(1)
    resolve({ data: {} })
    await flushPromises()
  })
})
