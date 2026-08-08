// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createI18n } from 'vue-i18n'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import FormInitiate from './FormInitiate.vue'

const mocks = vi.hoisted(() => ({
  formSubmit: vi.fn(),
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
  formApi: {
    getDef: vi.fn().mockResolvedValue({ data: { schemaJson: '{"fields":[]}' } }),
    submit: mocks.formSubmit,
  },
}))

vi.mock('@/api/oa/draft', () => ({
  draftApi: { create: mocks.draftSave },
}))

vi.mock('@/api/oa/forecast', () => ({
  forecastApi: { preview: vi.fn() },
}))

describe('FormInitiate submit permissions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.formSubmit.mockResolvedValue({ data: { instanceId: 'instance-1' } })
    mocks.draftSave.mockResolvedValue({ data: { id: 'draft-1' } })
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

    expect(mocks.formSubmit).toHaveBeenCalledTimes(1)
    const call = mocks.formSubmit.mock.calls[0]!
    expect(call[0]).toBe('expense-form')
    expect(call[1]).toEqual({})
    expect(call[2]).toEqual(expect.any(String))
    expect(mocks.draftSave).not.toHaveBeenCalled()
    expect(mocks.push).toHaveBeenCalledWith('/oa/inbox')
  })

  it('saves a standalone SFS draft by formKey without a flowKey', async () => {
    const wrapper = mount(FormInitiate, {
      global: {
        plugins: [
          ElementPlus,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ],
        directives: { permission: {} },
        stubs: { DynamicForm: true, FlowTimeline: true, CpEmpty: true },
      },
    })
    await flushPromises()

    const save = wrapper.findAll('button').find(button => button.text().includes('oa.initiate.saveDraft'))
    await save!.trigger('click')
    await flushPromises()

    expect(mocks.draftSave).toHaveBeenCalledWith('expense-form', {})
  })
})
