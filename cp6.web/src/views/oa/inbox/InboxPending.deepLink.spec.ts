// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createI18n } from 'vue-i18n'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import InboxPending from './InboxPending.vue'

const mocks = vi.hoisted(() => ({
  pending: vi.fn(),
  pendingCc: vi.fn(),
  markTaskRead: vi.fn(),
  push: vi.fn(),
}))

vi.mock('@/api/oa/inbox', () => ({
  inboxApi: {
    ...mocks,
    batch: vi.fn(),
  },
}))
vi.mock('@/api/oa/pref', () => ({
  prefApi: { get: vi.fn().mockResolvedValue({ data: {} }), saveMerge: vi.fn() },
}))
vi.mock('vue-router', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRouter: () => ({ push: mocks.push }) }
})

describe('Inbox business deep link', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.pending.mockResolvedValue({
      data: [{
        taskId: 'task-1', instanceId: 'instance-1', flowKey: 'pr', flowName: 'PR',
        nodeId: 'approve', starterId: 'starter', starterName: 'Alice',
        isRead: false, sentAt: '2026-07-23T12:00:00Z',
        detailRoute: '/pur/pr?prNo=PR-DEEP-1',
      }],
    })
    mocks.markTaskRead.mockResolvedValue({})
  })

  it('P0_AC_P07 navigates to the server-rendered internal PR route', async () => {
    const wrapper = mount(InboxPending, {
      global: {
        plugins: [
          ElementPlus,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ],
        directives: { permission: {} },
        stubs: { CpTag: true, CpEmpty: true },
      },
    })
    await flushPromises()
    await wrapper.find('.el-table__row').trigger('click')
    await flushPromises()
    expect(mocks.push).toHaveBeenCalledWith('/pur/pr?prNo=PR-DEEP-1')
  })
})
