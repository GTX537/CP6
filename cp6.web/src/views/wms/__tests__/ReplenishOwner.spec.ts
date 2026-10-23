import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), capabilities: vi.fn(), create: vi.fn(), update: vi.fn(), generateBatch: vi.fn(), execute: vi.fn(), cancel: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), confirm: vi.fn() }))
const permission = vi.hoisted(() => ({ loaded: true, has: vi.fn(() => true) }))
vi.mock('@/api/wms/logistics', () => ({ replenishApi: api }))
vi.mock('element-plus', () => ({ ElMessage: ui, ElMessageBox: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permission }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from '../ReplenishView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
const row = { replenishNo: 'RPL-1', productCd: 'P1', warehouseCd: 'W1', fromLocationCd: 'FROM', toLocationCd: 'TO', lotNo: 'L1', qty: 12, status: 0, priority: 2, triggerType: 'MANUAL' }
const query = { page: 1, size: 50, filters: { status: 0 } }
function render(): any { wrapper = shallowMount(View, { global: { directives: { permission: {}, loading: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return wrapper.vm }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permission.loaded = true; permission.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.capabilities.mockResolvedValue({ code: 0, data: { writesAvailable: false, status: 'REPLENISH_OWNER_ADOPTION_UNPROVEN' } })
  api.search.mockResolvedValue({ code: 0, data: [structuredClone(row)] }); api.get.mockResolvedValue({ code: 0, data: structuredClone(row) })
  ui.confirm.mockResolvedValue(undefined)
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('Replenishment current Owner page', () => {
  it('keeps unadopted commands disabled including direct form submissions', async () => {
    const vm = render(); await vm.fetchList(query); vm.openCreate(); expect(vm.dialogVisible).toBe(false)
    vm.openEdit(row); expect(vm.dialogVisible).toBe(false); vm.openBatch(); expect(vm.batchDialog).toBe(false)
    await expect(vm.submitCreate()).rejects.toThrow(); await expect(vm.submitBatch()).rejects.toThrow()
    await vm.onExecute(row); await vm.onCancel(row)
    for (const key of ['create', 'update', 'generateBatch', 'execute', 'cancel'] as const) expect(api[key]).not.toHaveBeenCalled()
    expect(ui.success).not.toHaveBeenCalled()
  })
  it('preserves status zero and fails unavailable or missing list data without false empty success', async () => {
    const vm = render(); await vm.fetchList(query); expect(api.search.mock.calls[0]![0].status).toBe(0)
    api.capabilities.mockRejectedValueOnce({ response: { status: 503 } }); await expect(vm.fetchList(query)).rejects.toThrow()
    api.search.mockResolvedValueOnce({ code: 0, data: null }); await expect(vm.fetchList(query)).rejects.toThrow()
    expect(vm.readError).not.toBe(''); expect(vm.writesAvailable).toBe(false)
  })
  it('clears protected forms on identity change and rejects an already pending old search', async () => {
    const vm = render(); const response = pending(); api.search.mockReturnValueOnce(response.promise)
    const old = vm.fetchList(query); const observed = expect(old).rejects.toThrow(); await flushPromises()
    expect(api.search).toHaveBeenCalledTimes(1); vm.dialogVisible = true; vm.createForm.productCd = 'private'
    identity(); response.resolve({ code: 0, data: [row] }); await observed
    expect(vm.dialogVisible).toBe(false); expect(vm.createForm.productCd).toBe(''); expect(vm.writesAvailable).toBe(false)
  })
  it('does not accept earlier capabilities after a later denied read', async () => {
    const vm = render(); const response = pending(); api.capabilities.mockReturnValueOnce(response.promise)
    const old = vm.fetchList(query); const observed = expect(old).rejects.toThrow()
    api.capabilities.mockRejectedValueOnce({ response: { status: 403 } }); await expect(vm.fetchList(query)).rejects.toThrow()
    response.resolve({ code: 0, data: { writesAvailable: true, status: 'LEGACY' } }); await observed
    expect(vm.writesAvailable).toBe(false)
  })
  it('rejects missing UI action and confirmation from an old identity', async () => {
    const vm = render(); permission.has.mockReturnValue(false); await expect(vm.fetchList(query)).rejects.toThrow(); expect(api.search).not.toHaveBeenCalled()
    permission.has.mockReturnValue(true); api.capabilities.mockResolvedValue({ code: 0, data: { writesAvailable: true, status: 'LEGACY' } })
    await vm.fetchList(query); const answer = pending(); ui.confirm.mockReturnValueOnce(answer.promise)
    const sending = vm.onExecute(row); identity(); answer.resolve(undefined); await sending; expect(api.execute).not.toHaveBeenCalled()
  })
  it('freezes legacy input, blocks duplicate submissions and retains unknown outcome without resend', async () => {
    const vm = render(); api.capabilities.mockResolvedValue({ code: 0, data: { writesAvailable: true, status: 'LEGACY' } })
    await vm.fetchList(query); vm.openCreate(); Object.assign(vm.createForm, row); const response = pending(); api.create.mockReturnValueOnce(response.promise)
    const saving = vm.submitCreate(); const observed = expect(saving).rejects.toThrow(); await flushPromises()
    await expect(vm.submitCreate()).rejects.toThrow(); vm.createForm.qty = 999
    expect(api.create.mock.calls[0]![0].qty).toBe(12); identity(); response.resolve({ code: 0, data: { replenishNo: 'RPL-2' } }); await observed
    expect(ui.success).not.toHaveBeenCalled(); await vm.fetchList(query); vm.openCreate(); api.create.mockRejectedValueOnce({ response: { status: 503 } })
    await expect(vm.submitCreate()).rejects.toThrow(); await vm.fetchList(query); await expect(vm.submitCreate()).rejects.toThrow()
    expect(api.create).toHaveBeenCalledTimes(2); expect(vm.intent.uncertain.value).toBe(true)
  })
})
