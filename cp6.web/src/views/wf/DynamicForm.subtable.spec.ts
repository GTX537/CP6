// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createI18n } from 'vue-i18n'
import { describe, expect, it, vi } from 'vitest'
import DynamicForm from './DynamicForm.vue'
import type { FormSchema } from '@/types/wf/wf'

vi.mock('@/api/sys/user', () => ({
  userApi: { getList: vi.fn() },
}))

const schema: FormSchema = {
  fields: [{
    name: 'items',
    label: '采购明细',
    type: 'table',
    minRows: 0,
    maxRows: 2,
    columns: [
      { name: 'material', label: '物料', type: 'input', required: true },
      { name: 'qty', label: '数量', type: 'number', required: true },
    ],
  }],
}

function mountForm(model: Record<string, unknown>, readonly = false) {
  return mount(DynamicForm, {
    props: {
      schema,
      modelValue: model,
      ...(readonly ? { mask: { items: 'readonly' as const } } : {}),
    },
    global: {
      plugins: [
        ElementPlus,
        createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
      ],
    },
  })
}

describe('DynamicForm subtable', () => {
  it('adds and removes a schema-shaped row', async () => {
    const model: Record<string, any> = { items: [] }
    const wrapper = mountForm(model)
    await flushPromises()

    await wrapper.get('[data-test="add-items"]').trigger('click')
    await flushPromises()
    expect(model.items).toEqual([{ material: '', qty: undefined }])

    await wrapper.get('[data-test="remove-items-0"]').trigger('click')
    expect(model.items).toEqual([])
  })

  it('readonly mask hides all row mutation actions', async () => {
    const wrapper = mountForm({ items: [{ material: 'A-01', qty: 2 }] }, true)
    await flushPromises()

    expect(wrapper.find('[data-test="add-items"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="remove-items-0"]').exists()).toBe(false)
  })
})
