import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/sampleOwner', () => ({ sampleOwnerApi: api }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue'); const state = reactive({ loaded: true, actionKeys: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, get actionKeys() { return state.actionKeys }, has: (key: string) => state.actionKeys.has(key) }) }
})
import View from './SampleStockView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const rid = '10000000-0000-0000-0000-000000000001'
const row = { id: rid, sampleNo: 'SMP0', sampleType: 'PROTO', declaredQuantity: '99999999999999.9999', unitCd: 'BOX ', legacyStatus: 3,
  warehouseCd: null, locationCd: null, registeredAt: '2026-01-01T00:00:00', lentAt: '2026-01-01T10:00:00', expectedReturnDate: '2026-01-02T00:00:00', returnedAt: null,
  customerCd: 'Customer ', customerName: 'Private customer', productCd: 'Product ', productName: 'Private product', lentTo: 'Private borrower ', remarks: 'Private note',
  customerCdReadable: true, customerNameReadable: true, productCdReadable: true, productNameReadable: true, borrowerReadable: true, remarksReadable: true,
  sourceMode: 'LEGACY_OBSERVATION', stockSourceState: 'SOURCE_UNPROVEN', historyState: 'HISTORY_INCOMPLETE', siteDateState: 'UNPROVEN', returnEvidenceState: 'OUTSTANDING_UNKNOWN', outstandingQuantity: null, etag: '"AAAAAAAAAAE="' }
const wrap = (data: any) => ({ code: 0, data })
function page(q: any, total = 503) {
  const count = Math.max(0, Math.min(q.pageSize, total - (q.page - 1) * q.pageSize))
  return { version: 'SAMPLE-OWNER/1', coverage: 'CURRENT_AUTHORIZED_RECORDS', formalOperationsAvailable: false,
    page: q.page, pageSize: q.pageSize, total, items: Array.from({ length: count }, (_, i) => ({ ...row, id: `10000000-0000-0000-0000-${String((q.page - 1) * q.pageSize + i + 1).padStart(12, '0')}`, legacyStatus: q.legacyStatus ?? 3 })) }
}
function deferred() { let resolve!: (v: any) => void; let reject!: (v: any) => void; const promise = new Promise<any>((r, j) => { resolve = r; reject = j }); return { promise, resolve, reject } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
let view: ReturnType<typeof shallowMount> | undefined
async function render(): Promise<any> {
  view = shallowMount(View, { global: { config: { warnHandler: () => {} }, stubs: { CpPageShell: { template: '<div><slot/><slot name="actions"/></div>' }, ElTable: true, ElTableColumn: true } } }); await flushPromises(); return view.vm
}
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true; shared.permission.actionKeys = new Set(['wms-sample-stock:query'])
  api.search.mockReset(); api.get.mockReset(); api.search.mockImplementation(async q => wrap(page(q))); api.get.mockImplementation(async id => wrap({ ...row, id }))
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('Sample original observation Owner', () => {
  it('uses real server totals beyond 500 and preserves decimal strings and missing evidence', async () => {
    const vm = await render(); expect(vm.result.total).toBe(503); expect(vm.result.items).toHaveLength(50)
    await vm.load(11); expect(api.search.mock.lastCall?.[0]).toMatchObject({ page: 11, pageSize: 50 }); expect(vm.result.items).toHaveLength(3)
    await vm.openDetail(vm.result.items[0]); expect(view!.text()).toContain('99999999999999.9999 BOX'); expect(view!.text()).toContain('未返却数量は未確定'); expect(view!.text()).toContain('正式な在庫所在は未確認')
    expect(vm.detail.lentTo).toBe('Private borrower '); expect(vm.detail.returnEvidenceState).toBe('OUTSTANDING_UNKNOWN')
  })
  it('finds expired records still missing return evidence without calculating browser overdue', async () => {
    const vm = await render(); vm.status = '3'; vm.awaiting = true; await vm.load()
    expect(api.search.mock.lastCall?.[0]).toMatchObject({ legacyStatus: 3, awaitingReturnEvidence: true })
    await vm.openDetail(vm.result.items[0]); expect(view!.text()).toContain('返却記録の確認が必要'); expect(view!.text()).toContain('正式な期限・逾期判定は表示しません')
    api.get.mockResolvedValueOnce(wrap({ ...row, returnedAt: '2026-01-03T00:00:00', returnEvidenceState: 'RETURN_RECORDED_UNVERIFIED' }))
    await vm.openDetail(vm.result.items[0]); expect(view!.text()).toContain('返却時刻あり・実在庫未照合'); expect(vm.detail.outstandingQuantity).toBeNull()
  })
  it('does not read without query and clears late responses after revocation', async () => {
    shared.permission.actionKeys.clear(); const vm = await render(); expect(api.search).not.toHaveBeenCalled()
    shared.permission.actionKeys.add('wms-sample-stock:query'); await vm.load(); const wait = deferred(); api.search.mockReturnValueOnce(wait.promise); const pending = vm.load(2)
    shared.permission.actionKeys.clear(); expect(vm.result).toBeNull(); wait.resolve(wrap(page({ page: 2, pageSize: 50 }))); await pending; expect(vm.result).toBeNull(); expect(vm.error).toBe('')
  })
  it('invalidates old page and detail when filters change and uses a captured query', async () => {
    const vm = await render(), wait = deferred(); api.get.mockReturnValueOnce(wait.promise); const pending = vm.openDetail(vm.result.items[0])
    vm.sampleNo = 'SMP'; expect(vm.result).toBeNull(); wait.resolve(wrap(row)); await pending; expect(vm.detail).toBeNull()
    const next = deferred(); api.search.mockReturnValueOnce(next.promise); const reading = vm.load(); vm.sampleNo = 'Changed'; expect(api.search.mock.lastCall?.[0].sampleNo).toBe('SMP')
    next.resolve(wrap(page({ page: 1, pageSize: 50 }))); await reading; expect(vm.result).toBeNull()
  })
  it('isolates identity changes and old failure messages', async () => {
    const vm = await render(), wait = deferred(); api.search.mockReturnValueOnce(wait.promise); const pending = vm.load(2); vm.status = '3'; identity()
    expect(vm.result).toBeNull(); expect(vm.status).toBe('all'); wait.reject(new Error('old account')); await pending; expect(vm.error).toBe(''); expect(vm.loading).toBe(false)
  })
  it('keeps only the latest freshly authorized detail and rejects mismatched identity', async () => {
    const vm = await render(), first = vm.result.items[0], second = vm.result.items[1], wait = deferred(); api.get.mockReturnValueOnce(wait.promise)
    const pending = vm.openDetail(first); await vm.openDetail(second); wait.resolve(wrap(row)); await pending; expect(vm.detail.id).toBe(second.id)
    api.get.mockResolvedValueOnce(wrap({ ...row, id: second.id })); await vm.openDetail(first); expect(vm.detail).toBeNull(); expect(vm.error).not.toBe('')
  })
  it('removes borrower and customer text when current fields are hidden', async () => {
    const vm = await render(); api.get.mockResolvedValueOnce(wrap({ ...row, customerCd: null, customerName: null, productCd: null, productName: null, lentTo: null, remarks: null,
      customerCdReadable: false, customerNameReadable: false, productCdReadable: false, productNameReadable: false, borrowerReadable: false, remarksReadable: false }))
    await vm.openDetail(vm.result.items[0]); expect(view!.text()).not.toContain('Private'); expect(view!.text()).toContain('最終借用者の旧記録：参照不可'); expect(view!.text()).toContain('得意先：参照不可')
  })
  it('refuses false formal claims, rounded numeric quantities and hidden-field leakage', async () => {
    const vm = await render(), valid = page({ page: 1, pageSize: 50 })
    for (const bad of [{ ...valid, formalOperationsAvailable: true }, { ...valid, items: [{ ...row, declaredQuantity: 1 }, ...valid.items.slice(1)] },
      { ...valid, items: [{ ...row, borrowerReadable: false }, ...valid.items.slice(1)] }, { ...valid, items: [{ ...row, outstandingQuantity: '0' }, ...valid.items.slice(1)] }]) {
      api.search.mockResolvedValueOnce(wrap(bad)); await vm.load(); expect(vm.result).toBeNull(); expect(vm.error).not.toBe('')
    }
  })
  it('clears fields on a catalog refresh even when query remains granted', async () => {
    const vm = await render(); await vm.openDetail(vm.result.items[0]); const wait = deferred(); api.get.mockReturnValueOnce(wait.promise); const pending = vm.openDetail(vm.result.items[0])
    shared.permission.actionKeys = new Set(shared.permission.actionKeys); expect(vm.result).toBeNull(); expect(vm.detail).toBeNull(); wait.resolve(wrap(row)); await pending; expect(vm.detail).toBeNull()
  })
  it('drops out-of-order pages and responses after unmount', async () => {
    const vm = await render(), wait = deferred(); api.search.mockReturnValueOnce(wait.promise); const pending = vm.load(2); await vm.load(11)
    wait.resolve(wrap(page({ page: 2, pageSize: 50 }))); await pending; expect(vm.result.page).toBe(11)
    const late = deferred(); api.search.mockReturnValueOnce(late.promise); const reading = vm.load(); view!.unmount(); view = undefined; late.resolve(wrap(page({ page: 1, pageSize: 50 }))); await reading; expect(vm.result).toBeNull()
  })
})
