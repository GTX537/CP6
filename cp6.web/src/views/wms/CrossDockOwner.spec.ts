import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ searchOwner: vi.fn(), getOwner: vi.fn(), create: vi.fn(), execute: vi.fn(), cancel: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/logistics', () => ({ crossDockApi: api }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue')
  const state = reactive({ loaded: true, actions: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, has: (key: string) => state.actions.has(key) }) }
})
import View from './CrossDockView.vue'
import { crossDockVersion } from '@/types/wms/crossDockOwner'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'

let wrapper: ReturnType<typeof shallowMount> | undefined
const row = { version: crossDockVersion, knowledge: 'UNKNOWN', sourceState: 'UNRESOLVED_SOURCE', id: 'native-id', xDockNo: 'XD1',
  productCd: 'MAT', qty: '9999999999999.99999999', lotNo: '', legacyStatus: 0, inTxnNo: 'OLD-IN', outTxnNo: null, remarksReadable: false, remarks: null }
const page = { version: crossDockVersion, coverage: 'AUTHORIZED_LEGACY_OBSERVATION', writesAvailable: false,
  writeBlocker: 'CROSS_DOCK_OWNER_ADOPTION_UNPROVEN', total: 503, page: 1, pageSize: 50, items: [row] }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function render(): any {
  wrapper = shallowMount(View, { global: { config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } })
  return wrapper.vm
}
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true
  shared.permission.actions = new Set(['wms-cross-dock:query', 'wms-inbound-order:query', 'wms-outbound-order:query'])
  for (const mock of Object.values(api)) mock.mockReset()
  api.searchOwner.mockImplementation(async q => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize } }))
  api.getOwner.mockResolvedValue({ code: 0, data: row })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })

describe('cross-dock historical current Owner', () => {
  it('preserves exact recorded quantities and real server totals beyond the old cap', async () => {
    const vm = render(); await flushPromises(); expect(vm.result.total).toBe(503); expect(vm.result.items[0].qty).toBe('9999999999999.99999999')
    vm.goPage(11); await flushPromises(); expect(api.searchOwner.mock.lastCall?.[0]).toMatchObject({ page: 11, pageSize: 50 }); expect(vm.result.page).toBe(11)
    expect(vm.result.writesAvailable).toBe(false); expect(api.create).not.toHaveBeenCalled(); expect(api.execute).not.toHaveBeenCalled(); expect(api.cancel).not.toHaveBeenCalled()
  })
  it('uses recorded legacy facts without promoting them to confirmed receipts or completion', async () => {
    const vm = render(); await flushPromises(); await vm.openDetail(row)
    expect(vm.detail.inTxnNo).toBe('OLD-IN'); expect(vm.detail.outTxnNo).toBeNull(); expect(vm.detail.knowledge).toBe('UNKNOWN')
    expect(vm.detail.lotNo).toBe(''); expect(vm.detail.remarks).toBeNull(); expect(vm.statusLabel(1)).toBe('历史执行标记')
  })
  it('requires a current entry action and clears on loaded or source permission changes', async () => {
    shared.permission.actions.delete('wms-cross-dock:query'); const vm = render(); await flushPromises(); expect(api.searchOwner).not.toHaveBeenCalled()
    shared.permission.actions.add('wms-cross-dock:query'); await vm.load({ ...vm.query }); await vm.openDetail(row)
    shared.permission.actions.delete('wms-inbound-order:query'); expect(vm.result).toBeNull(); expect(vm.detail).toBeNull()
    await vm.load({ ...vm.query }); expect(vm.result).not.toBeNull() // Unlinked history remains a server-scoped observation.
    shared.permission.loaded = false; expect(vm.result).toBeNull()
  })
  it('clears on identity change and ignores an old identity success', async () => {
    const vm = render(); await flushPromises(); await vm.openDetail(row); identity(); expect(vm.result).toBeNull(); expect(vm.detail).toBeNull()
    const deferred = pending(); api.searchOwner.mockReturnValueOnce(deferred.promise); const read = vm.load({ ...vm.query })
    identity(); deferred.resolve({ code: 0, data: page }); await read; expect(vm.result).toBeNull()
  })
  it('keeps a newer denied list empty when an older success arrives', async () => {
    const vm = render(); await flushPromises(); const deferred = pending(); api.searchOwner.mockReturnValueOnce(deferred.promise); const old = vm.load({ ...vm.query })
    api.searchOwner.mockRejectedValueOnce({ response: { status: 403 } }); await vm.load({ ...vm.query })
    deferred.resolve({ code: 0, data: page }); await old; expect(vm.result).toBeNull(); expect(vm.error).not.toBe(''); expect(vm.loading).toBe(false)
  })
  it('clears list and detail as soon as the visible filter changes', async () => {
    const vm = render(); await flushPromises(); const deferred = pending(); api.getOwner.mockReturnValueOnce(deferred.promise); const old = vm.openDetail(row)
    vm.query.productCd = 'OTHER'; expect(vm.result).toBeNull(); expect(vm.detailLoading).toBe(false)
    deferred.resolve({ code: 0, data: row }); await old; expect(vm.detail).toBeNull()
  })
  it('matches detail to the actually selected record and prevents older selections from overwriting', async () => {
    const next = { ...row, id: 'next-id', xDockNo: 'XD2' }
    api.searchOwner.mockResolvedValueOnce({ code: 0, data: { ...page, items: [row, next] } }); const vm = render(); await flushPromises()
    const deferred = pending(); api.getOwner.mockReturnValueOnce(deferred.promise); const old = vm.openDetail(row)
    api.getOwner.mockResolvedValueOnce({ code: 0, data: next }); await vm.openDetail(next)
    deferred.resolve({ code: 0, data: row }); await old; expect(vm.detail.xDockNo).toBe('XD2')
    api.getOwner.mockResolvedValueOnce({ code: 0, data: row }); await vm.openDetail(next); expect(vm.result).toBeNull(); expect(vm.detail).toBeNull()
  })
  it('rejects legacy or malformed observations and never enables writes from a response', async () => {
    const vm = render(); await flushPromises()
    for (const changed of [{ version: 'legacy' }, { coverage: 'COMPLETE' }, { writesAvailable: true }, { page: 9 }, { items: [{ ...row, qty: 0 }] }]) {
      api.searchOwner.mockResolvedValueOnce({ code: 0, data: { ...page, ...changed } }); await vm.load({ ...vm.query }); expect(vm.result).toBeNull()
    }
    expect(api.create).not.toHaveBeenCalled(); expect(api.execute).not.toHaveBeenCalled(); expect(api.cancel).not.toHaveBeenCalled()
  })
  it('normalizes a cleared status consistently across a search and later pages', async () => {
    const vm = render(); await flushPromises(); vm.query.status = ''; vm.search(); await flushPromises(); expect(api.searchOwner.mock.lastCall?.[0].status).toBeUndefined()
    vm.goPage(2); await flushPromises(); expect(api.searchOwner.mock.lastCall?.[0].status).toBeUndefined(); expect(vm.result.page).toBe(2)
  })
  it('drops detail responses after unmount without issuing any command', async () => {
    const vm = render(); await flushPromises(); const deferred = pending(); api.getOwner.mockReturnValueOnce(deferred.promise); const old = vm.openDetail(row)
    wrapper!.unmount(); wrapper = undefined; deferred.resolve({ code: 0, data: row }); await old; expect(vm.detail).toBeNull()
    expect(api.execute).not.toHaveBeenCalled()
  })
})
