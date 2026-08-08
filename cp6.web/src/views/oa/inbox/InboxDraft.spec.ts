// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createI18n } from 'vue-i18n'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import InboxDraft from './InboxDraft.vue'

const mocks = vi.hoisted(() => ({
  list: vi.fn(),
  get: vi.fn(),
  update: vi.fn(),
  rebase: vi.fn(),
  submit: vi.fn(),
  remove: vi.fn(),
}))

vi.mock('@/api/oa/draft', () => ({ draftApi: mocks }))

describe('InboxDraft pinned lifecycle', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.list.mockResolvedValue({
      data: {
        items: [{
          id: 'draft-1', formKey: 'leave', formName: 'Leave', formVersion: 1,
          latestPublishedVersion: 2, dataJson: '{"reason":"annual"}',
          title: 'July', updatedAtUtc: '2026-07-23T12:00:00Z', stale: true, rowVersion: 'AQ==',
        }],
        total: 1,
      },
    })
    mocks.get.mockResolvedValue({
      data: {
        id: 'draft-1', formKey: 'leave', formName: 'Leave', formVersion: 1,
        latestPublishedVersion: 2,
        title: 'July', updatedAtUtc: '2026-07-23T12:00:00Z', stale: true,
        rowVersion: 'AQ==', formDefVersionId: 'v1',
        schemaJson: '{"fields":[{"name":"reason","type":"input"}]}',
        dataJson: '{"reason":"annual"}',
      },
    })
  })

  it('reopens with pinned schema/data in DynamicForm and disables stale direct submit', async () => {
    const wrapper = mount(InboxDraft, {
      global: {
        plugins: [
          ElementPlus,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ],
        directives: { permission: {} },
        stubs: {
          DynamicForm: { props: ['schema', 'modelValue'], template: '<div data-test="dynamic-form" />' },
          CpEmpty: true,
        },
      },
    })
    await flushPromises()

    const edit = wrapper.findAll('button').find(x => x.text().includes('oa.draft.edit'))
    await edit!.trigger('click')
    await flushPromises()

    expect(mocks.get).toHaveBeenCalledWith('draft-1')
    expect(wrapper.find('[data-test="dynamic-form"]').exists()).toBe(true)
    expect(wrapper.find('textarea').exists()).toBe(false)
    const submit = wrapper.findAll('button').find(x => x.text().includes('oa.draft.submit'))
    expect(submit?.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('表单已有新版本，请升级后提交')
  })
})
