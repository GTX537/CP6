import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ stockOwner: vi.fn(), transactionsOwner: vi.fn(), monthlyStock: vi.fn(), abcAnalysis: vi.fn(), deadStock: vi.fn(), downloadCsv: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/reportCenter', () => ({ reportApi: api }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue'); const state = reactive({ loaded: true, actions: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, has: (key: string) => state.actions.has(key) }) }
})
import View from './ReportCenterView.vue'
import { reportOwnerVersion } from '@/types/wms/reportCenterOwner'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let view: ReturnType<typeof shallowMount> | undefined
const quantity = '9999999999999.99999999'
const common = { id: 'record-id', warehouseCd: 'W', locationCd: 'L', productCd: 'P', lotNo: '', unitCd: null, unitState: 'NOT_RECORDED', ownerType: 'LEGACY', ownerState: 'UNKNOWN', ownerCd: null, ownerCdReadable: false }
const stock = { ...common, physicalQty: quantity, legacyAllocatedQty: '0', legacyAvailableQty: quantity, receiveDate: null, expiryDate: null, freeQty: null, valuation: null }
const txn = { ...common, txnNo: 'T', txnType: 'ADJ', txnDateTime: '2026-01-31T23:59:59.999999', qty: '-' + quantity, operatorCd: null, operatorCdReadable: false, remark: null, remarkReadable: false, relatedNo: null, relatedType: null, relatedSourceState: 'SOURCE_DISCLOSURE_UNAVAILABLE', occurrenceState: 'NOT_RECONSTRUCTED', quantityMeaning: 'ORIGINAL_UNSIGNED_OR_SIGNED_LEGACY_VALUE', timeBasis: 'RECORDED_TXN_DATETIME_WITHOUT_SITE_TIMEZONE' }
const page = { version: reportOwnerVersion, coverage: 'CURRENT_AUTHORIZED_RECORDS', source: 'CURRENT_STOCK', snapshotState: 'NOT_CAPTURED', businessTimeState: 'NOT_RECONSTRUCTED', valuationState: 'UNKNOWN', freeState: 'NOT_EVALUATED', exportAvailable: false, total: 5001, page: 1, pageSize: 50, items: [stock] }
function pending() { let resolve!: (value: any) => void; let reject!: (reason: any) => void; const promise = new Promise<any>((r, j) => { resolve = r; reject = j }); return { promise, resolve, reject } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function render(): any { view = shallowMount(View, { global: { config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return view.vm }
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true; shared.permission.actions = new Set(['wms-report-center:query', 'wms-stock:query'])
  for (const mock of Object.values(api)) mock.mockReset()
  api.stockOwner.mockImplementation(async q => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize } }))
  api.transactionsOwner.mockImplementation(async q => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize, source: 'CURRENT_TRANSACTION_RECORDS', items: [txn] } }))
})
afterEach(() => { view?.unmount(); view = undefined })
describe('Report center current Owner', () => {
  it('preserves exact original values, unknown units/owners and real paging without formal report/export calls', async () => {
    const vm = render(); await flushPromises(); expect(vm.result.items[0].physicalQty).toBe(quantity); expect(vm.result.items[0].unitCd).toBeNull(); expect(vm.result.items[0].ownerState).toBe('UNKNOWN')
    vm.goPage(101); await flushPromises(); expect(vm.result.page).toBe(101); expect(vm.result.total).toBe(5001)
    vm.mode = 'transactions'; await flushPromises(); expect(vm.result.items[0].qty).toBe('-' + quantity); expect(vm.result.items[0].relatedNo).toBeNull(); expect(vm.result.snapshotState).toBe('NOT_CAPTURED')
    expect(api.monthlyStock).not.toHaveBeenCalled(); expect(api.abcAnalysis).not.toHaveBeenCalled(); expect(api.deadStock).not.toHaveBeenCalled(); expect(api.downloadCsv).not.toHaveBeenCalled()
  })
  it.each(['wms-report-center:query', 'wms-stock:query'])('requires %s and clears before late response after revocation', async action => {
    shared.permission.actions.delete(action); const vm = render(); await flushPromises(); expect(api.stockOwner).not.toHaveBeenCalled()
    shared.permission.actions.add(action); await vm.load({ ...vm.query }); const old = pending(); api.stockOwner.mockReturnValueOnce(old.promise); const read = vm.load({ ...vm.query })
    shared.permission.actions.delete(action); expect(vm.result).toBeNull(); old.resolve({ code: 0, data: page }); await read; expect(vm.result).toBeNull()
  })
  it('identity changes invalidate both cached and in-flight records', async () => {
    const vm = render(); await flushPromises(); identity(); expect(vm.result).toBeNull()
    const old = pending(); api.stockOwner.mockReturnValueOnce(old.promise); const read = vm.load({ ...vm.query }); identity(); old.resolve({ code: 0, data: page }); await read; expect(vm.result).toBeNull()
  })
  it('late prior mode successes and failures cannot overwrite the current result', async () => {
    const vm = render(); await flushPromises(); const old = pending(); api.stockOwner.mockReturnValueOnce(old.promise); const read = vm.load({ ...vm.query }); vm.mode = 'transactions'; await flushPromises()
    old.resolve({ code: 0, data: page }); await read; expect(vm.result.items[0].txnNo).toBe('T')
    const failed = pending(); api.transactionsOwner.mockReturnValueOnce(failed.promise); const previous = vm.load({ ...vm.query }); vm.mode = 'stocks'; await flushPromises(); failed.reject(new Error('late')); await previous; expect(vm.result.source).toBe('CURRENT_STOCK')
  })
  it('filters and paging clear immediately; mode changes remove incompatible date filters', async () => {
    const vm = render(); await flushPromises(); vm.query.productCd = 'P2'; expect(vm.result).toBeNull(); vm.search(); await flushPromises(); expect(api.stockOwner.mock.lastCall?.[0].productCd).toBe('P2')
    vm.mode = 'transactions'; await flushPromises(); vm.query.fromDate = '2026-01-01'; vm.query.toDate = '2026-01-31'; vm.query.txnType = 'OUT'; expect(vm.result).toBeNull(); vm.goPage(2); await flushPromises()
    expect(api.transactionsOwner.mock.lastCall?.[0]).toMatchObject({ fromDate: '2026-01-01', toDate: '2026-01-31', txnType: 'OUT', page: 2 })
    vm.mode = 'stocks'; await flushPromises(); expect(api.stockOwner.mock.lastCall?.[0].fromDate).toBeUndefined(); expect(api.stockOwner.mock.lastCall?.[0].txnType).toBeUndefined()
  })
  it('fails closed on unexpected contracts, rounded quantities, hidden values or fabricated free/valuation', async () => {
    const vm = render(); await flushPromises()
    for (const change of [{ version: 'legacy' }, { exportAvailable: true }, { page: 8 }, { snapshotState: 'READY_COMPLETE' }, { source: 'CURRENT_TRANSACTION_RECORDS' }, { items: [{ ...stock, physicalQty: 1 }] }, { items: [{ ...stock, ownerCd: 'secret' }] }, { items: [{ ...stock, freeQty: '3' }] }, { items: [stock, stock] }]) {
      api.stockOwner.mockResolvedValueOnce({ code: 0, data: { ...page, ...change } }); await vm.load({ ...vm.query }); expect(vm.result).toBeNull()
    }
  })
  it('rejects unavailable source refs or hidden operator metadata in otherwise valid transaction records', async () => {
    const vm = render(); await flushPromises(); vm.mode = 'transactions'; await flushPromises()
    for (const change of [{ relatedNo: 'hidden-source' }, { operatorCd: 'hidden-person' }, { remark: 'hidden-text' }, { occurrenceState: 'COMPLETE' }, { qty: -1 }]) {
      api.transactionsOwner.mockResolvedValueOnce({ code: 0, data: { ...page, source: 'CURRENT_TRANSACTION_RECORDS', items: [{ ...txn, ...change }] } }); await vm.load({ ...vm.query }); expect(vm.result).toBeNull()
    }
  })
  it('clears failures, permission unload and unmounted late responses', async () => {
    const vm = render(); await flushPromises(); api.stockOwner.mockRejectedValueOnce(new Error('failed')); await vm.load({ ...vm.query }); expect(vm.result).toBeNull()
    await vm.load({ ...vm.query }); shared.permission.loaded = false; expect(vm.result).toBeNull(); shared.permission.loaded = true
    const old = pending(); api.stockOwner.mockReturnValueOnce(old.promise); const read = vm.load({ ...vm.query }); view!.unmount(); view = undefined; old.resolve({ code: 0, data: page }); await read; expect(vm.result).toBeNull()
  })
})
