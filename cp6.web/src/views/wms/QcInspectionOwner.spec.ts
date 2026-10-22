import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), createFromInbound: vi.fn(), saveItems: vi.fn(), judge: vi.fn(), cancel: vi.fn() }))
const messages = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), confirm: vi.fn() }))
vi.mock('@/api/wms/qcInspection', () => ({ qcInspectionApi: api }))
vi.mock('element-plus', () => ({ ElMessage: messages, ElMessageBox: { confirm: messages.confirm } }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/utils/format', () => ({ formatQty: (value: unknown) => String(value ?? '') }))
import View from './QcInspectionView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let view: ReturnType<typeof shallowMount> | undefined
function render(): any {
  view = shallowMount(View, { global: { directives: { permission: {} }, config: { warnHandler: () => {} }, stubs: { ElCard: { template: '<div><slot/></div>' }, ElTable: true, ElDialog: true } } })
  return view.vm
}
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
const record = () => ({ inspectionNo: 'QC1', status: 1, items: [{ lineNo: 1, acceptedQty: 1.12345678 }] })
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); for (const mock of Object.values(api)) mock.mockReset()
  api.search.mockResolvedValue({ data: [] }); api.get.mockResolvedValue({ data: record() }); api.judge.mockResolvedValue({ data: { generatedReceiptNo: 'RC1' } }); api.saveItems.mockResolvedValue({ code: 0 }); api.cancel.mockResolvedValue({ code: 0 })
  api.createFromInbound.mockResolvedValue({ data: { inspectionNo: 'QC1' } }); messages.confirm.mockReset().mockResolvedValue(true)
})
afterEach(() => { view?.unmount(); view = undefined })
describe('inbound inspection Owner page', () => {
  it('freezes quantities and judge input, rejects duplicate submissions, and retains confirmed state on read failure', async () => {
    const vm = render(); vm.current = record(); const request = pending(); api.saveItems.mockReturnValueOnce(request.promise)
    const save = vm.onSaveItems(); await vm.onSaveItems(); vm.current.items[0].acceptedQty = 9
    expect(api.saveItems.mock.calls[0]![1][0].acceptedQty).toBe(1.12345678); expect(api.saveItems).toHaveBeenCalledTimes(1)
    request.resolve({ code: 0 }); await save; expect(vm.current.items[0].acceptedQty).toBe(9)
    vm.onJudgeClick(); vm.judgeForm.acceptWarehouseCd = 'W01'; const judge = pending(); api.judge.mockReturnValueOnce(judge.promise)
    const submitting = vm.onJudgeConfirm(); vm.judgeForm.acceptWarehouseCd = 'W02'; await vm.onJudgeConfirm()
    expect(api.judge.mock.calls[0]![1].acceptWarehouseCd).toBe('W01'); expect(api.judge).toHaveBeenCalledTimes(1)
    api.get.mockRejectedValueOnce({ response: { status: 403 } }); judge.resolve({ data: { generatedReceiptNo: 'RC1' } }); await submitting
    expect(vm.current.status).toBe(2); expect(vm.current.generatedReceiptNo).toBe('RC1'); expect(vm.lastSavedNo).toBe('QC1'); expect(vm.intent.uncertain.value).toBe(false)
  })
  it('does not cancel a different target or continue an old identity confirmation', async () => {
    const vm = render(); vm.current = record(); const confirm = pending(); messages.confirm.mockReturnValueOnce(confirm.promise)
    const cancel = vm.onCancel(); vm.current = { ...record(), inspectionNo: 'QC2' }; confirm.resolve(true); await cancel; expect(api.cancel).not.toHaveBeenCalled()
    const old = pending(); messages.confirm.mockReturnValueOnce(old.promise); const pendingCancel = vm.onCancel(); identity(); old.resolve(true); await pendingCancel; expect(api.cancel).not.toHaveBeenCalled()
  })
  it('blocks repeat creation after UNKNOWN and keeps the confirmed number when the detail cannot be read', async () => {
    const vm = render(); vm.bridgeInboundNo = 'IN1'; api.get.mockRejectedValueOnce({ response: { status: 403 } }); await vm.onBridge()
    expect(vm.lastSavedNo).toBe('QC1'); expect(vm.bridgeInboundNo).toBe(''); expect(vm.readError).toBeTruthy()
    vm.bridgeInboundNo = 'IN2'; api.createFromInbound.mockRejectedValueOnce({ response: { status: 503 } }); await vm.onBridge(); await vm.onBridge()
    expect(api.createFromInbound).toHaveBeenCalledTimes(2); expect(vm.intent.uncertain.value).toBe(true); expect(vm.lastSavedNo).toBe('QC1')
  })
  it('suppresses late detail after returning to the list or changing identity', async () => {
    const vm = render(); const request = pending(); api.get.mockReturnValueOnce(request.promise); const load = vm.openDetail('OLD'); vm.backToList()
    request.resolve({ data: record() }); await load; expect(vm.current).toBeNull(); expect(vm.mode).toBe('list')
    const old = pending(); api.get.mockReturnValueOnce(old.promise); const read = vm.openDetail('PRIVATE'); identity(); old.resolve({ data: record() }); await read; expect(vm.current).toBeNull()
    await flushPromises()
  })
})
