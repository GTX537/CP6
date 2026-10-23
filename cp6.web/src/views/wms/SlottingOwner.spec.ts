import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ searchOwner: vi.fn(), getOwner: vi.fn(), analyze: vi.fn(), approve: vi.fn(), cancel: vi.fn() }))
const shared = vi.hoisted(() => ({ permission: undefined as any }))
vi.mock('@/api/wms/logistics', () => ({ slottingApi: api }))
vi.mock('@/stores/permission', async () => {
  const { reactive } = await import('vue')
  const state = reactive({ loaded: true, actions: new Set<string>() }); shared.permission = state
  return { usePermissionStore: () => ({ get loaded() { return state.loaded }, has: (key: string) => state.actions.has(key) }) }
})
import View from './SlottingView.vue'
import { slottingVersion } from '@/types/wms/slottingOwner'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'

let wrapper: ReturnType<typeof shallowMount> | undefined
const original = '[ { "OutQty": 9999999999999.99999999, "MobileTaskNo": "OLD-REF" } ]'
const row = { version: slottingVersion, knowledge: 'UNKNOWN', statisticsMethod: 'LEGACY_ROW_COUNT', id: 'native-id', slottingPlanNo: 'SLP1',
  warehouseCd: 'WH', legacyStatus: 2, snapshotReadable: true, snapshotState: 'AUTHORIZED_LEGACY_SNAPSHOT', recommendationsJson: original,
  txnSampleCount: 18, recommendationCount: 1, jsonState: 'JSON_ARRAY', remarksReadable: false, remarks: null }
const page = { version: slottingVersion, coverage: 'AUTHORIZED_LEGACY_OBSERVATION', writesAvailable: false,
  writeBlocker: 'SLOTTING_OWNER_ADOPTION_UNPROVEN', total: 203, page: 1, pageSize: 50, items: [row] }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function render(): any {
  wrapper = shallowMount(View, { global: { config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } })
  return wrapper.vm
}
beforeEach(() => {
  localStorage.clear(); shared.permission.loaded = true
  shared.permission.actions = new Set(['wms-slotting:query', 'wms-stock:query', 'wms-mobile:query', 'wms-outbound-order:query'])
  for (const mock of Object.values(api)) mock.mockReset()
  api.searchOwner.mockImplementation(async q => ({ code: 0, data: { ...page, page: q.page, pageSize: q.pageSize } }))
  api.getOwner.mockResolvedValue({ code: 0, data: row })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })

describe('slotting historical current Owner', () => {
  it('preserves exact recorded quantities and real server totals beyond the old cap', async () => {
    const vm = render(); await flushPromises(); expect(vm.result.total).toBe(203); expect(vm.result.items[0].recommendationsJson).toBe(original)
    vm.goPage(5); await flushPromises(); expect(api.searchOwner.mock.lastCall?.[0]).toMatchObject({ page: 5, pageSize: 50 }); expect(vm.result.page).toBe(5)
    expect(vm.result.writesAvailable).toBe(false); expect(api.analyze).not.toHaveBeenCalled(); expect(api.approve).not.toHaveBeenCalled(); expect(api.cancel).not.toHaveBeenCalled()
  })
  it('preserves bad original text and never turns a hidden or unknown snapshot into empty success', async () => {
    const vm = render(); await flushPromises(); api.getOwner.mockResolvedValueOnce({ code: 0, data: { ...row, recommendationsJson: '{broken 9999999999999.99999999', jsonState: 'LEGACY_PARSE_ERROR' } })
    await vm.openDetail(row); expect(vm.detail.recommendationsJson).toBe('{broken 9999999999999.99999999'); expect(vm.detail.jsonState).toBe('LEGACY_PARSE_ERROR')
    expect(vm.detail.knowledge).toBe('UNKNOWN'); expect(vm.statusLabel(2)).toBe('历史批准标记')
    api.getOwner.mockResolvedValueOnce({ code: 0, data: { ...row, snapshotReadable: false, snapshotState: 'SOURCE_DISCLOSURE_UNAVAILABLE', recommendationsJson: null, txnSampleCount: null, recommendationCount: null, jsonState: 'NOT_DISCLOSED' } })
    await vm.openDetail(row); expect(vm.detail.recommendationsJson).toBeNull(); expect(vm.detail.txnSampleCount).toBeNull()
  })
  it('requires a current entry action and clears on loaded or source permission changes', async () => {
    shared.permission.actions.delete('wms-slotting:query'); const vm = render(); await flushPromises(); expect(api.searchOwner).not.toHaveBeenCalled()
    shared.permission.actions.add('wms-slotting:query'); await vm.load({ ...vm.query }); await vm.openDetail(row)
    shared.permission.actions.delete('wms-stock:query'); expect(vm.result).toBeNull(); expect(vm.detail).toBeNull()
    await vm.load({ ...vm.query }); expect(vm.result).not.toBeNull() // A restricted snapshot may still expose its separately authorized plan header.
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
    vm.query.warehouseCd = 'OTHER'; expect(vm.result).toBeNull(); expect(vm.detailLoading).toBe(false)
    deferred.resolve({ code: 0, data: row }); await old; expect(vm.detail).toBeNull()
  })
  it('matches detail to the actually selected record and prevents older selections from overwriting', async () => {
    const next = { ...row, id: 'next-id', slottingPlanNo: 'SLP2' }
    api.searchOwner.mockResolvedValueOnce({ code: 0, data: { ...page, items: [row, next] } }); const vm = render(); await flushPromises()
    const deferred = pending(); api.getOwner.mockReturnValueOnce(deferred.promise); const old = vm.openDetail(row)
    api.getOwner.mockResolvedValueOnce({ code: 0, data: next }); await vm.openDetail(next)
    deferred.resolve({ code: 0, data: row }); await old; expect(vm.detail.slottingPlanNo).toBe('SLP2')
    api.getOwner.mockResolvedValueOnce({ code: 0, data: row }); await vm.openDetail(next); expect(vm.result).toBeNull(); expect(vm.detail).toBeNull()
  })
  it('rejects legacy or malformed observations and never enables writes from a response', async () => {
    const vm = render(); await flushPromises()
    for (const changed of [{ version: 'legacy' }, { coverage: 'COMPLETE' }, { writesAvailable: true }, { page: 9 }, { items: [{ ...row, recommendationsJson: 0 }] }, { items: [{ ...row, snapshotReadable: false }] }]) {
      api.searchOwner.mockResolvedValueOnce({ code: 0, data: { ...page, ...changed } }); await vm.load({ ...vm.query }); expect(vm.result).toBeNull()
    }
    expect(api.analyze).not.toHaveBeenCalled(); expect(api.approve).not.toHaveBeenCalled(); expect(api.cancel).not.toHaveBeenCalled()
  })
  it('normalizes a cleared status consistently across a search and later pages', async () => {
    const vm = render(); await flushPromises(); vm.query.status = ''; vm.search(); await flushPromises(); expect(api.searchOwner.mock.lastCall?.[0].status).toBeUndefined()
    vm.goPage(2); await flushPromises(); expect(api.searchOwner.mock.lastCall?.[0].status).toBeUndefined(); expect(vm.result.page).toBe(2)
  })
  it('drops detail responses after unmount without issuing any command', async () => {
    const vm = render(); await flushPromises(); const deferred = pending(); api.getOwner.mockReturnValueOnce(deferred.promise); const old = vm.openDetail(row)
    wrapper!.unmount(); wrapper = undefined; deferred.resolve({ code: 0, data: row }); await old; expect(vm.detail).toBeNull()
    expect(api.approve).not.toHaveBeenCalled()
  })
})
