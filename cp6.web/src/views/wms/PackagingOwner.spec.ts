import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), queue: vi.fn(), order: vi.fn(), ship: vi.fn(), confirm: vi.fn(), success: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/packagingOwner', () => ({ packagingOwnerApi: { search: api.search, get: api.get } }))
vi.mock('@/api/wms/outboundOrder', () => ({ outboundOrderApi: { search: api.queue, get: api.order, ship: api.ship } }))
vi.mock('vue-i18n', async original => ({ ...await original<any>(), useI18n: () => ({ t: (x: string) => x }) }))
vi.mock('element-plus', () => ({ ElMessageBox: { confirm: api.confirm }, ElMessage: { success: api.success } }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue'); const state = reactive({ loaded: true, actionKeys: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, get actionKeys() { return state.actionKeys }, has: (key: string) => state.actionKeys.has(key) }) }
})
import View from './PackingShipView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const parent = '20000000-0000-0000-0000-000000000001'
const row = { id: '10000000-0000-0000-0000-000000000001', packageNo: 'PKG1', outboundId: parent, outboundNo: 'OUT1', caseQty: 3,
  totalWeightKg: '1234567.891', totalVolumeM3: '123456.7891', departureTime: '2026-10-01T09:30:00', carrierCd: 'Carrier ', trackingNo: 'Actual ', remarks: 'Private',
  carrierReadable: true, trackingReadable: true, remarksReadable: true, recordKind: 'LEGACY_SHIPPING_PACKAGE', formalPackageState: 'UNPROVEN', deliveryProofState: 'NOT_RECORDED', timeBasis: 'LEGACY_UNSPECIFIED' }
const order = (number = 'OUT1') => ({ outboundNo: number, outboundType: 2, status: 3, carrierCd: 'OTHER', details: [{ lineNo: 1 }] })
const wrap = (data: any) => ({ code: 0, data })
function page(q: any, total = 63) {
  return { version: 'PACKAGING-OWNER/1', coverage: 'CURRENT_AUTHORIZED_RECORDS', page: q.page, pageSize: q.pageSize, total,
    items: Array.from({ length: Math.max(0, Math.min(q.pageSize, total - (q.page - 1) * q.pageSize)) }, (_, i) => {
      const n = (q.page - 1) * q.pageSize + i + 1; return { ...row, id: `10000000-0000-0000-0000-${String(n).padStart(12, '0')}`, packageNo: 'PKG' + n }
    }) }
}
function deferred() { let resolve!: (v: any) => void, reject!: (v: any) => void; const promise = new Promise<any>((r, j) => { resolve = r; reject = j }); return { promise, resolve, reject } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
let view: ReturnType<typeof shallowMount> | undefined
async function render(): Promise<any> { view = shallowMount(View, { global: { config: { warnHandler: () => {} }, directives: { permission: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); await flushPromises(); return view.vm }
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true; shared.permission.actionKeys = new Set(['wms-packaging:query', 'wms-outbound-order:query', 'wms-outbound-order:ship'])
  for (const m of Object.values(api)) m.mockReset()
  api.search.mockImplementation(async q => wrap(page(q))); api.get.mockImplementation(async number => wrap({ ...row, packageNo: number }))
  api.queue.mockResolvedValue(wrap([order(), order('OUT2')])); api.order.mockImplementation(async number => wrap(order(number)))
  api.confirm.mockResolvedValue('confirm'); api.ship.mockResolvedValue(wrap({ packageNo: 'PKG-SHIPPED' }))
})
afterEach(() => { view?.unmount(); view = undefined; vi.restoreAllMocks() })
describe('Packaging original Owner observations and shipment UI isolation', () => {
  it('pages the authorized records, preserves decimals and reads the exact original package', async () => {
    const vm = await render(); expect(vm.historyResult.total).toBe(63); expect(vm.historyResult.items[0].totalWeightKg).toBe('1234567.891')
    await vm.reloadHistory(3); expect(vm.historyResult.items).toHaveLength(3); expect(api.search.mock.lastCall?.[0]).toEqual({ page: 3, pageSize: 30, packageNo: undefined })
    await vm.reloadHistory(1); await vm.loadPackage(vm.historyResult.items[0]); expect(vm.packageDetail.trackingNo).toBe('Actual ')
    expect(vm.genTracking).toBeUndefined(); await vm.loadOrder(vm.queue[0]); expect(vm.shipForm.trackingNo).toBe('')
  })
  it.each(['wms-packaging:query', 'wms-outbound-order:query'])('requires %s and ignores a response after revocation', async key => {
    shared.permission.actionKeys.delete(key); const vm = await render(); expect(api.search).not.toHaveBeenCalled()
    shared.permission.actionKeys.add(key); const wait = deferred(); api.search.mockReturnValueOnce(wait.promise); const pending = vm.reloadHistory()
    shared.permission.actionKeys.delete(key); wait.resolve(wrap(page({ page: 1, pageSize: 30 }))); await pending; expect(vm.historyResult).toBeNull()
  })
  it('isolates late queue, original order and package responses on identity change', async () => {
    const vm = await render(), wait = deferred(); api.order.mockReturnValueOnce(wait.promise); const pending = vm.loadOrder(vm.queue[0]); identity()
    wait.resolve(wrap(order())); await pending; expect(vm.current).toBeNull(); expect(vm.historyResult).toBeNull(); expect(vm.queue).toEqual([])
    const read = deferred(); api.search.mockReturnValueOnce(read.promise); const history = vm.reloadHistory(); identity(); read.reject(new Error('old subject')); await history; expect(vm.historyError).toBe('')
  })
  it('replaces earlier order selections only with the most recently requested original order', async () => {
    const vm = await render(), wait = deferred(); api.order.mockReturnValueOnce(wait.promise); const pending = vm.loadOrder(vm.queue[0])
    await vm.loadOrder(vm.queue[1]); wait.resolve(wrap(order())); await pending; expect(vm.current.outboundNo).toBe('OUT2')
    api.order.mockResolvedValueOnce(wrap(order('MISMATCH'))); await vm.loadOrder(vm.queue[0]); expect(vm.current).toBeNull(); expect(vm.queueError).not.toBe('')
  })
  it('invalidates previous filter/detail responses and refuses malformed or over-disclosed observations', async () => {
    const vm = await render(), wait = deferred(); api.get.mockReturnValueOnce(wait.promise); const pending = vm.loadPackage(vm.historyResult.items[0]); vm.packageFilter = 'NEW'
    wait.resolve(wrap(row)); await pending; expect(vm.packageDetail).toBeNull(); expect(vm.historyResult).toBeNull()
    vm.packageFilter = ''; const valid = page({ page: 1, pageSize: 30 }); valid.items[0] = { ...row, trackingReadable: false }
    api.search.mockResolvedValueOnce(wrap(valid)); await vm.reloadHistory(); expect(vm.historyResult).toBeNull(); expect(vm.historyError).not.toBe('')
    api.search.mockResolvedValueOnce(wrap({ ...page({ page: 1, pageSize: 30 }), total: 1 })); await vm.reloadHistory(); expect(vm.historyResult).toBeNull()
  })
  it('allows hidden null fields while keeping them distinct from recorded empty values', async () => {
    const vm = await render(); api.get.mockResolvedValueOnce(wrap({ ...row, trackingReadable: false, trackingNo: null, remarks: '' }))
    await vm.loadPackage(vm.historyResult.items[0]); expect(vm.packageDetail.trackingReadable).toBe(false); expect(vm.packageDetail.remarks).toBe('')
  })
  it('freezes the confirmed original request and allows only one pending confirmation or send', async () => {
    const vm = await render(); await vm.loadOrder(vm.queue[0]); vm.shipForm.trackingNo = 'Real-1'; const confirm = deferred(); api.confirm.mockReturnValueOnce(confirm.promise)
    const pending = vm.onShip(); await vm.onShip(); expect(api.confirm).toHaveBeenCalledTimes(1); vm.shipForm.trackingNo = 'Changed-after-confirm'; await vm.loadOrder(vm.queue[1]); expect(vm.current.outboundNo).toBe('OUT1')
    confirm.resolve('confirm'); await pending; expect(api.ship).toHaveBeenCalledExactlyOnceWith('OUT1', expect.objectContaining({ trackingNo: 'Real-1' })); expect(api.success).toHaveBeenCalledTimes(1)
  })
  it.each(['identity', 'permission', 'selected'])('does not send after %s changes during confirmation', async change => {
    const vm = await render(); await vm.loadOrder(vm.queue[0]); const confirm = deferred(); api.confirm.mockReturnValueOnce(confirm.promise); const pending = vm.onShip()
    if (change === 'identity') identity(); else if (change === 'permission') shared.permission.actionKeys.delete('wms-outbound-order:ship'); else vm.current = order('OUT2')
    confirm.resolve('confirm'); await pending; expect(api.ship).not.toHaveBeenCalled()
  })
  it('keeps an unknown send blocked through list refresh without guessing success or resending', async () => {
    const vm = await render(); await vm.loadOrder(vm.queue[0]); api.ship.mockRejectedValueOnce(new Error('network lost')); await vm.onShip(); expect(vm.intent.uncertain.value).toBe(true)
    await vm.onShip(); await vm.reloadQueue(); await vm.loadOrder(vm.queue[0]); await vm.onShip(); expect(api.ship).toHaveBeenCalledTimes(1); expect(api.success).not.toHaveBeenCalled(); expect(vm.intent.error.value).toContain('再送せず')
    shared.permission.actionKeys = new Set(shared.permission.actionKeys); expect(vm.intent.uncertain.value).toBe(true); await vm.reloadQueue(); await vm.loadOrder(vm.queue[0]); await vm.onShip(); expect(api.ship).toHaveBeenCalledTimes(1)
  })
  it('discards an old identity send result and all reads on catalog refresh or unmount', async () => {
    const vm = await render(); await vm.loadOrder(vm.queue[0]); const send = deferred(); api.ship.mockReturnValueOnce(send.promise); const pending = vm.onShip(); await flushPromises(); identity()
    send.resolve(wrap({ packageNo: 'OLD' })); await pending; expect(api.success).not.toHaveBeenCalled(); expect(vm.current).toBeNull()
    await vm.reloadHistory(); shared.permission.actionKeys = new Set(shared.permission.actionKeys); expect(vm.historyResult).toBeNull()
    const read = deferred(); api.search.mockReturnValueOnce(read.promise); const history = vm.reloadHistory(); view!.unmount(); view = undefined; read.resolve(wrap(page({ page: 1, pageSize: 30 }))); await history; expect(vm.historyResult).toBeNull()
  })
  it('keeps an in-flight send unresolved after the same subject refreshes the permission catalog', async () => {
    const vm = await render(); await vm.loadOrder(vm.queue[0]); const send = deferred(); api.ship.mockReturnValueOnce(send.promise); const pending = vm.onShip(); await flushPromises()
    shared.permission.actionKeys = new Set(shared.permission.actionKeys); expect(vm.intent.uncertain.value).toBe(true)
    send.resolve(wrap({ packageNo: 'ORIGINAL' })); await pending; expect(api.success).not.toHaveBeenCalled(); expect(vm.intent.uncertain.value).toBe(true)
    await vm.reloadQueue(); await vm.loadOrder(vm.queue[0]); await vm.onShip(); expect(api.ship).toHaveBeenCalledTimes(1)
  })
  it('permits another confirmation after cancellation without a shipment call', async () => {
    const vm = await render(); await vm.loadOrder(vm.queue[0]); api.confirm.mockRejectedValueOnce('cancel'); await vm.onShip()
    expect(api.ship).not.toHaveBeenCalled(); expect(vm.intent.uncertain.value).toBe(false); expect(vm.saving).toBe(false)
    await vm.onShip(); expect(api.ship).toHaveBeenCalledTimes(1)
  })
})
