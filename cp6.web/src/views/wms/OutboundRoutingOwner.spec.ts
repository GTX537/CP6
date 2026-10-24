import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), warehouses: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/routingOwner', () => ({ routingOwnerApi: api }))
vi.mock('@/api/wms/warehouseOwner', () => ({ warehouseOwnerApi: { warehouses: api.warehouses } }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue'); const state = reactive({ loaded: true, actionKeys: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, get actionKeys() { return state.actionKeys }, has: (key: string) => state.actionKeys.has(key) }) }
})
import View from './OutboundRoutingView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const rid = '10000000-0000-0000-0000-000000000001', wid = '20000000-0000-0000-0000-000000000001'
const row = { id: rid, ruleName: 'Existing', sortOrder: 0, customerCd: '', customerCondition: 'LEGACY_EXACT', productCdPrefix: '', productCondition: 'LEGACY_ANY', outboundType: null,
  targetWarehouseId: wid, targetWarehouseCd: 'W', legacyEnabled: true, remarks: 'Private', etag: '"AAAAAAAAAAE="', nameReadable: true, customerReadable: true, productReadable: true, remarksReadable: true,
  formalPolicyState: 'UNPROVEN', domainState: 'NOT_RECORDED', selectorState: 'NOT_ADOPTED' }
const wrap = (data: any) => ({ code: 0, data })
function page(q: any, total = 103) {
  const count = Math.max(0, Math.min(q.pageSize, total - (q.page - 1) * q.pageSize))
  return { version: 'ROUTING-OWNER/1', coverage: 'CURRENT_AUTHORIZED_RECORDS', formalPreviewAvailable: false, legacyResolverEnabled: false,
    page: q.page, pageSize: q.pageSize, total, items: Array.from({ length: count }, (_, i) => ({ ...row, id: `10000000-0000-0000-0000-${String((q.page - 1) * q.pageSize + i + 1).padStart(12, '0')}`, legacyEnabled: q.enabled ?? true })) }
}
function deferred() { let resolve!: (v: any) => void; let reject!: (v: any) => void; const promise = new Promise<any>((r, j) => { resolve = r; reject = j }); return { promise, resolve, reject } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
let view: ReturnType<typeof shallowMount> | undefined
async function render(): Promise<any> {
  view = shallowMount(View, { global: { config: { warnHandler: () => {} }, stubs: { CpPageShell: { template: '<div><slot/><slot name="actions"/></div>' }, ElTable: true, ElTableColumn: true } } }); await flushPromises(); return view.vm
}
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true; shared.permission.actionKeys = new Set(['wms-outbound-routing:query', 'wms-warehouse:query'])
  for (const mock of Object.values(api)) mock.mockReset()
  api.search.mockImplementation(async q => wrap(page(q))); api.get.mockImplementation(async id => wrap({ ...row, id }))
  api.warehouses.mockResolvedValue(wrap({ version: 'WAREHOUSE-OWNER/1', coverage: 'CURRENT_AUTHORIZED_RECORDS', formalMasterState: 'UNPROVEN', page: 1, pageSize: 200, total: 0, items: [] }))
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('Outbound routing current original records', () => {
  it('pages actual observations while feature-disabled is independent of record visibility', async () => {
    const vm = await render(); expect(vm.result.total).toBe(103); expect(vm.result.items).toHaveLength(50); expect(view!.text()).toContain('無効（単一倉庫の従来動作）')
    await vm.load(3); expect(vm.result.items).toHaveLength(3); expect(api.search.mock.lastCall?.[0]).toMatchObject({ page: 3, pageSize: 50 })
    await vm.openDetail(vm.result.items[0]); expect(vm.detail.customerCondition).toBe('LEGACY_EXACT'); expect(view!.text()).toContain('旧完全一致：空文字'); expect(view!.text()).toContain('正式ドメインの ANY を示しません')
  })
  it.each(['wms-outbound-routing:query', 'wms-warehouse:query'])('does not fetch without %s and clears late responses after revocation', async key => {
    shared.permission.actionKeys.delete(key); const vm = await render(); expect(api.search).not.toHaveBeenCalled()
    shared.permission.actionKeys.add(key); await vm.load(); const wait = deferred(); api.search.mockReturnValueOnce(wait.promise); const pending = vm.load(2)
    shared.permission.actionKeys.delete(key); expect(vm.result).toBeNull(); wait.resolve(wrap(page({ page: 2, pageSize: 50 }))); await pending; expect(vm.result).toBeNull(); expect(vm.error).toBe('')
  })
  it('freezes filters and rejects previous page/detail after changed intent', async () => {
    const vm = await render(); const old = vm.result.items[0], wait = deferred(); api.get.mockReturnValueOnce(wait.promise); const pending = vm.openDetail(old)
    vm.enabled = 'false'; expect(vm.result).toBeNull(); expect(vm.detail).toBeNull(); wait.resolve(wrap(row)); await pending; expect(vm.detail).toBeNull()
    vm.warehouseId = wid; await vm.load(1); expect(api.search.mock.lastCall?.[0]).toMatchObject({ targetWarehouseId: wid, enabled: false }); expect(vm.result.items[0].legacyEnabled).toBe(false)
  })
  it('isolates old identity responses and failure notices', async () => {
    const vm = await render(), wait = deferred(); api.search.mockReturnValueOnce(wait.promise); const pending = vm.load(2); identity(); expect(vm.result).toBeNull()
    wait.reject(new Error('old identity')); await pending; expect(vm.error).toBe(''); expect(vm.loading).toBe(false); expect(vm.warehouseId).toBe('')
  })
  it('only shows the most recently selected freshly read detail', async () => {
    const vm = await render(), first = vm.result.items[0], second = vm.result.items[1], wait = deferred(); api.get.mockReturnValueOnce(wait.promise)
    const pending = vm.openDetail(first); await vm.openDetail(second); wait.resolve(wrap(row)); await pending; expect(vm.detail.id).toBe(second.id)
    api.get.mockResolvedValueOnce(wrap({ ...row, id: second.id })); await vm.openDetail(first); expect(vm.detail).toBeNull(); expect(vm.error).not.toBe('')
  })
  it('keeps hidden original conditions distinct from unspecified conditions', async () => {
    const vm = await render(); api.get.mockResolvedValueOnce(wrap({ ...row, ruleName: null, customerCd: null, productCdPrefix: null, remarks: null,
      nameReadable: false, customerReadable: false, productReadable: false, remarksReadable: false, customerCondition: 'HIDDEN', productCondition: 'HIDDEN' }))
    await vm.openDetail(vm.result.items[0]); expect(view!.text()).not.toContain('Private'); expect(view!.text()).toContain('得意先条件：参照不可'); expect(vm.detail.customerCondition).toBe('HIDDEN')
  })
  it('refuses malformed scoped results and formal-policy claims', async () => {
    const vm = await render(), valid = page({ page: 1, pageSize: 50 })
    api.search.mockResolvedValueOnce(wrap({ ...valid, formalPreviewAvailable: true })); await vm.load(); expect(vm.result).toBeNull()
    api.search.mockResolvedValueOnce(wrap({ ...valid, items: [{ ...row, nameReadable: false }, ...valid.items.slice(1)] })); await vm.load(); expect(vm.result).toBeNull()
    vm.warehouseId = '20000000-0000-0000-0000-000000000002'; api.search.mockResolvedValueOnce(wrap(valid)); await vm.load(); expect(vm.result).toBeNull()
  })
  it('clears data when action catalog is refreshed and ignores responses after unmount', async () => {
    const vm = await render(); shared.permission.actionKeys = new Set(shared.permission.actionKeys); expect(vm.result).toBeNull()
    const wait = deferred(); api.search.mockReturnValueOnce(wait.promise); const pending = vm.load(); view!.unmount(); view = undefined; wait.resolve(wrap(page({ page: 1, pageSize: 50 }))); await pending; expect(vm.result).toBeNull()
  })
  it('invalidates late warehouse choices on a permission catalog refresh with unchanged query grants', async () => {
    const vm = await render(), wait = deferred(); vm.warehouseId = wid; api.warehouses.mockReturnValueOnce(wait.promise); const pending = vm.loadWarehouses('W')
    shared.permission.actionKeys = new Set(shared.permission.actionKeys); expect(vm.warehouseId).toBe('')
    wait.resolve(wrap({ version: 'WAREHOUSE-OWNER/1', coverage: 'CURRENT_AUTHORIZED_RECORDS', formalMasterState: 'UNPROVEN', page: 1, pageSize: 200, total: 1,
      items: [{ id: wid, warehouseCd: 'W', warehouseName: 'Old visible name', nameReadable: true, nameWritable: true, warehouseType: 1, baseCd: null,
        addressText: null, managerCd: null, remarks: null, addressReadable: false, managerReadable: false, remarksReadable: false,
        legacyAllowNegative: false, legacyOutboundPriority: 0, siteId: null, siteState: 'NOT_RECORDED', lifecycleState: 'UNPROVEN', etag: row.etag }] }))
    await pending; expect(vm.warehouses).toEqual([]); expect(vm.warehouseTotal).toBe(0)
  })
})
