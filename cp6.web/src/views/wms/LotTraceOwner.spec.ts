import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ read: vi.fn(), recall: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any, confirm: vi.fn() }))
vi.mock('@/api/wms/lotTrace', () => ({ lotTraceApi: api }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue')
  const state = reactive({ loaded: true, actions: new Set<string>() })
  shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, has: (key: string) => state.actions.has(key) }) }
})
vi.mock('element-plus', async original => ({ ...await original<typeof import('element-plus')>(), ElMessageBox: { confirm: shared.confirm } }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from './LotTraceView.vue'
import { lotTraceVersion } from '@/types/wms/lotTraceOwner'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'

let wrapper: ReturnType<typeof shallowMount> | undefined
const query = { productCd: 'MAT', lotNo: 'LOT', direction: 'FORWARD' }
const snapshot = { ...query, version: lotTraceVersion, coverage: 'AUTHORIZED_LOT_OBSERVATION',
  operatorReadable: true, remarkReadable: true, customerNameReadable: true, supplierNameReadable: true,
  stock: { recallVersion: 'real-complete-set-version', physicalQty: '19999999999999.99999998', availableQty: '0.00000001', stockCount: 2, recallState: 'NONE' },
  nodes: [{ txnNo: 'T1', qty: '9999999999999.99999999', operatorCd: 'private', remark: 'private' }], customers: [], suppliers: [] }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function render(): any {
  wrapper = shallowMount(View, { global: { config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } })
  const vm = wrapper.vm as any; Object.assign(vm.query, query); return vm
}
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true
  shared.permission.actions = new Set(['wms-lot-trace:query', 'wms-lot-trace:recall', 'wms-stock:query'])
  api.read.mockReset(); api.recall.mockReset(); shared.confirm.mockReset()
  api.read.mockResolvedValue({ code: 0, data: snapshot }); shared.confirm.mockResolvedValue('confirm')
  api.recall.mockImplementation(async command => ({ code: 0, data: { ...command, version: lotTraceVersion, affectedStocks: 2 } }))
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })

describe('lot trace current Owner page', () => {
  it('uses exact quantities and accepts only the matching named observation', async () => {
    const vm = render(); await vm.load({ ...query })
    expect(vm.result.stock.physicalQty).toBe('19999999999999.99999998'); expect(vm.result.nodes[0].qty).toBe('9999999999999.99999999')
    api.read.mockResolvedValueOnce({ code: 0, data: { ...snapshot, lotNo: 'OTHER' } }); await vm.load({ ...query })
    expect(vm.result).toBeNull(); expect(vm.readError).not.toBe('')
    api.read.mockResolvedValueOnce({ code: 0, data: { ...snapshot, version: 'legacy' } }); await vm.load({ ...query }); expect(vm.result).toBeNull()
  })
  it('does not query without current entry and stock source permissions', async () => {
    const vm = render(); shared.permission.actions.delete('wms-stock:query'); await vm.load({ ...query })
    expect(api.read).not.toHaveBeenCalled(); expect(vm.result).toBeNull()
    shared.permission.actions.add('wms-stock:query'); await vm.load({ ...query }); shared.permission.loaded = false
    expect(vm.result).toBeNull(); expect(vm.canRecall()).toBe(false)
  })
  it('clears protected results immediately when the product lot or direction changes', async () => {
    const vm = render(); await vm.load({ ...query }); vm.query.lotNo = 'OTHER'; expect(vm.result).toBeNull()
    await vm.load({ ...query }); vm.query.direction = 'BACKWARD'; expect(vm.result).toBeNull()
  })
  it('does not let an old success overwrite a newer denied read', async () => {
    const vm = render(); const response = pending(); api.read.mockReturnValueOnce(response.promise); const old = vm.load({ ...query })
    api.read.mockRejectedValueOnce({ response: { status: 403 } }); await vm.load({ ...query })
    response.resolve({ code: 0, data: snapshot }); await old; expect(vm.result).toBeNull(); expect(vm.readError).not.toBe('')
  })
  it('discards old identity reads and clears displayed records', async () => {
    const vm = render(); await vm.load({ ...query }); identity(); expect(vm.result).toBeNull()
    const response = pending(); api.read.mockReturnValueOnce(response.promise); const reading = vm.load({ ...query })
    identity(); response.resolve({ code: 0, data: snapshot }); await reading; expect(vm.result).toBeNull()
  })
  it('confirms and submits the actually displayed lot and native set version', async () => {
    const vm = render(); await vm.load({ ...query }); await vm.setRecall(true)
    expect(api.recall).toHaveBeenCalledExactlyOnceWith({ productCd: 'MAT', lotNo: 'LOT', flag: true, recallVersion: snapshot.stock.recallVersion })
    expect(vm.message).toContain('库存数量未变'); expect(api.read).toHaveBeenCalledTimes(2)
  })
  it('does not submit after query changes while confirmation is pending', async () => {
    const vm = render(); await vm.load({ ...query }); const dialog = pending(); shared.confirm.mockReturnValueOnce(dialog.promise)
    const saving = vm.setRecall(true); vm.query.productCd = 'OTHER'; dialog.resolve('confirm'); await saving
    expect(api.recall).not.toHaveBeenCalled(); expect(vm.result).toBeNull(); expect(vm.intent.busy.value).toBe(false)
  })
  it('does not submit after identity or action permission changes during confirmation', async () => {
    const vm = render(); await vm.load({ ...query }); const dialog = pending(); shared.confirm.mockReturnValueOnce(dialog.promise)
    const saving = vm.setRecall(true); identity(); dialog.resolve('confirm'); await saving; expect(api.recall).not.toHaveBeenCalled()
    await vm.load({ ...query }); const next = pending(); shared.confirm.mockReturnValueOnce(next.promise)
    const denied = vm.setRecall(true); shared.permission.actions.delete('wms-lot-trace:recall'); next.resolve('confirm'); await denied
    expect(api.recall).not.toHaveBeenCalled()
  })
  it('does not label a cancelled confirmation as an unknown mutation', async () => {
    const vm = render(); await vm.load({ ...query }); shared.confirm.mockRejectedValueOnce('cancel'); await vm.setRecall(true)
    expect(api.recall).not.toHaveBeenCalled(); expect(vm.intent.uncertain.value).toBe(false); expect(vm.intent.busy.value).toBe(false)
  })
  it('blocks duplicates and never automatically resends stale or unknown writes', async () => {
    const vm = render(); await vm.load({ ...query }); const response = pending(); api.recall.mockReturnValueOnce(response.promise)
    const saving = vm.setRecall(true); await flushPromises(); await vm.setRecall(true); expect(api.recall).toHaveBeenCalledTimes(1)
    identity(); response.resolve({ code: 0, data: { ...query, version: lotTraceVersion, flag: true } }); await saving
    expect(vm.result).toBeNull(); expect(vm.message).toBe('')
    await vm.load({ ...query }); api.recall.mockRejectedValueOnce({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } })
    await vm.setRecall(true); expect(vm.intent.uncertain.value).toBe(true); expect(vm.result).toBeNull(); await vm.setRecall(true); expect(api.recall).toHaveBeenCalledTimes(2)
    await vm.load({ ...query }); api.recall.mockRejectedValueOnce({ response: { status: 409 } }); await vm.setRecall(false)
    expect(vm.result).toBeNull(); expect(vm.message).toBe(''); expect(api.recall).toHaveBeenCalledTimes(3)
  })
  it('cannot use an empty stock snapshot as a recall command', async () => {
    api.read.mockResolvedValueOnce({ code: 0, data: { ...snapshot, stock: null } }); const vm = render(); await vm.load({ ...query })
    await vm.setRecall(true); expect(shared.confirm).not.toHaveBeenCalled(); expect(api.recall).not.toHaveBeenCalled()
  })
  it('preserves the existing empty no-lot key and does not trim a different stored key', async () => {
    const vm = render(); vm.query.lotNo = ''; api.read.mockResolvedValue({ code: 0, data: { ...snapshot, lotNo: '' } })
    vm.search(); await flushPromises(); expect(api.read).toHaveBeenLastCalledWith({ ...query, lotNo: '' })
    await vm.setRecall(true); expect(api.recall).toHaveBeenCalledExactlyOnceWith({ productCd: 'MAT', lotNo: '', flag: true, recallVersion: snapshot.stock.recallVersion })
    vm.query.lotNo = ' LOT '; api.read.mockResolvedValueOnce({ code: 0, data: { ...snapshot, lotNo: ' LOT ' } }); vm.search(); await flushPromises()
    expect(api.read).toHaveBeenLastCalledWith({ ...query, lotNo: ' LOT ' }); expect(vm.result.lotNo).toBe(' LOT ')
  })
})
