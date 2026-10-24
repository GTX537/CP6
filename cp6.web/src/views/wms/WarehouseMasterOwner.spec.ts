import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
import { defineComponent } from 'vue'
const api = vi.hoisted(() => ({ warehouses: vi.fn(), locations: vi.fn(), scope: vi.fn(), rename: vi.fn(), operation: vi.fn(), close: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any, leave: undefined as any, confirm: vi.fn() }))
vi.mock('@/api/wms/warehouseOwner', () => ({ warehouseOwnerApi: api }))
vi.mock('element-plus', () => ({ ElMessageBox: { confirm: shared.confirm } }))
vi.mock('vue-router', () => ({ onBeforeRouteLeave: (fn: any) => { shared.leave = fn } }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue'); const state = reactive({ loaded: true, actions: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, has: (key: string) => state.actions.has(key) }) }
})
import View from './WarehouseMasterOwnerView.vue'
import { useWarehouseOwnerChoices } from '@/composables/useWarehouseOwnerChoices'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import type { MasterKind } from '@/types/wms/warehouseOwner'
const tenantId = '10000000-0000-0000-0000-000000000001', actorId = '20000000-0000-0000-0000-000000000001'
const wid = '30000000-0000-0000-0000-000000000001', lid = '40000000-0000-0000-0000-000000000001'
const oldEtag = '"AAAAAAAAAAE="', nextEtag = '"AAAAAAAAAAI="'
const warehouse = { id: wid, warehouseCd: 'W', warehouseName: 'Original', warehouseType: 1, baseCd: null, addressText: null, managerCd: null, remarks: null, addressReadable: false, managerReadable: false, remarksReadable: false, legacyAllowNegative: true, legacyOutboundPriority: 0, siteId: null, siteState: 'NOT_RECORDED', lifecycleState: 'UNPROVEN', etag: oldEtag, nameReadable: true, nameWritable: true }
const location = { id: lid, warehouseId: wid, warehouseCd: 'W', locationCd: 'L', locationName: 'Original', nameReadable: true, nameWritable: true, locationLevel: 1, parentId: null, parentLocationCd: null, parentState: 'SOURCE_DISCLOSURE_UNAVAILABLE', rawCapacityQty: '0', rawReservedCapacityQty: '9999999999999.99999999', areaCd: 'A', rawAllowedProductType: 'legacy', legacyIsPickable: true, legacyIsBlocked: false, barcode: null, barcodeReadable: false, capacityState: 'UNKNOWN', typedCapacity: null, confirmedReservedQty: null, reservationState: 'UNPROVEN', hierarchyState: 'ORIGINAL_REFERENCES_ONLY', lifecycleState: 'UNPROVEN', etag: oldEtag }
const page = (row: any, q: any) => ({ version: 'WAREHOUSE-OWNER/1', coverage: 'CURRENT_AUTHORIZED_RECORDS', formalMasterState: 'UNPROVEN', total: 501, page: q.page, pageSize: q.pageSize, items: [row] })
const wrap = (data: any) => ({ code: 0, data })
const key = (kind: string, actor = actorId) => `cp6.master-name:${kind}:${tenantId}:${actor}`
function deferred() { let resolve!: (v: any) => void; let reject!: (v: any) => void; const promise = new Promise<any>((r, j) => { resolve = r; reject = j }); return { promise, resolve, reject } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
let view: ReturnType<typeof shallowMount> | undefined
async function render(kind: MasterKind = 'warehouses'): Promise<any> {
  view = shallowMount(View, { props: { kind }, global: { config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); await flushPromises()
  const vm: any = view.vm
  if (kind === 'locations') { vm.warehouseId = wid; await flushPromises() }
  return vm
}
beforeEach(() => {
  localStorage.clear(); sessionStorage.clear(); shared.permission.loaded = true
  shared.permission.actions = new Set(['wms-warehouse:query', 'wms-warehouse:edit', 'wms-warehouse:abandon', 'wms-location:query', 'wms-location:edit', 'wms-location:abandon'])
  for (const mock of Object.values(api)) mock.mockReset()
  shared.confirm.mockReset().mockResolvedValue('confirm'); api.scope.mockResolvedValue(wrap({ tenantId, actorId }))
  api.warehouses.mockImplementation(async q => wrap(page(warehouse, q))); api.locations.mockImplementation(async (_, q) => wrap(page(location, q)))
  api.rename.mockImplementation(async (kind, objectId, operationId) => wrap({ kind, objectId, operationId, effect: 'CHANGED', etag: nextEtag }))
  api.operation.mockImplementation(async (kind, objectId, operationId) => wrap({ kind, objectId, operationId, state: 'UNKNOWN', result: null }))
  api.close.mockImplementation(async (kind, objectId, operationId) => wrap({ kind, objectId, operationId, state: 'CLOSED', result: null }))
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('Warehouse and location current Owner', () => {
  it.each(['warehouses', 'locations'] as MasterKind[])('uses paged %s observations and exact name-only intent', async kind => {
    const vm = await render(kind); expect(vm.result.total).toBe(501); await vm.goPage(8); expect(vm.result.page).toBe(8)
    if (kind === 'locations') { expect(vm.result.items[0].rawReservedCapacityQty).toBe('9999999999999.99999999'); expect(vm.result.items[0].capacityState).toBe('UNKNOWN'); await vm.loadChoices(3); expect(vm.choices.page).toBe(3) }
    await vm.openName(vm.result.items[0]); vm.name = 'New'; await vm.saveName()
    expect(api.rename.mock.lastCall).toEqual([kind, kind === 'warehouses' ? wid : lid, expect.any(String), oldEtag, { name: { op: 'SET', value: 'New' } }])
    expect(vm.pending).toBeNull(); expect(sessionStorage.getItem(key(kind))).toBeNull(); expect(vm.result.items[0][kind === 'warehouses' ? 'warehouseName' : 'locationName']).toBe('Original')
  })
  it('sends explicit location CLEAR and permits exact unchanged-value NO_CHANGE', async () => {
    const vm = await render('locations'); await vm.openName(vm.result.items[0]); vm.clearName = true; await vm.saveName(); expect(api.rename.mock.lastCall?.[4]).toEqual({ name: { op: 'CLEAR' } })
    api.rename.mockImplementationOnce(async (kind, objectId, operationId) => wrap({ kind, objectId, operationId, effect: 'NO_CHANGE', etag: oldEtag }))
    await vm.openName(vm.result.items[0]); await vm.saveName(); expect(vm.notice).toContain('行バージョンに変更はありません')
  })
  it('persists only a scoped locator before sending and prevents duplicate keys until original resolution', async () => {
    const vm = await render(); const wait = deferred(); api.rename.mockReturnValueOnce(wait.promise); await vm.openName(vm.result.items[0]); vm.name = 'secret-name'; const send = vm.saveName()
    const saved = sessionStorage.getItem(key('warehouses'))!; expect(saved).not.toContain('secret-name'); expect(saved).not.toContain('Original'); expect(JSON.parse(saved).actorId).toBe(actorId)
    await vm.saveName(); expect(api.rename).toHaveBeenCalledTimes(1); wait.reject(new Error('lost ack')); await send
    const op = vm.pending.operationId; await vm.recover(false); expect(vm.pending.operationId).toBe(op); expect(vm.notice).toContain('未実行とは判断できません')
    await vm.openName(vm.result.items[0]); await vm.saveName(); expect(api.rename).toHaveBeenCalledTimes(1)
    view!.unmount(); view = undefined; const restored = await render(); expect(restored.pending.operationId).toBe(op)
    await restored.recover(true); expect(api.close.mock.lastCall).toEqual(['warehouses', wid, op]); expect(restored.pending).toBeNull()
  })
  it('recovers committed originals after edit permission is removed without reapplying the command', async () => {
    const vm = await render(); api.rename.mockRejectedValueOnce(new Error('lost')); await vm.openName(vm.result.items[0]); vm.name = 'New'; await vm.saveName(); const op = vm.pending.operationId
    shared.permission.actions.delete('wms-warehouse:edit'); expect(vm.result).toBeNull(); await vm.initialize()
    api.operation.mockResolvedValueOnce(wrap({ kind: 'warehouses', objectId: wid, operationId: op, state: 'COMMITTED', result: { kind: 'warehouses', objectId: wid, operationId: op, effect: 'CHANGED', etag: nextEtag } }))
    await vm.recover(false); expect(vm.pending).toBeNull(); expect(api.rename).toHaveBeenCalledTimes(1)
  })
  it.each(['wms-location:query', 'wms-warehouse:query'])('clears late reads on %s revocation and restores only after explicit refresh', async action => {
    const vm = await render('locations'); const wait = deferred(); api.locations.mockReturnValueOnce(wait.promise); const load = vm.load(2)
    shared.permission.actions.delete(action); expect(vm.result).toBeNull(); expect(vm.choices).toBeNull(); wait.resolve(wrap(page(location, { page: 2, pageSize: 50 }))); await load; expect(vm.result).toBeNull()
    await vm.saveName(); expect(api.rename).not.toHaveBeenCalled()
  })
  it('isolates late writes and pending locators across identity changes', async () => {
    const vm = await render(); const wait = deferred(); api.rename.mockReturnValueOnce(wait.promise); await vm.openName(vm.result.items[0]); vm.name = 'Secret'; const send = vm.saveName(); const p = { ...vm.pending }
    identity(); expect(vm.result).toBeNull(); expect(vm.editing).toBeNull(); expect(vm.pending).toBeNull(); expect(vm.scope).toBeNull()
    api.scope.mockResolvedValue(wrap({ tenantId, actorId: '20000000-0000-0000-0000-000000000002' })); await vm.initialize(); expect(vm.pending).toBeNull()
    wait.resolve(wrap({ ...p, effect: 'CHANGED', etag: nextEtag })); await send; expect(sessionStorage.getItem(key('warehouses'))).not.toBeNull(); expect(vm.notice).toBe('')
  })
  it('rejects wrong result locators and preserves UNKNOWN until verified terminal evidence', async () => {
    const vm = await render(); api.rename.mockResolvedValueOnce(wrap({ kind: 'warehouses', objectId: lid, operationId: crypto.randomUUID(), effect: 'CHANGED', etag: nextEtag }))
    await vm.openName(vm.result.items[0]); await vm.saveName(); expect(vm.pending).not.toBeNull()
    api.operation.mockResolvedValueOnce(wrap({ ...vm.pending, state: 'CLOSED', result: { fake: true } })); await vm.recover(false); expect(vm.pending).not.toBeNull()
    shared.permission.actions.delete('wms-warehouse:abandon'); await vm.initialize(); await vm.recover(true); expect(api.close).not.toHaveBeenCalled()
  })
  it('fails closed before sending when storage is unavailable or a saved locator is corrupt', async () => {
    const vm = await render(); await vm.openName(vm.result.items[0]); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') }); await vm.saveName(); expect(api.rename).not.toHaveBeenCalled(); expect(vm.storageBlocked).toBe(true)
    vi.restoreAllMocks(); sessionStorage.setItem(key('warehouses'), '{bad'); await vm.initialize(); expect(vm.storageBlocked).toBe(true)
  })
  it('clears filters immediately and discards late page replies, rounded quantities and private values', async () => {
    const vm = await render('locations'); const wait = deferred(); api.locations.mockReturnValueOnce(wait.promise); const old = vm.load(2); vm.queryText = 'NEW'; expect(vm.result).toBeNull()
    await vm.search(); wait.resolve(wrap(page(location, { page: 2, pageSize: 50 }))); await old; expect(vm.result.page).toBe(1)
    for (const change of [{ rawCapacityQty: 0 }, { parentLocationCd: 'hidden' }, { barcode: 'hidden' }, { typedCapacity: '0' }, { warehouseId: lid }]) { api.locations.mockResolvedValueOnce(wrap(page({ ...location, ...change }, { page: 1, pageSize: 50 }))); await vm.load(1); expect(vm.result).toBeNull() }
  })
  it('protects unsaved drafts and rejects late confirmation after identity invalidation', async () => {
    const vm = await render(); await vm.openName(vm.result.items[0]); vm.name = 'Draft'; shared.confirm.mockRejectedValueOnce('cancel'); expect(await shared.leave()).toBe(false); expect(vm.editing).not.toBeNull()
    const wait = deferred(); shared.confirm.mockReturnValueOnce(wait.promise); const open = vm.openName(vm.result.items[0]); identity(); wait.resolve('confirm'); await open; expect(vm.editing).toBeNull()
  })
  it('ignores unmounted responses while retaining an unresolved operation locator', async () => {
    const vm = await render(); const wait = deferred(); api.rename.mockReturnValueOnce(wait.promise); await vm.openName(vm.result.items[0]); const sending = vm.saveName(); const p = { ...vm.pending }; view!.unmount(); view = undefined
    wait.resolve(wrap({ ...p, effect: 'CHANGED', etag: nextEtag })); await sending; expect(sessionStorage.getItem(key('warehouses'))).not.toBeNull(); expect(vm.result).toBeNull()
  })
  it('routing warehouse choices use bounded current search and clear selection on revoked authority', async () => {
    const clear = vi.fn(); view = shallowMount(defineComponent({ setup: () => useWarehouseOwnerChoices(clear), template: '<div />' })); const vm: any = view.vm
    await vm.loadWarehouses('W'); expect(api.warehouses.mock.lastCall?.[0]).toEqual({ warehouseCd: 'W', page: 1, pageSize: 200 }); expect(vm.total).toBe(501)
    const wait = deferred(); api.warehouses.mockReturnValueOnce(wait.promise); const old = vm.loadWarehouses('OLD'); await vm.loadWarehouses('NEW'); wait.resolve(wrap(page(warehouse, { page: 1, pageSize: 200 }))); await old
    shared.permission.actions.delete('wms-warehouse:query'); expect(vm.warehouses).toEqual([]); expect(clear).toHaveBeenCalled(); await vm.loadWarehouses(); expect(vm.warehouses).toEqual([])
  })
})
