// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createI18n } from 'vue-i18n'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import FormInitiate from './FormInitiate.vue'

const mocks = vi.hoisted(() => ({
  flowSubmit: vi.fn(),
  draftSave: vi.fn(),
  push: vi.fn(),
}))

vi.mock('vue-router', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ query: { formKey: 'expense-form' } }),
    useRouter: () => ({ push: mocks.push }),
  }
})

vi.mock('@/api/oa/flowAdmin', () => ({
  flowAdminApi: {
    list: vi.fn().mockResolvedValue({
      data: [{ formKey: 'expense-form', flowKey: 'expense-approve', enable: true }],
    }),
  },
}))

vi.mock('@/api/wf/form', () => ({
  formApi: { getDef: vi.fn().mockResolvedValue({ data: { schemaJson: '{"fields":[]}' } }) },
}))

vi.mock('@/api/wf/flow', () => ({
  flowApi: { submit: mocks.flowSubmit },
}))

vi.mock('@/api/oa/draft', () => ({
  draftApi: { save: mocks.draftSave },
}))

vi.mock('@/api/oa/forecast', () => ({
  forecastApi: { preview: vi.fn() },
}))

describe('FormInitiate submit permissions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.flowSubmit.mockResolvedValue({ data: { instanceId: 'instance-1' } })
  })

  it('submits directly with the submit permission instead of requiring draft add', async () => {
    const wrapper = mount(FormInitiate, {
      global: {
        plugins: [
          ElementPlus,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ],
        directives: { permission: {} },
        stubs: {
          DynamicForm: true,
          FlowTimeline: true,
          CpEmpty: true,
        },
      },
    })
    await flushPromises()

    const submit = wrapper.findAll('button').find(button => button.text().includes('oa.initiate.submit'))
    expect(submit).toBeDefined()
    await submit!.trigger('click')
    await flushPromises()

    expect(mocks.flowSubmit).toHaveBeenCalledWith({
      flowKey: 'expense-approve',
      varsJson: '{}',
    })
    expect(mocks.draftSave).not.toHaveBeenCalled()
    expect(mocks.push).toHaveBeenCalledWith('/oa/inbox')
  })
})
