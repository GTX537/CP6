import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, shallowMount } from '@vue/test-utils'
import type { GrCreateForm } from '@/types/pur/pur'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import GoodsReceiptView from './GoodsReceiptView.vue'

const { gr, po, messages } = vi.hoisted(() => ({
  gr: { list: vi.fn(), confirm: vi.fn(), confirmationScope: vi.fn(), originalConfirmation: vi.fn() },
  po: { get: vi.fn() },
  messages: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
}))
vi.mock('@/api/pur/pur', () => ({ grApi: gr, poApi: po }))
vi.mock('element-plus', () => ({ ElMessage: messages }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (value: string) => value }) }))

function open() {
  return shallowMount(GoodsReceiptView, {
    global: {
      directives: { permission: {}, loading: {} },
      config: { warnHandler: () => {} },
      stubs: { ElTable: true, ElTableColumn: true },
    },
  })
}

type View = {
  pending: undefined | { operationId: string; request: GrCreateForm }
  form: GrCreateForm
  openCreate: () => void
  loadPoLines: () => Promise<void>
  submit: () => Promise<void>
  recover: (retry: boolean) => Promise<void>
}
const ok = <T>(data: T) => ({ code: 0, data })
const scope = { tenantId: 'tenant-a', actorId: 'buyer-a' }
const stored = 'cp6_gr_confirmation_v1:tenant-a:buyer-a'
const received = { grNo: 'GR1' }
let mounted: ReturnType<typeof open> | undefined
function mount() {
  mounted = open()
  return mounted.vm as unknown as View
}
async function prepare(view: View) {
  view.openCreate()
  view.form.poNo = 'PO1'
  view.form.warehouseCd = 'W01'
  await view.loadPoLines()
}

beforeEach(() => {
  vi.resetAllMocks()
  localStorage.clear()
  sessionStorage.clear()
  localStorage.setItem(AUTH_EPOCH, 'buyer-a')
  gr.list.mockResolvedValue(ok([]))
  gr.confirmationScope.mockResolvedValue(ok(scope))
  po.get.mockResolvedValue(ok({ lines: [{ lineNo: 7, itemId: 'PART', qty: 3.12345678 }] }))
})
afterEach(() => { mounted?.unmount(); mounted = undefined })

describe('purchase receipt recovery', () => {
  it('keeps a lost reply across reload and can read the committed original without posting again', async () => {
    let view = mount()
    await prepare(view)
    gr.confirm.mockRejectedValueOnce({ response: { status: 503 } })
    await view.submit()
    const key = view.pending!.operationId
    expect(gr.confirm).toHaveBeenCalledTimes(1)
    expect(JSON.parse(sessionStorage.getItem(stored)!).operationId).toBe(key)

    mounted?.unmount()
    view = mount()
    await flushPromises()
    gr.originalConfirmation.mockResolvedValueOnce(ok({ operationId: key, state: 'COMMITTED', receipt: received }))
    await view.recover(false)
    expect(gr.originalConfirmation).toHaveBeenCalledWith(key)
    expect(gr.confirm).toHaveBeenCalledTimes(1)
    expect(sessionStorage.getItem(stored)).toBeNull()
    expect(messages.success).toHaveBeenCalledTimes(1)
  })

  it('preserves UNKNOWN and retries the original input and key following a later denial', async () => {
    const view = mount()
    await prepare(view)
    gr.confirm.mockRejectedValueOnce(new Error('disconnected'))
    await view.submit()
    const [request, key] = gr.confirm.mock.calls[0]!
    gr.originalConfirmation.mockResolvedValueOnce(ok({ operationId: key, state: 'UNKNOWN', receipt: null }))
    await view.recover(false)
    expect(view.pending?.operationId).toBe(key)
    view.form.remarks = 'edited after submitting'
    gr.confirm.mockRejectedValueOnce({ response: { status: 403 } }).mockResolvedValueOnce(ok(received))
    await view.recover(true)
    expect(sessionStorage.getItem(stored)).not.toBeNull()
    await view.recover(true)
    expect(gr.confirm.mock.calls[1]).toEqual([request, key])
    expect(gr.confirm.mock.calls[2]).toEqual([request, key])
    expect(view.pending).toBeUndefined()
  })

  it('does not use an earlier identity response or expose its pending request', async () => {
    const view = mount()
    await prepare(view)
    let finish!: (result: unknown) => void
    gr.confirm.mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    const first = view.submit()
    await flushPromises()
    await view.submit()
    expect(gr.confirm).toHaveBeenCalledTimes(1)

    gr.confirmationScope.mockResolvedValue(ok({ tenantId: 'tenant-b', actorId: 'buyer-b' }))
    localStorage.setItem(AUTH_EPOCH, 'buyer-b')
    window.dispatchEvent(new Event(AUTH_EVENT))
    finish(ok(received))
    await first
    await flushPromises()
    expect(view.pending).toBeUndefined()
    expect(view.form.poNo).toBe('')
    expect(sessionStorage.getItem(stored)).not.toBeNull()
    expect(messages.success).not.toHaveBeenCalled()
  })
})
