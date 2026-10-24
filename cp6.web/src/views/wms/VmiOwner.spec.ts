import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ customersOwner: vi.fn(), stockOwner: vi.fn(), billingsOwner: vi.fn(), calculate: vi.fn(), confirm: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/paperIndustry', () => ({ vmiApi: api }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue')
  const state = reactive({ loaded: true, actions: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, has: (key: string) => state.actions.has(key) }) }
})
import View from './VmiView.vue'
import { vmiVersion } from '@/types/wms/vmiOwner'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
const qty = '9999999999999.99999999', amount = '9999999999999999.99'
const customer = { customerCd: 'C1', unitCd: 'EA', unitKnowledge: 'RECORDED', stockRowCount: 1, skuCount: 1, physicalQty: qty, legacyAllocatedQty: '0', legacyAvailableQty: qty, availabilityKnowledge: 'NOT_EVALUATED', free: null, valuationKnowledge: 'UNKNOWN', estimatedValue: null }
const stock = { ...customer, id: 'stock-uuid', productCd: 'P1', lotNo: 'LOT', warehouseCd: 'W', locationCd: 'L' }
const bill = { id: 'bill-uuid', billingNo: 'VMI1', customerCd: 'C1', customerName: null, customerNameReadable: false, yearMonth: '202610', beginQty: qty, endQty: qty, avgQty: qty, dailyStorageRate: '123.4567', billingAmount: amount, rateReadable: true, amountReadable: true, legacyConfirmedFlag: true, remarks: null, remarksReadable: false, legacySemantics: 'POINT_IN_TIME_ESTIMATE', unitKnowledge: 'UNKNOWN', currencyKnowledge: 'UNKNOWN', financeOutcome: 'UNKNOWN' }
const page = { version: vmiVersion, coverage: 'AUTHORIZED_LEGACY_OBSERVATION', source: 'CURRENT_STOCK_OBSERVATION', writesAvailable: false, writeBlocker: 'VMI_OWNER_ADOPTION_UNPROVEN', total: 103, page: 1, pageSize: 50, items: [customer] }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function render(): any { wrapper = shallowMount(View, { global: { config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return wrapper.vm }
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true; shared.permission.actions = new Set(['wms-vmi:query', 'wms-stock:query', 'wms-vmi:amount-read', 'wms-vmi:rate-read'])
  for (const mock of Object.values(api)) mock.mockReset()
  api.customersOwner.mockImplementation(async q => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize } }))
  api.stockOwner.mockImplementation(async (cd, q) => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize, items: [{ ...stock, customerCd: cd }] } }))
  api.billingsOwner.mockImplementation(async q => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize, source: 'HISTORICAL_BILLING_ESTIMATE', items: [bill] } }))
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('VMI current Owner observations', () => {
  it('preserves exact values and pages customers, stock and historical estimates without write calls', async () => {
    const vm = render(); await flushPromises(); expect(vm.result.items[0].physicalQty).toBe(qty)
    vm.goPage(3); await flushPromises(); expect(vm.result.page).toBe(3); expect(vm.result.total).toBe(103)
    vm.openCustomer('C1'); await flushPromises(); expect(api.stockOwner.mock.lastCall?.[0]).toBe('C1'); expect(vm.result.items[0].physicalQty).toBe(qty)
    vm.mode = 'billings'; vm.search(); await flushPromises(); expect(vm.result.items[0].billingAmount).toBe(amount); expect(vm.result.items[0].financeOutcome).toBe('UNKNOWN')
    expect(api.calculate).not.toHaveBeenCalled(); expect(api.confirm).not.toHaveBeenCalled()
  })
  it('preserves unknown units and hidden amounts as unknown, never as zero', async () => {
    api.customersOwner.mockResolvedValueOnce({ code: 0, data: { ...page, items: [{ ...customer, unitCd: null, unitKnowledge: 'UNKNOWN', physicalQty: null, legacyAllocatedQty: null, legacyAvailableQty: null }] } })
    const vm = render(); await flushPromises(); expect(vm.result.items[0].physicalQty).toBeNull(); expect(vm.result.items[0].estimatedValue).toBeNull()
    vm.mode = 'billings'; api.billingsOwner.mockResolvedValueOnce({ code: 0, data: { ...page, source: 'HISTORICAL_BILLING_ESTIMATE', items: [{ ...bill, amountReadable: false, billingAmount: null, rateReadable: false, dailyStorageRate: null }] } })
    vm.search(); await flushPromises(); expect(vm.result.items[0].billingAmount).toBeNull(); expect(vm.result.items[0].dailyStorageRate).toBeNull()
  })
  it('requires current entry and source actions while allowing independently authorized billing reads', async () => {
    shared.permission.actions.delete('wms-stock:query'); const vm = render(); await flushPromises(); expect(api.customersOwner).not.toHaveBeenCalled()
    vm.mode = 'billings'; vm.search(); await flushPromises(); expect(vm.result).not.toBeNull()
    shared.permission.actions.delete('wms-vmi:query'); expect(vm.result).toBeNull(); vm.search(); await flushPromises(); expect(api.billingsOwner).toHaveBeenCalledTimes(1)
  })
  it('clears cached sensitive data on permission change and rejects a response claiming a revoked grant', async () => {
    const vm = render(); await flushPromises(); vm.mode = 'billings'; vm.search(); await flushPromises()
    shared.permission.actions.delete('wms-vmi:amount-read'); expect(vm.result).toBeNull(); vm.search(); await flushPromises(); expect(vm.result).toBeNull()
    shared.permission.loaded = false; expect(vm.result).toBeNull()
  })
  it('drops delayed success after an identity change or unmount', async () => {
    const vm = render(); await flushPromises(); const first = pending(); api.customersOwner.mockReturnValueOnce(first.promise); const old = vm.load({ ...vm.query })
    identity(); first.resolve({ code: 0, data: page }); await old; expect(vm.result).toBeNull()
    const second = pending(); api.customersOwner.mockReturnValueOnce(second.promise); const leaving = vm.load({ ...vm.query })
    wrapper!.unmount(); wrapper = undefined; second.resolve({ code: 0, data: page }); await leaving; expect(vm.result).toBeNull()
  })
  it('keeps a newer denied result empty when an older success arrives', async () => {
    const vm = render(); await flushPromises(); const deferred = pending(); api.customersOwner.mockReturnValueOnce(deferred.promise); const old = vm.load({ ...vm.query })
    api.customersOwner.mockRejectedValueOnce({ response: { status: 403 } }); await vm.load({ ...vm.query }); deferred.resolve({ code: 0, data: page }); await old
    expect(vm.result).toBeNull(); expect(vm.error).not.toBe(''); expect(vm.loading).toBe(false)
  })
  it('clears results on customer, tab and page changes and rejects the old response', async () => {
    const vm = render(); await flushPromises(); const deferred = pending(); api.customersOwner.mockReturnValueOnce(deferred.promise); const old = vm.load({ ...vm.query })
    vm.query.customerCd = 'C2'; expect(vm.result).toBeNull(); deferred.resolve({ code: 0, data: page }); await old; expect(vm.result).toBeNull()
    vm.mode = 'stock'; vm.search(); await flushPromises(); expect(vm.result.items[0].customerCd).toBe('C2')
    vm.query.page = 2; expect(vm.result).toBeNull(); vm.mode = 'billings'; expect(vm.result).toBeNull()
  })
  it('rejects numeric precision loss, mixed-unit sums and a wrong response source', async () => {
    const vm = render(); await flushPromises()
    for (const changed of [{ version: 'legacy' }, { writesAvailable: true }, { source: 'HISTORICAL_BILLING_ESTIMATE' }, { page: 9 }, { items: [{ ...customer, physicalQty: 9999999999999 }] }, { items: [{ ...customer, unitCd: null, unitKnowledge: 'UNKNOWN' }] }]) {
      api.customersOwner.mockResolvedValueOnce({ code: 0, data: { ...page, ...changed } }); await vm.load({ ...vm.query }); expect(vm.result).toBeNull()
    }
  })
  it('sends false and cleared confirmation distinctly and removes billing filters from stock requests', async () => {
    const vm = render(); await flushPromises(); vm.mode = 'billings'; vm.confirmed = 'false'; vm.query.yearMonth = '202610'; vm.search(); await flushPromises()
    expect(api.billingsOwner.mock.lastCall?.[0]).toMatchObject({ confirmed: false, yearMonth: '202610' })
    vm.confirmed = ''; vm.goPage(2); await flushPromises(); expect(api.billingsOwner.mock.lastCall?.[0].confirmed).toBeUndefined()
    vm.mode = 'stock'; vm.query.customerCd = 'C1'; vm.search(); await flushPromises(); expect(api.stockOwner.mock.lastCall?.[1].yearMonth).toBeUndefined(); expect(api.stockOwner.mock.lastCall?.[1].confirmed).toBeUndefined()
  })
})
