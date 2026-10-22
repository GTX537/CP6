import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ today: vi.fn(), search: vi.fn(), trend: vi.fn(), recalculate: vi.fn() }))
const machines = vi.hoisted(() => ({ search: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn() }))
const permission = vi.hoisted(() => ({ loaded: true, has: vi.fn(() => true) }))
vi.mock('@/api/mes/mes', () => ({ oeeApi: api, machineApi: machines }))
vi.mock('element-plus', () => ({ ElMessage: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permission }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from './OeeAnalysisView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
function render(): any { wrapper = shallowMount(View, { global: { directives: { permission: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return wrapper.vm }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
const row = { machineCd: 'MC1', machineName: 'private machine', oeeDate: '2026-10-22', oee: 25, availability: 80, quality: 90, performance: 35 }
beforeEach(() => {
  vi.useFakeTimers(); localStorage.clear(); vi.clearAllMocks(); permission.loaded = true; permission.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.today.mockResolvedValue({ data: [row] }); api.search.mockResolvedValue({ data: [row] }); api.trend.mockResolvedValue({ data: { MC1: [row] } })
  api.recalculate.mockResolvedValue({ data: { recalculated: 1 } }); machines.search.mockReset().mockResolvedValue({ data: [{ machineCd: 'MC1' }] })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.useRealTimers() })
describe('OEE current source and page boundaries', () => {
  it('clears old identity data and ignores its delayed aggregate', async () => {
    const vm = render(); await flushPromises(); const response = pending(); api.today.mockReturnValueOnce(response.promise)
    const loading = vm.loadAll(); identity(); response.resolve({ data: [row] }); await loading
    expect(vm.todayOee).toEqual([]); expect(vm.rows).toEqual([]); expect(vm.trendMachines).toEqual([]); expect(vm.allMachines).toEqual([])
  })
  it('clears forbidden aggregates and stops polling after loss of authority', async () => {
    const vm = render(); await flushPromises(); api.today.mockRejectedValueOnce({ response: { status: 403 } })
    await vm.loadAll(); expect(vm.todayOee).toEqual([]); expect(vm.rows).toEqual([]); expect(vm.trendMachines).toEqual([])
    const count = api.today.mock.calls.length; await vi.advanceTimersByTimeAsync(31000); expect(api.today).toHaveBeenCalledTimes(count)
  })
  it('does not start polling or request aggregates after an old machine lookup finishes on unmount', async () => {
    const intervals = vi.spyOn(window, 'setInterval'); const response = pending(); machines.search.mockReturnValueOnce(response.promise)
    render(); wrapper!.unmount(); wrapper = undefined; response.resolve({ data: [{ machineCd: 'MC1' }] }); await flushPromises()
    expect(intervals).not.toHaveBeenCalled(); expect(api.today).not.toHaveBeenCalled(); intervals.mockRestore()
  })
  it('freezes and deduplicates recalculation and retains UNKNOWN through a forbidden refresh', async () => {
    const vm = render(); await flushPromises(); vm.machineCd = 'MC1'; const response = pending(); api.recalculate.mockReturnValueOnce(response.promise)
    const saving = vm.onRecalc(); await vm.onRecalc(); vm.machineCd = 'CHANGED'
    expect(api.recalculate).toHaveBeenCalledTimes(1); expect(api.recalculate.mock.calls[0]![0].machineCd).toBe('MC1')
    response.resolve({ data: { recalculated: 1 } }); await saving
    api.recalculate.mockRejectedValueOnce({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } })
    await vm.onRecalc(); api.today.mockRejectedValueOnce({ response: { status: 403 } }); await vm.loadAll(); await vm.onRecalc()
    expect(api.recalculate).toHaveBeenCalledTimes(2); expect(vm.intent.uncertain.value).toBe(true)
  })
  it('does not write without its own action or report success to a different identity', async () => {
    const vm = render(); await flushPromises(); permission.has.mockReturnValue(false); await vm.onRecalc(); expect(api.recalculate).not.toHaveBeenCalled()
    permission.has.mockReturnValue(true); const response = pending(); api.recalculate.mockReturnValueOnce(response.promise)
    const saving = vm.onRecalc(); identity(); response.resolve({ data: { recalculated: 1 } }); await saving; expect(ui.success).not.toHaveBeenCalled()
  })
  it('rejects a stale filter response after a more recent selection has loaded', async () => {
    const vm = render(); await flushPromises(); const response = pending(); api.today.mockReturnValueOnce(response.promise)
    const old = vm.loadAll(); vm.machineCd = 'MC2'; api.today.mockResolvedValueOnce({ data: [{ ...row, machineCd: 'MC2' }] }); await vm.loadAll()
    response.resolve({ data: [row] }); await old; expect(vm.todayOee[0].machineCd).toBe('MC2')
  })
})
