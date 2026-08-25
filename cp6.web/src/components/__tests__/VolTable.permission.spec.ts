// @vitest-environment jsdom
import { defineComponent, nextTick } from 'vue'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import VolTable from '../VolTable.vue'
import { permission } from '@/directives/permission'
import { usePermissionStore } from '@/stores/permission'

vi.mock('@/api/sys/rolePerm', () => ({ rolePermApi: { myActions: vi.fn() } }))

const VolFormStub = defineComponent({
  props: { visible: Boolean },
  template: '<div v-if="visible" class="vol-form-open" />',
})

const PermissionHarness = defineComponent({
  template: '<div><button class="guarded" v-permission="\'pub-seq:delete\'">guarded</button></div>',
})

const columns = [{ prop: 'name', label: '名称' }]
const api = {
  getList: vi.fn().mockResolvedValue({ rows: [{ id: 1, name: 'SEQ-1' }], total: 1 }),
  add: vi.fn(),
  update: vi.fn(),
  del: vi.fn(),
}

function prepareStore(keys: string[], loaded = true) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = usePermissionStore()
  store.actionKeys = new Set(keys)
  store.loaded = loaded
  return { pinia, store }
}

function mountTable(keys: string[], options: { width?: number; loaded?: boolean; withPermissions?: boolean } = {}) {
  const { width = 375, loaded = true, withPermissions = true } = options
  const { pinia, store } = prepareStore(keys, loaded)
  Object.defineProperty(window, 'innerWidth', { value: width, configurable: true })
  window.dispatchEvent(new Event('resize'))

  const permissionProps = withPermissions ? {
    addPermission: 'pub-seq:add',
    editPermission: 'pub-seq:edit',
    deletePermission: 'pub-seq:delete',
  } : {}
  const wrapper = mount(VolTable, {
    props: {
      columns,
      api,
      ...permissionProps,
    },
    global: {
      plugins: [
        pinia,
        createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ElementPlus,
      ],
      directives: { permission },
      stubs: { VolForm: VolFormStub },
    },
  })
  return { wrapper, store }
}

describe('VolTable mobile permissions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getList.mockResolvedValue({ rows: [{ id: 1, name: 'SEQ-1' }], total: 1 })
  })

  it('keeps add but hides edit/delete affordances for an add-only user', async () => {
    const { wrapper } = mountTable(['pub-seq:add'])
    await flushPromises()

    expect(wrapper.find('.cp6-fab').exists()).toBe(true)
    expect(wrapper.find('.more-btn').exists()).toBe(false)

    await wrapper.get('.row-card').trigger('click')
    expect(wrapper.find('.vol-form-open').exists()).toBe(false)
  })

  it('preserves edit access for a fully authorized user', async () => {
    const { wrapper } = mountTable(['pub-seq:add', 'pub-seq:edit', 'pub-seq:delete'])
    await flushPromises()

    expect(wrapper.find('.more-btn').exists()).toBe(true)
    await wrapper.get('.row-card').trigger('click')
    expect(wrapper.find('.vol-form-open').exists()).toBe(true)
  })

  it('re-evaluates fail-open controls when async permissions finish loading', async () => {
    const { wrapper, store } = mountTable(['pub-seq:add'], { loaded: false })
    await flushPromises()
    expect(wrapper.find('.more-btn').exists()).toBe(true)

    store.loaded = true
    await nextTick()

    expect(wrapper.find('.cp6-fab').exists()).toBe(true)
    expect(wrapper.find('.more-btn').exists()).toBe(false)
    await wrapper.get('.row-card').trigger('click')
    expect(wrapper.find('.vol-form-open').exists()).toBe(false)
  })

  it('removes a guarded DOM element after async permissions finish loading', async () => {
    const { pinia, store } = prepareStore([], false)
    const wrapper = mount(PermissionHarness, {
      global: { plugins: [pinia], directives: { permission } },
    })
    expect(wrapper.find('.guarded').exists()).toBe(true)

    store.loaded = true
    await nextTick()

    expect(wrapper.find('.guarded').exists()).toBe(false)
  })

  it('applies a desktop edit-only permission matrix', async () => {
    const { wrapper } = mountTable(['pub-seq:edit'], { width: 1280 })
    await flushPromises()
    const buttons = wrapper.findAll('button').map(button => button.text())

    expect(buttons).not.toContain('table.add')
    expect(buttons).not.toContain('table.delete')
    expect(buttons).toContain('table.edit')
  })

  it('keeps legacy CRUD controls when permission props are omitted', async () => {
    const { wrapper } = mountTable([], { width: 1280, withPermissions: false })
    await flushPromises()
    const buttons = wrapper.findAll('button').map(button => button.text())

    expect(buttons).toContain('table.add')
    expect(buttons).toContain('table.edit')
    expect(buttons).toContain('table.delete')
  })

  it('formats .NET high-precision values without exposing raw or fractional precision', async () => {
    const { pinia } = prepareStore([])
    const datetimeApi = {
      ...api,
      getList: vi.fn().mockResolvedValue({
        rows: [{ id: 1, createDate: '2026-04-08T22:06:21.1795134' }],
        total: 1,
      }),
    }
    Object.defineProperty(window, 'innerWidth', { value: 1280, configurable: true })

    const wrapper = mount(VolTable, {
      props: {
        columns: [{ prop: 'createDate', label: '创建时间', type: 'datetime' }],
        api: datetimeApi,
      },
      global: {
        plugins: [
          pinia,
          createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
          ElementPlus,
        ],
        directives: { permission },
        stubs: { VolForm: VolFormStub },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('2026')
    expect(wrapper.text()).not.toContain('T22:06:21.1795134')
    expect(wrapper.text()).not.toContain('.179')
  })
})
