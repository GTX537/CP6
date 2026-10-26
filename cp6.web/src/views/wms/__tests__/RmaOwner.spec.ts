import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ page: vi.fn(), search: vi.fn(), get: vi.fn(), capabilities: vi.fn(), create: vi.fn(), receive: vi.fn(), startInspection: vi.fn(), judge: vi.fn(), close: vi.fn(), cancel: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), confirm: vi.fn() }))
const permission = vi.hoisted(() => ({ loaded: true, has: vi.fn(() => true) }))
vi.mock('@/api/wms/rma', () => ({ rmaApi: api }))
vi.mock('element-plus', () => ({ ElMessage: ui, ElMessageBox: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permission }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from '../RmaView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
const row = { rmaNo: 'RMA-1', customerCd: 'C1', customerName: 'private', warehouseCd: 'W1', status: 1, details: [{ lineNo: 1, qty: 1, productCd: 'P1', judgement: 'RESELL' }] }
const query = { page: 1, size: 50, filters: {} }
function render(): any { wrapper = shallowMount(View, { global: { directives: { permission: {}, loading: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return wrapper.vm }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permission.loaded = true; permission.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.capabilities.mockResolvedValue({ code: 0, data: { writesAvailable: false, status: 'RMA_OWNER_ADOPTION_UNPROVEN' } })
  api.page.mockResolvedValue({ code: 0, data: { items: [structuredClone(row)], total: 1, page: 1, pageSize: 50 } }); api.get.mockResolvedValue({ code: 0, data: structuredClone(row) })
  ui.confirm.mockResolvedValue(undefined)
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('RMA current Owner page', () => {
  it('keeps every unadopted command unavailable even when called directly', async () => {
    const vm = render(); await vm.fetchList(query); await vm.openDetail('RMA-1')
    expect(vm.canReceive).toBe(false); vm.openCreate(); expect(vm.current.rmaNo).toBe('RMA-1')
    await vm.onSave(); await vm.onReceive(); await vm.onStartInsp(); await vm.onJudge(); await vm.onClose(); await vm.onCancel()
    for (const name of ['create', 'receive', 'startInspection', 'judge', 'close', 'cancel'] as const) expect(api[name]).not.toHaveBeenCalled()
    expect(ui.success).not.toHaveBeenCalled()
  })
  it('rejects unavailable capability or list responses and clears protected detail', async () => {
    const vm = render(); await vm.fetchList(query); await vm.openDetail('RMA-1')
    api.capabilities.mockRejectedValueOnce({ response: { status: 503 } })
    await expect(vm.fetchList(query)).rejects.toThrow(); expect(vm.current).toBeNull(); expect(vm.readError).not.toBe('')
    api.page.mockRejectedValueOnce({ response: { status: 403 } })
    await expect(vm.fetchList(query)).rejects.toThrow(); expect(vm.writesAvailable).toBe(false)
  })
  it('clears identity data and ignores an old delayed detail response', async () => {
    const vm = render(); await vm.fetchList(query); const response = pending(); api.get.mockReturnValueOnce(response.promise)
    const old = vm.openDetail('RMA-1'); await flushPromises(); expect(api.get).toHaveBeenCalledTimes(1)
    identity(); response.resolve({ code: 0, data: row }); await old
    expect(vm.current).toBeNull(); expect(vm.writesAvailable).toBe(false)
  })
  it('does not accept an older list after a later denial', async () => {
    const vm = render(); const response = pending(); api.capabilities.mockReturnValueOnce(response.promise)
    const old = vm.fetchList(query); const observed = expect(old).rejects.toThrow()
    api.capabilities.mockRejectedValueOnce({ response: { status: 403 } }); await expect(vm.fetchList(query)).rejects.toThrow()
    response.resolve({ code: 0, data: { writesAvailable: true, status: 'LEGACY' } }); await observed
    expect(vm.writesAvailable).toBe(false); expect(vm.current).toBeNull()
  })
  it('does not treat a successful HTTP envelope with missing data as an empty authorized list', async () => {
    const vm = render(); api.page.mockResolvedValueOnce({ code: 0, data: null })
    await expect(vm.fetchList(query)).rejects.toThrow(); expect(vm.readError).not.toBe('')
  })
  it('honors missing query action and rechecks identity after legacy confirmation', async () => {
    const vm = render(); permission.has.mockReturnValue(false); await expect(vm.fetchList(query)).rejects.toThrow()
    expect(api.page).not.toHaveBeenCalled(); permission.has.mockReturnValue(true)
    api.capabilities.mockResolvedValue({ code: 0, data: { writesAvailable: true, status: 'LEGACY' } })
    await vm.fetchList(query); await vm.openDetail('RMA-1'); const answer = pending(); ui.confirm.mockReturnValueOnce(answer.promise)
    const sending = vm.onReceive(); identity(); answer.resolve(undefined); await sending; expect(api.receive).not.toHaveBeenCalled()
  })
  it('blocks duplicate and uncertain legacy writes without reporting stale success', async () => {
    const vm = render(); api.capabilities.mockResolvedValue({ code: 0, data: { writesAvailable: true, status: 'LEGACY' } })
    await vm.fetchList(query); await vm.openDetail('RMA-1'); const answer = pending(); api.receive.mockReturnValueOnce(answer.promise)
    const sending = vm.onReceive(); await flushPromises(); await vm.onReceive(); expect(api.receive).toHaveBeenCalledTimes(1)
    identity(); answer.resolve({ code: 0 }); await sending; expect(ui.success).not.toHaveBeenCalled()
    await vm.fetchList(query); await vm.openDetail('RMA-1'); api.receive.mockRejectedValueOnce({ response: { status: 503 } })
    await vm.onReceive(); await vm.fetchList(query); await vm.openDetail('RMA-1'); await vm.onReceive()
    expect(api.receive).toHaveBeenCalledTimes(2); expect(vm.intent.uncertain.value).toBe(true)
  })
  it('loads a server page beyond 500 with the authorized total and status zero', async () => {
    const vm = render()
    api.page.mockResolvedValue({ code: 0, data: { items: [row], total: 501, page: 11, pageSize: 50 } })
    const result = await vm.fetchList({ page: 11, size: 50, filters: { status: 0 } })
    expect(api.page).toHaveBeenCalledWith(expect.objectContaining({ page: 11, pageSize: 50, status: 0 }))
    expect(result).toEqual({ rows: [row], total: 501 })
    expect(api.search).not.toHaveBeenCalled()
  })
  it('rejects a malformed server page total instead of reporting a false empty result', async () => {
    const vm = render()
    api.page.mockResolvedValue({ code: 0, data: { items: [], page: 1, pageSize: 50 } })
    await expect(vm.fetchList(query)).rejects.toThrow()
    expect(vm.writesAvailable).toBe(false)
  })

  it('rejects an older page after a newer page is accepted', async () => {
    const vm = render(), response = pending()
    api.page.mockReturnValueOnce(response.promise)
    const old = vm.fetchList(query), observed = expect(old).rejects.toThrow()
    await flushPromises()
    api.page.mockResolvedValueOnce({ code: 0, data: { items: [row], total: 51, page: 2, pageSize: 50 } })
    expect(await vm.fetchList({ ...query, page: 2 })).toEqual({ rows: [row], total: 51 })
    response.resolve({ code: 0, data: { items: [row], total: 1, page: 1, pageSize: 50 } })
    await observed
    expect(vm.readError).toBe('')
  })
  it('does not disclose a pending page after the current query grant is removed', async () => {
    const vm = render(), response = pending()
    api.page.mockReturnValueOnce(response.promise)
    const old = vm.fetchList(query), observed = expect(old).rejects.toThrow()
    await flushPromises(); permission.has.mockReturnValue(false)
    response.resolve({ code: 0, data: { items: [row], total: 1, page: 1, pageSize: 50 } })
    await observed; expect(vm.writesAvailable).toBe(false); expect(vm.total).toBeUndefined()
  })

})
