// @vitest-environment jsdom
import { defineComponent } from 'vue'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import VolTable from '../VolTable.vue'
import { permission } from '@/directives/permission'

const { permissionState } = vi.hoisted(() => ({
  permissionState: { keys: new Set<string>() },
}))

vi.mock('@/stores/permission', () => ({
  usePermissionStore: () => ({
    loaded: true,
    has: (key: string) => permissionState.keys.has(key),
  }),
}))

const VolFormStub = defineComponent({
  props: { visible: Boolean },
  template: '<div v-if="visible" class="vol-form-open" />',
})

const columns = [{ prop: 'name', label: '名称' }]
const api = {
  getList: vi.fn().mockResolvedValue({ rows: [{ id: 1, name: 'SEQ-1' }], total: 1 }),
  add: vi.fn(),
  update: vi.fn(),
  del: vi.fn(),
}

function mountMobileTable() {
  Object.defineProperty(window, 'innerWidth', { value: 375, configurable: true })
  window.dispatchEvent(new Event('resize'))

  return mount(VolTable, {
    props: {
      columns,
      api,
      addPermission: 'pub-seq:add',
      editPermission: 'pub-seq:edit',
      deletePermission: 'pub-seq:delete',
    },
    global: {
      plugins: [
        createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ElementPlus,
      ],
      directives: { permission },
      stubs: { VolForm: VolFormStub },
    },
  })
}

describe('VolTable mobile permissions', () => {
  beforeEach(() => {
    permissionState.keys = new Set()
    vi.clearAllMocks()
    api.getList.mockResolvedValue({ rows: [{ id: 1, name: 'SEQ-1' }], total: 1 })
  })

  it('keeps add but hides edit/delete affordances for an add-only user', async () => {
    permissionState.keys = new Set(['pub-seq:add'])
    const wrapper = mountMobileTable()
    await flushPromises()

    expect(wrapper.find('.cp6-fab').exists()).toBe(true)
    expect(wrapper.find('.more-btn').exists()).toBe(false)

    await wrapper.get('.row-card').trigger('click')
    expect(wrapper.find('.vol-form-open').exists()).toBe(false)
  })

  it('preserves edit access for a fully authorized user', async () => {
    permissionState.keys = new Set(['pub-seq:add', 'pub-seq:edit', 'pub-seq:delete'])
    const wrapper = mountMobileTable()
    await flushPromises()

    expect(wrapper.find('.more-btn').exists()).toBe(true)
    await wrapper.get('.row-card').trigger('click')
    expect(wrapper.find('.vol-form-open').exists()).toBe(true)
  })
})
