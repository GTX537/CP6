import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ tasks: vi.fn(), get: vi.fn(), events: vi.fn(), capabilities: vi.fn(), create: vi.fn(), assign: vi.fn(), pause: vi.fn(), cancel: vi.fn(), release: vi.fn(), takeover: vi.fn(), exception: vi.fn(), resolveException: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), prompt: vi.fn() }))
const permission = vi.hoisted(() => ({ loaded: true, has: vi.fn(() => true) }))
const hub = vi.hoisted(() => ({ state: 'Connected', on: vi.fn(), off: vi.fn(), onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn() }))
vi.mock('@/api/wms/mobile', () => ({ mobileApi: api }))
vi.mock('element-plus', () => ({ ElMessage: ui, ElMessageBox: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permission }))
vi.mock('@/utils/wmsHub', () => ({ getWmsConnection: () => hub, startWmsConnection: async () => hub }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from '../MobileTaskView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
const row = { taskNo: 'MOB-1', taskType: 'MOVE', qty: 12, status: 0, rowVersion: 'v1', executionVersion: 1, assignedTo: 'private' }
function render(): any { wrapper = shallowMount(View, { global: { directives: { permission: {}, loading: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true, ElDescriptions: true, ElDescriptionsItem: true, WmsProductionConsole: true } } }); return wrapper.vm }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permission.loaded = true; permission.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.capabilities.mockResolvedValue({ writesAvailable: false, status: 'MOBILE_OWNER_ADOPTION_UNPROVEN' })
  api.tasks.mockResolvedValue({ items: [structuredClone(row)], total: 1, page: 1, pageSize: 50 })
  api.get.mockResolvedValue(structuredClone(row)); api.events.mockResolvedValue([])
  for (const key of ['create', 'assign', 'pause', 'cancel', 'release', 'takeover', 'exception', 'resolveException'] as const) api[key].mockResolvedValue(row)
  ui.prompt.mockResolvedValue({ value: 'reason' })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('Mobile current Owner page', () => {
  it('blocks unadopted commands including direct handlers', async () => {
    const vm = render(); await flushPromises()
    await vm.create(); await vm.assign(row); await vm.pause(row); await vm.cancel(row)
    for (const action of ['release', 'takeover', 'exception', 'resolve']) await vm.taskAction(action, row)
    for (const key of ['create', 'assign', 'pause', 'cancel', 'release', 'takeover', 'exception', 'resolveException'] as const) expect(api[key]).not.toHaveBeenCalled()
    expect(ui.success).not.toHaveBeenCalled()
  })
  it('clears data and reports a failed read instead of showing previous rows', async () => {
    const vm = render(); await flushPromises(); expect(vm.tasks).toHaveLength(1)
    api.tasks.mockRejectedValueOnce({ response: { status: 403 } }); await expect(vm.reload()).rejects.toThrow()
    expect(vm.tasks).toEqual([]); expect(vm.total).toBe(0); expect(vm.readError).not.toBe('')
  })
  it('drops an in-flight old identity list and clears forms', async () => {
    const vm = render(); await flushPromises(); const result = pending(); api.tasks.mockReturnValueOnce(result.promise)
    const reading = vm.reload().catch(() => {}); await flushPromises(); vm.createVisible = true; vm.createForm.productCd = 'private'
    identity(); result.resolve({ items: [row], total: 1 }); await reading
    expect(vm.tasks).toEqual([]); expect(vm.createVisible).toBe(false); expect(vm.createForm.productCd).toBe('')
  })
  it('rejects old identity detail and missing current view action', async () => {
    const vm = render(); await flushPromises(); const result = pending(); api.get.mockReturnValueOnce(result.promise)
    const reading = vm.openDetail(row).catch(() => {}); await flushPromises(); identity(); result.resolve(row); await reading
    expect(vm.selected).toBeUndefined(); expect(vm.detailVisible).toBe(false)
    permission.has.mockReturnValue(false); api.tasks.mockClear(); await expect(vm.reload()).rejects.toThrow(); expect(api.tasks).not.toHaveBeenCalled()
  })
  it('does not accept an older capability after a denied read or missing list data', async () => {
    const vm = render(); await flushPromises(); const result = pending(); api.capabilities.mockReturnValueOnce(result.promise)
    const old = vm.reload().catch(() => {}); api.capabilities.mockRejectedValueOnce({ response: { status: 403 } }); await expect(vm.reload()).rejects.toThrow()
    result.resolve({ writesAvailable: true, status: 'LEGACY' }); await old; expect(vm.writesAvailable).toBe(false); expect(vm.tasks).toEqual([])
    api.tasks.mockResolvedValueOnce({ total: 0 }); await expect(vm.reload()).rejects.toThrow(); expect(vm.readError).not.toBe('')
  })
  it('freezes legacy payload and blocks duplicate and unknown result resubmission', async () => {
    api.capabilities.mockResolvedValue({ writesAvailable: true, status: 'LEGACY' }); const vm = render(); await flushPromises()
    vm.createForm.qty = 12; const result = pending(); api.create.mockReturnValueOnce(result.promise)
    const saving = vm.create(); await flushPromises(); vm.createForm.qty = 999; await vm.create()
    expect(api.create).toHaveBeenCalledTimes(1); expect(api.create.mock.calls[0]![0].qty).toBe(12)
    result.resolve({}); await saving; expect(ui.success).not.toHaveBeenCalled(); expect(vm.intent.uncertain.value).toBe(true)
    await vm.reload(); await vm.create(); expect(api.create).toHaveBeenCalledTimes(1)
  })
  it('rejects confirmation under a changed identity and never celebrates an old response', async () => {
    api.capabilities.mockResolvedValue({ writesAvailable: true, status: 'LEGACY' }); const vm = render(); await flushPromises()
    const answer = pending(); ui.prompt.mockReturnValueOnce(answer.promise); const sending = vm.assign(row); identity(); answer.resolve({ value: 'other' }); await sending
    expect(api.assign).not.toHaveBeenCalled(); await vm.reload()
    const result = pending(); api.create.mockReturnValueOnce(result.promise); const saving = vm.create(); await flushPromises(); identity(); result.resolve(row); await saving
    expect(ui.success).not.toHaveBeenCalled(); expect(vm.tasks).toEqual([])
  })
})
