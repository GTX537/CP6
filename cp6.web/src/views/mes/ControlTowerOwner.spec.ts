import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ summary: vi.fn(), dailyTrend: vi.fn(), delayAlerts: vi.fn() }))
const machine = vi.hoisted(() => ({ search: vi.fn() }))
const hub = vi.hoisted(() => ({ on: vi.fn(), off: vi.fn(), onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(), state: 'Connected' }))
const lifecycle = vi.hoisted(() => ({ start: vi.fn(), stop: vi.fn() }))
vi.mock('@/api/mes/mes', () => ({ mesDashboardApi: api, machineApi: machine }))
vi.mock('@/utils/mesHub', () => ({ getMesHub: () => hub, startMesHub: lifecycle.start, stopMesHub: lifecycle.stop }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import Tower from './ControlTowerView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
function render(): any {
  wrapper = shallowMount(Tower, { global: { config: { warnHandler: () => {} } } }); return wrapper.vm
}
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
beforeEach(() => {
  vi.useFakeTimers(); localStorage.clear(); vi.clearAllMocks()
  api.summary.mockReset().mockResolvedValue({ data: { inProgressCount: 1, completedCount: 2, totalGoodQty: 5, totalDefectQty: 1, defectRate: 20, delayedCount: 0 } })
  api.dailyTrend.mockReset().mockResolvedValue({ data: [] }); api.delayAlerts.mockReset().mockResolvedValue({ data: [] })
  machine.search.mockReset().mockResolvedValue({ data: [] }); lifecycle.start.mockResolvedValue(undefined); lifecycle.stop.mockResolvedValue(undefined)
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.useRealTimers() })
describe('MES control tower realtime boundary', () => {
  it('never displays business payloads from old event names', async () => {
    const vm = render(); await flushPromises()
    for (const name of ['ProductionReported', 'DefectIssued', 'MachineStatusChanged', 'WorkOrderStatusChanged', 'DowntimeRegistered']) {
      const handler = hub.on.mock.calls.find(call => call[0] === name)?.[1]
      handler?.({ workOrderNo: 'PRIVATE', processCd: 'PRIVATE', defectNo: 'PRIVATE', machineCd: 'PRIVATE', downtimeNo: 'PRIVATE', categoryCd: 'PRIVATE', goodQty: 999, defectQty: 999 })
    }
    await flushPromises(); expect(wrapper!.text()).not.toContain('PRIVATE'); expect(JSON.stringify(vm.events)).not.toContain('PRIVATE')
  })
  it('uses empty invalidation only to refresh HTTP results, ignoring any injected payload', async () => {
    const vm = render(); await flushPromises()
    api.summary.mockResolvedValueOnce({ data: { totalGoodQty: 7 } })
    const invalidate = hub.on.mock.calls.find(call => call[0] === 'MesInvalidatedV1')![1]
    await invalidate({ workOrderNo: 'PRIVATE', goodQty: 999 })
    expect(api.summary).toHaveBeenCalledTimes(2); expect(vm.summary.totalGoodQty).toBe(7)
    expect(wrapper!.text()).not.toContain('PRIVATE'); expect(JSON.stringify(vm.events)).not.toContain('PRIVATE')
  })
  it('ignores late responses from an old identity and clears previously loaded data', async () => {
    const vm = render(); await flushPromises(); expect(vm.summary.totalGoodQty).toBe(5)
    let done!: (value: unknown) => void
    api.summary.mockImplementationOnce(() => new Promise(resolve => { done = resolve }))
    const pending = vm.loadAll(); identity(); done({ data: { totalGoodQty: 999 } }); await pending
    expect(vm.summary.totalGoodQty).toBe(0); expect(vm.trend).toEqual([]); expect(vm.machines).toEqual([])
    expect(lifecycle.stop).toHaveBeenCalled()
  })
  it('clears forbidden refresh results without retaining stale dashboard values', async () => {
    const vm = render(); await flushPromises()
    api.summary.mockRejectedValueOnce({ response: { status: 403 } })
    await expect(vm.loadAll()).resolves.toBeUndefined()
    expect(vm.summary.totalGoodQty).toBe(0)
  })
  it('unmounts while loading without later starting a realtime connection or polling', async () => {
    const intervals = vi.spyOn(window, 'setInterval')
    let done!: (value: unknown) => void
    api.summary.mockImplementationOnce(() => new Promise(resolve => { done = resolve }))
    render(); wrapper!.unmount(); wrapper = undefined
    done({ data: { totalGoodQty: 999 } }); await flushPromises()
    expect(lifecycle.start).not.toHaveBeenCalled(); expect(intervals).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(11000)
    expect(api.summary).toHaveBeenCalledTimes(1)
    intervals.mockRestore()
  })
  it('does not restore LIVE or polling when an old connection finishes after identity change', async () => {
    let done!: () => void
    lifecycle.start.mockImplementationOnce(() => new Promise<void>(resolve => { done = resolve }))
    const vm = render(); await flushPromises(); identity(); done(); await flushPromises()
    await vi.advanceTimersByTimeAsync(11000)
    expect(api.summary).toHaveBeenCalledTimes(1); expect(vm.hubConnected).toBe(false)
    expect(hub.off).toHaveBeenCalledWith('MesInvalidatedV1', expect.any(Function))
  })
})
