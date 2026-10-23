import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ analytics: vi.fn(), featureFlags: vi.fn(), featureChanges: vi.fn() }))
const permission = vi.hoisted(() => ({ loaded: true, has: vi.fn(() => true) }))
vi.mock('@/api/wms/production', () => ({ productionApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permission }))
vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn() } }))
import View from '../WmsProductionConsole.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
const data = { created: 2, completed: 1, partiallyCompleted: 0, exceptions: 0, overdue: 0, averageMinutes: 0,
  durationSampleCount: 0, timeBasis: 'LEGACY_CREATED_AT', durationBasis: 'LEGACY_ELAPSED', physicalResultsVerified: false }
function render(): any { wrapper = shallowMount(View, { global: { directives: { permission: {}, loading: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true, ElTabPane: true, WmsSerialLpnDialogs: true } } }); return wrapper.vm }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permission.loaded = true; permission.has.mockReturnValue(true)
  api.analytics.mockReset(); api.analytics.mockResolvedValue(structuredClone(data)); api.featureFlags.mockResolvedValue([]); api.featureChanges.mockResolvedValue([])
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('Mobile analytics current Owner page', () => {
  it('labels the historical cohort and does not substitute zero for missing duration samples', async () => {
    const vm = render(); await flushPromises(); await vm.loadAnalytics()
    expect(vm.metrics.find((m: any) => m.label === 'Historical elapsed minutes').value).toBe('No samples')
    expect(vm.metrics.find((m: any) => m.label === 'Legacy reported complete').value).toBe(1)
    api.analytics.mockResolvedValueOnce({ ...data, durationSampleCount: 1, averageMinutes: 15 }); await vm.loadAnalytics()
    expect(vm.metrics.find((m: any) => m.label === 'Historical elapsed minutes').value).toBe(15)
  })
  it('clears denied or malformed analytics and does not turn an error into a zero dashboard', async () => {
    const vm = render(); await flushPromises(); await vm.loadAnalytics(); expect(vm.analytics).toBeDefined()
    api.analytics.mockRejectedValueOnce({ response: { status: 403 } }); await vm.loadAnalytics()
    expect(vm.analytics).toBeUndefined(); expect(vm.analyticsError).not.toBe('')
    api.analytics.mockResolvedValueOnce({ created: 0 }); await vm.loadAnalytics(); expect(vm.analytics).toBeUndefined()
    permission.has.mockReturnValue(false); api.analytics.mockClear(); await vm.loadAnalytics(); expect(api.analytics).not.toHaveBeenCalled()
  })
  it('drops stale identity and superseded analytics results', async () => {
    const vm = render(); await flushPromises(); const result = pending(); api.analytics.mockReturnValueOnce(result.promise)
    const reading = vm.loadAnalytics(); localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT))
    result.resolve(data); await reading; expect(vm.analytics).toBeUndefined()
    const older = pending(); api.analytics.mockReturnValueOnce(older.promise); const before = vm.loadAnalytics()
    api.analytics.mockRejectedValueOnce({ response: { status: 503 } }); await vm.loadAnalytics(); older.resolve(data); await before
    expect(vm.analytics).toBeUndefined(); expect(vm.analyticsError).not.toBe('')
    const unloaded = pending(); api.analytics.mockReturnValueOnce(unloaded.promise); const last = vm.loadAnalytics()
    permission.loaded = false; unloaded.resolve(data); await last; expect(vm.analytics).toBeUndefined()
  })
})
