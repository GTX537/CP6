import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ searchMastersOwner: vi.fn(), getMasterOwner: vi.fn(), searchOrdersOwner: vi.fn(), getOrderOwner: vi.fn(), execute: vi.fn(), cancel: vi.fn(), createMaster: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/kitting', () => ({ kittingApi: api }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue'); const state = reactive({ loaded: true, actions: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, has: (key: string) => state.actions.has(key) }) }
})
import View from './KitView.vue'
import { kitVersion } from '@/types/wms/kittingOwner'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let view: ReturnType<typeof shallowMount> | undefined
const quantity = '9999999999999.99999999'
const master = { version: kitVersion, id: 'master-id', kitSku: 'K1', kitName: 'current recipe', defaultWarehouseCd: 'W1', activeFlg: true, remarks: null, kitNameReadable: true, remarksReadable: false, recipeState: 'CURRENT_LEGACY_RECIPE' }
const component = { id: 'component-id', lineNo: 1, componentProductCd: 'C1', componentName: null, requiredQty: quantity, unitCd: 'EA', remarks: null, componentNameReadable: false, remarksReadable: false }
const masterDetail = { version: kitVersion, historicalComposition: 'UNPROVEN', master, components: [component] }
const order = { version: kitVersion, id: 'order-id', kitOrderNo: 'O1', kitSku: 'K1', kitName: 'historical name', qty: quantity, unitCd: null, quantityUnitState: 'NOT_RECORDED', direction: 'ASSEMBLE', warehouseCd: 'W1', kitLocationCd: 'L1', kitLotNo: 'OLD', legacyStatus: 1, executedAt: null, createDate: null, operatorCd: null, remarks: null, executedTxnNos: 'T1;T2', kitNameReadable: true, operatorCdReadable: false, remarksReadable: false, executedTxnNosReadable: true, transactionReferencesState: 'AUTHORIZED_ORIGINAL_REFERENCES', compositionState: 'UNPROVEN' }
const page = { version: kitVersion, coverage: 'AUTHORIZED_LEGACY_OBSERVATION', writesAvailable: false, writeBlocker: 'KIT_OWNER_ADOPTION_UNPROVEN', total: 203, page: 1, pageSize: 50, items: [master] }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function render(): any { view = shallowMount(View, { global: { config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return view.vm }
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true; shared.permission.actions = new Set(['wms-kitting:query', 'wms-stock:query'])
  for (const mock of Object.values(api)) mock.mockReset()
  api.searchMastersOwner.mockImplementation(async q => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize } }))
  api.searchOrdersOwner.mockImplementation(async q => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize, items: [order] } }))
  api.getMasterOwner.mockResolvedValue({ code: 0, data: masterDetail }); api.getOrderOwner.mockResolvedValue({ code: 0, data: order })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('Kitting current Owner observations', () => {
  it('keeps exact quantities, source units and independent historical meaning with real paging', async () => {
    const vm = render(); await flushPromises(); expect(vm.result.total).toBe(203); await vm.openDetail(master)
    expect(vm.masterDetail.components[0].requiredQty).toBe(quantity); expect(vm.masterDetail.components[0].unitCd).toBe('EA')
    vm.goPage(5); await flushPromises(); expect(api.searchMastersOwner.mock.lastCall?.[0]).toMatchObject({ page: 5 }); expect(vm.result.page).toBe(5)
    vm.mode = 'orders'; await flushPromises(); await vm.openDetail(order)
    expect(vm.orderDetail.qty).toBe(quantity); expect(vm.orderDetail.unitCd).toBeNull(); expect(vm.orderDetail.kitName).toBe('historical name')
    expect(vm.orderDetail.compositionState).toBe('UNPROVEN'); expect(vm.statusLabel(1)).toBe('旧执行标记'); expect(vm.masterDetail).toBeNull()
    expect(api.execute).not.toHaveBeenCalled(); expect(api.cancel).not.toHaveBeenCalled(); expect(api.createMaster).not.toHaveBeenCalled()
  })
  it('preserves false and zero filters while consistently clearing empty selectors', async () => {
    const vm = render(); await flushPromises(); vm.query.active = false; vm.search(); await flushPromises(); expect(api.searchMastersOwner.mock.lastCall?.[0].active).toBe(false)
    vm.goPage(2); await flushPromises(); expect(api.searchMastersOwner.mock.lastCall?.[0].active).toBe(false)
    vm.mode = 'orders'; await flushPromises(); vm.query.status = 0; vm.search(); await flushPromises(); expect(api.searchOrdersOwner.mock.lastCall?.[0].status).toBe(0)
    vm.query.status = ''; vm.goPage(2); await flushPromises(); expect(api.searchOrdersOwner.mock.lastCall?.[0].status).toBeUndefined(); expect(api.searchOrdersOwner.mock.lastCall?.[0].active).toBeUndefined()
  })
  it('requires the current action and clears data when source authority becomes unavailable', async () => {
    shared.permission.actions.delete('wms-kitting:query'); const vm = render(); await flushPromises(); expect(api.searchMastersOwner).not.toHaveBeenCalled()
    shared.permission.actions.add('wms-kitting:query'); vm.mode = 'orders'; await flushPromises(); await vm.openDetail(order)
    shared.permission.actions.delete('wms-stock:query'); expect(vm.result).toBeNull(); expect(vm.orderDetail).toBeNull()
    api.searchOrdersOwner.mockResolvedValueOnce({ code: 0, data: { ...page, items: [{ ...order, executedTxnNosReadable: false, executedTxnNos: null, transactionReferencesState: 'SOURCE_DISCLOSURE_UNAVAILABLE' }] } })
    await vm.load({ ...vm.query }); expect(vm.result.items[0].executedTxnNos).toBeNull(); shared.permission.loaded = false; expect(vm.result).toBeNull()
  })
  it('invalidates both modes on identity changes and ignores old successes', async () => {
    const vm = render(); await flushPromises(); await vm.openDetail(master); identity(); expect(vm.result).toBeNull(); expect(vm.masterDetail).toBeNull()
    const old = pending(); api.searchMastersOwner.mockReturnValueOnce(old.promise); const read = vm.load({ ...vm.query }); identity()
    old.resolve({ code: 0, data: page }); await read; expect(vm.result).toBeNull()
  })
  it('does not let an old mode replace the newer mode or detail', async () => {
    const vm = render(); await flushPromises(); const old = pending(); api.searchMastersOwner.mockReturnValueOnce(old.promise); const read = vm.load({ ...vm.query })
    vm.mode = 'orders'; await flushPromises(); await vm.openDetail(order); old.resolve({ code: 0, data: page }); await read
    expect(vm.result.items[0].kitOrderNo).toBe('O1'); expect(vm.orderDetail.id).toBe('order-id')
  })
  it('clears immediately on filters and prevents older detail responses from returning', async () => {
    const vm = render(); await flushPromises(); const old = pending(); api.getMasterOwner.mockReturnValueOnce(old.promise); const read = vm.openDetail(master)
    vm.query.keyword = 'other'; expect(vm.result).toBeNull(); expect(vm.detailLoading).toBe(false)
    old.resolve({ code: 0, data: masterDetail }); await read; expect(vm.masterDetail).toBeNull()
  })
  it('keeps a newer denial empty after an older list succeeds', async () => {
    const vm = render(); await flushPromises(); const old = pending(); api.searchMastersOwner.mockReturnValueOnce(old.promise); const read = vm.load({ ...vm.query })
    api.searchMastersOwner.mockRejectedValueOnce({ response: { status: 403 } }); await vm.load({ ...vm.query }); old.resolve({ code: 0, data: page }); await read
    expect(vm.result).toBeNull(); expect(vm.error).not.toBe(''); expect(vm.loading).toBe(false)
  })
  it('validates exact detail identity and rejects numeric or undisclosed component content', async () => {
    const vm = render(); await flushPromises()
    for (const change of [{ master: { ...master, id: 'different' } }, { components: [{ ...component, requiredQty: 1.25 }] }, { components: [{ ...component, componentName: 'leak' }] }, { historicalComposition: 'PROVEN' }]) {
      await vm.load({ ...vm.query }); api.getMasterOwner.mockResolvedValueOnce({ code: 0, data: { ...masterDetail, ...change } }); await vm.openDetail(master); expect(vm.masterDetail).toBeNull(); expect(vm.result).toBeNull()
    }
  })
  it('rejects changed contracts, invented units and unavailable raw transaction disclosures', async () => {
    const vm = render(); await flushPromises(); vm.mode = 'orders'; await flushPromises()
    for (const change of [{ version: 'legacy' }, { writesAvailable: true }, { page: 8 }, { items: [{ ...order, qty: 1 }] }, { items: [{ ...order, unitCd: 'EA' }] }, { items: [{ ...order, executedTxnNosReadable: false }] }]) {
      api.searchOrdersOwner.mockResolvedValueOnce({ code: 0, data: { ...page, items: [order], ...change } }); await vm.load({ ...vm.query }); expect(vm.result).toBeNull()
    }
  })
  it('drops detail responses after unmount without issuing a write', async () => {
    const vm = render(); await flushPromises(); const old = pending(); api.getMasterOwner.mockReturnValueOnce(old.promise); const read = vm.openDetail(master)
    view!.unmount(); view = undefined; old.resolve({ code: 0, data: masterDetail }); await read; expect(vm.masterDetail).toBeNull(); expect(api.execute).not.toHaveBeenCalled()
  })
})
