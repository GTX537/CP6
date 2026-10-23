// @vitest-environment jsdom
import { nextTick } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElMessage, ElMessageBox } from 'element-plus'
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
  afterEach(() => vi.restoreAllMocks())

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

  it('confirms reference removal and only reports success after the explicit unlink acknowledgement', async () => {
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm')
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close: vi.fn() }))
    vi.mocked(attachmentApi.remove).mockResolvedValue({ code: 0, data: { state: 'UNLINKED', physicalDeletion: 'NOT_REQUESTED' } } as any)
    vi.mocked(attachmentApi.list).mockResolvedValueOnce([fileRow]).mockResolvedValueOnce([])
    const { wrapper } = mountUpload(['erp-order:add'])
    await flushPromises()
    await wrapper.find('.attachment-delete').trigger('click')
    await flushPromises()
    expect(confirm).toHaveBeenCalledWith('确定移除该附件引用？文件内容不会被永久删除。', '提示', { type: 'warning' })
    expect(attachmentApi.remove).toHaveBeenCalledWith(fileRow.id)
    expect(success).toHaveBeenCalledWith('已移除附件引用')
    expect(wrapper.find('.attachment-delete').exists()).toBe(false)
    wrapper.unmount()
  })

  it('keeps the visible reference and avoids success when an old server omits the unlink acknowledgement', async () => {
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm')
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close: vi.fn() }))
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => ({ close: vi.fn() }))
    vi.mocked(attachmentApi.remove).mockResolvedValue({ code: 0 } as any)
    const { wrapper } = mountUpload(['erp-order:add'])
    await flushPromises()
    await wrapper.find('.attachment-delete').trigger('click')
    await flushPromises()
    expect(success).not.toHaveBeenCalled()
    expect(error).toHaveBeenCalledWith('附件移除结果未确认，请刷新核对')
    expect(wrapper.find('.attachment-delete').exists()).toBe(true)
    expect(attachmentApi.list).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })
})
