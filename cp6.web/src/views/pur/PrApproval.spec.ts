// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createI18n } from 'vue-i18n'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PrView from './PrView.vue'

const mocks = vi.hoisted(() => ({
  list: vi.fn(),
  get: vi.fn(),
  create: vi.fn(),
  submit: vi.fn(),
  convert: vi.fn(),
  replace: vi.fn(),
  query: { prNo: 'PR-DEEP-1' } as Record<string, string>,
}))

vi.mock('@/api/pur/pur', () => ({ prApi: mocks }))
vi.mock('vue-router', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ query: mocks.query }),
    useRouter: () => ({ replace: mocks.replace }),
  }
})

describe('PUR_PR page approval integration', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.query.prNo = 'PR-DEEP-1'
    const pr = {
      prNo: 'PR-DEEP-1', requesterId: 'alice', requestDate: '2026-07-23',
      status: 0, source: 'manual', lines: [],
    }
    mocks.list.mockResolvedValue({ data: [pr] })
    mocks.get.mockResolvedValue({ data: pr })
    mocks.submit.mockResolvedValue({ data: { ...pr, status: 1 } })
  })

  it('P0_AC_P01 submits only the business identity prNo', async () => {
    const wrapper = mount(PrView, {
      global: {
        plugins: [
          ElementPlus,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ],
        directives: { permission: {} },
        stubs: {
          ApprovalPanel: {
            props: ['bizId', 'submitHandler'],
            template: '<button data-test="panel-submit" @click="submitHandler()">submit</button>',
          },
        },
      },
    })
    await flushPromises()

    await wrapper.find('[data-test="panel-submit"]').trigger('click')
    await flushPromises()
    expect(mocks.submit).toHaveBeenCalledWith('PR-DEEP-1')
    expect(mocks.submit.mock.calls[0]).toHaveLength(1)
  })

  it('P0_AC_P07 restores the exact PR from the deep-link query on refresh', async () => {
    mount(PrView, {
      global: {
        plugins: [
          ElementPlus,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ],
        directives: { permission: {} },
        stubs: { ApprovalPanel: true },
      },
    })
    await flushPromises()
    expect(mocks.get).toHaveBeenCalledWith('PR-DEEP-1')
  })
})
