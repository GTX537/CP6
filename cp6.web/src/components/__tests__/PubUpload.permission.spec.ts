// @vitest-environment jsdom
import { nextTick } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import PubUpload from '../PubUpload.vue'
import { permission } from '@/directives/permission'
import { usePermissionStore } from '@/stores/permission'
import { attachmentApi } from '@/api/pub/attachment'

vi.mock('@/api/pub/attachment', () => ({
  attachmentApi: {
    list: vi.fn(),
    upload: vi.fn(),
    remove: vi.fn(),
    download: vi.fn(),
    previewObjectUrl: vi.fn(),
  },
}))
vi.mock('@/api/sys/rolePerm', () => ({ rolePermApi: { myActions: vi.fn() } }))

const fileRow = {
  id: 'attachment-1',
  fileName: 'evidence.txt',
  size: 12,
  uploader: 'alice',
}

function mountUpload(keys: string[], loaded = true) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = usePermissionStore()
  store.actionKeys = new Set(keys)
  store.loaded = loaded

  const wrapper = mount(PubUpload, {
    props: {
      bizType: 'erp-order',
      bizId: 'order-1',
      writePermission: 'erp-order:add',
    },
    global: {
      plugins: [
        pinia,
        createI18n({ legacy: false, locale: 'zh-CN', missingWarn: false, fallbackWarn: false }),
        ElementPlus,
      ],
      directives: { permission },
    },
  })

  return { wrapper, store }
}

describe('PubUpload host permission UX', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(attachmentApi.list).mockResolvedValue([fileRow])
  })

  it('keeps read actions but hides upload and delete without the host write action', async () => {
    const { wrapper } = mountUpload([])
    await flushPromises()

    expect(wrapper.find('.attachment-write').exists()).toBe(false)
    expect(wrapper.find('.attachment-delete').exists()).toBe(false)
    expect(wrapper.find('.attachment-download').exists()).toBe(true)
    expect(wrapper.find('.attachment-preview').exists()).toBe(true)
  })

  it('shows upload and delete with the host write action', async () => {
    const { wrapper } = mountUpload(['erp-order:add'])
    await flushPromises()

    expect(wrapper.find('.attachment-write').exists()).toBe(true)
    expect(wrapper.find('.attachment-delete').exists()).toBe(true)
  })

  it('re-evaluates write controls after asynchronous permissions load', async () => {
    const { wrapper, store } = mountUpload([], false)
    await flushPromises()
    expect(wrapper.find('.attachment-write').exists()).toBe(true)

    store.loaded = true
    await nextTick()

    expect(wrapper.find('.attachment-write').exists()).toBe(false)
    expect(wrapper.find('.attachment-delete').exists()).toBe(false)
  })
})
