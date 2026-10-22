import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { shallowMount } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), history: vi.fn(), setQcStatus: vi.fn() }))
vi.mock('@/api/wms/stock', () => ({ stockApi: api }))
vi.mock('element-plus', () => ({ ElMessage: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/utils/format', () => ({ formatQty: (value: unknown) => String(value ?? '') }))
import View from './StockQueryView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let view: ReturnType<typeof shallowMount> | undefined
function render(): any {
  view = shallowMount(View, { global: { directives: { permission: {} }, config: { warnHandler: () => {} } } }); return view.vm
}
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
beforeEach(() => { localStorage.clear(); vi.clearAllMocks(); api.setQcStatus.mockReset() })
afterEach(() => { view?.unmount(); view = undefined })
it('freezes the selected QC stock and blocks retransmission after UNKNOWN', async () => {
  const vm = render(); vm.openQcDialog({ id: 'S1', qcStatus: 'PENDING' }); vm.qcNewStatus = 'HOLD'
  api.setQcStatus.mockRejectedValueOnce({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } })
  await vm.onQcSave(); await vm.onQcSave(); vm.openQcDialog({ id: 'S2', qcStatus: 'PENDING' })
  expect(api.setQcStatus).toHaveBeenCalledExactlyOnceWith('S1', 'HOLD', undefined); expect(vm.qcTarget.id).toBe('S1'); expect(vm.intent.uncertain.value).toBe(true)
})
it('deduplicates pending writes and clears old-identity busy state without applying an old response', async () => {
  const vm = render(); vm.openQcDialog({ id: 'S1', qcStatus: 'PENDING' }); vm.qcNewStatus = 'HOLD'
  const request = pending(); api.setQcStatus.mockReturnValueOnce(request.promise); const save = vm.onQcSave(); await vm.onQcSave()
  expect(api.setQcStatus).toHaveBeenCalledTimes(1)
  localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT))
  expect(vm.qcSaving).toBe(false); request.resolve({ code: 0, data: { qcStatus: 'HOLD' } }); await save
  expect(vm.qcTarget).toBeNull(); expect(vm.qcDialogVisible).toBe(false)
})
