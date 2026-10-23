import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ summary: vi.fn(), observation: vi.fn(), dispose: vi.fn() }))
const permissions = vi.hoisted(() => ({ loaded: true, has: vi.fn((_key: string) => true) }))
vi.mock('@/api/wms/stockDwell', () => ({ stockDwellApi: api }))
vi.mock('@/api/wms/expiry', () => ({ expiryApi: api }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permissions }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import Dwell from './StockDwellView.vue'
import Expiry from './ExpiryView.vue'
import { observationText, observationPercent, inventoryObservationVersion } from '@/utils/inventoryObservation'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'

let wrapper: ReturnType<typeof shallowMount> | undefined
const data = { version: inventoryObservationVersion, asOfDate: '2026-10-23', days: 0, total: 503, page: 1, pageSize: 50,
  totalQty: '19999999999999.99999998', referenceValue: null, referenceReadable: false, rows: [] }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permissions.loaded = true; permissions.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.summary.mockResolvedValue({ code: 0, data }); api.observation.mockResolvedValue({ code: 0, data })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
for (const [name, component, method] of [['dwell', Dwell, 'summary'], ['expiry', Expiry, 'observation']] as const) {
  function render(): any {
    wrapper = shallowMount(component, { global: { directives: { loading: {} }, config: { warnHandler: () => {} },
      stubs: { ElTable: true, ElTableColumn: true } } }); return wrapper.vm
  }
  const reload = (vm: any) => name === 'dwell' ? vm.load() : vm.load({ ...vm.query })
  const result = (vm: any) => name === 'dwell' ? vm.summary : vm.result
  describe(`Inventory observation ${name}`, () => {
    it('keeps exact quantities and unknown values, requires the named Owner response', async () => {
      const vm = render(); await flushPromises(); expect(result(vm).totalQty).toBe('19999999999999.99999998')
      expect(result(vm).referenceValue).toBeNull()
      api[method].mockResolvedValueOnce({ code: 0, data: { ...data, version: undefined } }); await reload(vm)
      expect(result(vm)).toBeNull(); expect(vm.readError).not.toBe('')
    })
    it('clears identity changes immediately and discards late previous identity results', async () => {
      const vm = render(); await flushPromises(); const response = pending(); api[method].mockReturnValueOnce(response.promise)
      const reading = reload(vm); identity(); expect(result(vm)).toBeNull()
      response.resolve({ code: 0, data }); await reading; expect(result(vm)).toBeNull(); expect(vm.loading).toBe(false)
    })
    it('does not allow an older success to replace a more recent denied read', async () => {
      const vm = render(); await flushPromises(); const response = pending(); api[method].mockReturnValueOnce(response.promise)
      const older = reload(vm); api[method].mockRejectedValueOnce({ response: { status: 403 } }); await reload(vm)
      response.resolve({ code: 0, data }); await older; expect(result(vm)).toBeNull(); expect(vm.readError).not.toBe('')
    })
    it('requires both view and source stock actions and an explicitly selected date', async () => {
      const vm = render(); await flushPromises(); api[method].mockClear()
      permissions.has.mockImplementation(key => key !== 'wms-stock:query'); await reload(vm)
      expect(api[method]).not.toHaveBeenCalled(); expect(result(vm)).toBeNull()
      permissions.has.mockReturnValue(true); vm.query.asOfDate = ''; await reload(vm); expect(api[method]).not.toHaveBeenCalled()
    })
    if (name === 'expiry') it('pages the last applied range on the server, retains day zero and never submits disposal', async () => {
      const vm = render(); await flushPromises(); vm.query.days = 0; vm.query.asOfDate = '2026-10-23'; vm.search(); await flushPromises()
      expect(api.observation.mock.calls.at(-1)![0]).toMatchObject({ days: 0, page: 1, asOfDate: '2026-10-23' })
      vm.query.days = 200; vm.pageChanged(2); await flushPromises()
      expect(api.observation.mock.calls.at(-1)![0]).toMatchObject({ days: 0, page: 2, pageSize: 50 })
      expect(api.dispose).not.toHaveBeenCalled(); expect(vm.onDispose).toBeUndefined()
    })
  })
}
it('formats every stored decimal digit without Number conversion, with approximation restricted to chart geometry', () => {
  expect(observationText('999999999999999998999000000.000000000001')).toBe('999,999,999,999,999,998,999,000,000.000000000001')
  expect(observationText('0.00000001')).toBe('0.00000001'); expect(observationText('0')).toBe('0')
  expect(observationText(null)).toBe('—'); expect(observationPercent(undefined, undefined)).toBeNull()
  expect(observationPercent('9999999999999.99999999', '19999999999999.99999998')).toBe(50)
})
