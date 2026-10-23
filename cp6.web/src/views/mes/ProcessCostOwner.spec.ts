import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ list: vi.fn(), save: vi.fn(), remove: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), confirm: vi.fn() }))
const permissions = vi.hoisted(() => ({ loaded: true, has: vi.fn(() => true) }))
vi.mock('@/api/mes/processCost', () => ({ workCenterApi: api, processCostRateApi: api }))
vi.mock('element-plus', () => ({ ElMessage: ui, ElMessageBox: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permissions }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import Center from './WorkCenterView.vue'
import Rate from './ProcessCostRateView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
const center = { id: 'center-id', rowVersion: 'AAAAAA==', wgCd: 'WC1', wgName: 'Private center', dailyCapacityHours: '9999999999999.99999999', enable: true }
const rate = { id: 'rate-id', rowVersion: 'BBBBBB==', wgCd: 'WC1', laborRate: '1234567890123.12345678', overheadRate: '0.00000001', laborRateWritable: true, overheadRateWritable: true, validFrom: '2026-01-01T12:34:56.123456', validTo: null }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permissions.loaded = true; permissions.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.save.mockResolvedValue({ code: 0 }); api.remove.mockResolvedValue({ code: 0 }); ui.confirm.mockResolvedValue(undefined)
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
for (const [name, component, row] of [['center', Center, center], ['rate', Rate, rate]] as const) {
  function render(): any {
    api.list.mockResolvedValue({ data: [row], code: 0 })
    wrapper = shallowMount(component, { global: { directives: { permission: {}, loading: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return wrapper.vm
  }
  describe(`Precise ${name} Owner page`, () => {
    it('clears protected rows and forms on identity change and ignores delayed old reads', async () => {
      const vm = render(); await flushPromises(); vm.openEdit(row); const response = pending(); api.list.mockReturnValueOnce(response.promise)
      const reading = vm.reload(); identity(); response.resolve({ data: [row], code: 0 }); await reading
      expect(vm.rows).toEqual([]); expect(vm.dialogVisible).toBe(false); expect(vm.form.wgCd).toBe('')
    })
    it('clears an edited id and expected version when creating a distinct record', async () => {
      const vm = render(); await flushPromises(); vm.openEdit(row); vm.openCreate()
      expect(vm.form.id).toBeUndefined(); expect(vm.form.rowVersion).toBeUndefined()
    })
    it('keeps decimals and unchanged timestamp byte-exact while freezing a versioned write', async () => {
      const vm = render(); await flushPromises(); vm.openEdit(row); const response = pending(); api.save.mockReturnValueOnce(response.promise)
      const saving = vm.submit(); await vm.submit(); vm.form.wgCd = 'LATE'
      expect(api.save).toHaveBeenCalledTimes(1); const sent = api.save.mock.calls[0]![0]
      expect(sent.wgCd).toBe('WC1'); expect(sent.id).toBe(row.id); expect(sent.rowVersion).toBe(row.rowVersion)
      if (name === 'center') expect(sent.dailyCapacityHours).toBe(center.dailyCapacityHours)
      else { expect(sent.laborRate).toBe(rate.laborRate); expect(sent.overheadRate).toBe(rate.overheadRate); expect(sent.validFrom).toBe(rate.validFrom) }
      response.resolve({ code: 0 }); await saving
    })
    it('clears failures and discards older responses after a newer denied read', async () => {
      const vm = render(); await flushPromises(); vm.openEdit(row); const response = pending(); api.list.mockReturnValueOnce(response.promise)
      const older = vm.reload(); api.list.mockRejectedValueOnce({ response: { status: 403 } }); await vm.reload()
      response.resolve({ data: [row], code: 0 }); await older; expect(vm.rows).toEqual([]); expect(vm.dialogVisible).toBe(false)
    })
    it('does not report conflicts or unknown outcomes as success and never resends an uncertain write', async () => {
      const vm = render(); await flushPromises(); vm.openEdit(row)
      api.save.mockRejectedValueOnce({ response: { status: 409, data: { message: 'ROW_VERSION_CONFLICT' } } }); await vm.submit()
      expect(ui.success).not.toHaveBeenCalled(); expect(vm.rows).toEqual([]); expect(vm.dialogVisible).toBe(false)
      await vm.reload(); vm.openEdit(row); api.save.mockRejectedValueOnce({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } }); await vm.submit()
      vm.openEdit(row); await vm.submit(); await vm.reload(); vm.openEdit(row); await vm.submit()
      expect(api.save).toHaveBeenCalledTimes(2); expect(vm.intent.uncertain.value).toBe(true); expect(ui.success).not.toHaveBeenCalled()
    })
    it('cannot confirm a previous identity delete or bypass a missing current action', async () => {
      const vm = render(); await flushPromises(); const confirmation = pending(); ui.confirm.mockReturnValueOnce(confirmation.promise)
      const deleting = vm.doDelete(row); identity(); confirmation.resolve(undefined); await deleting; expect(api.remove).not.toHaveBeenCalled()
      permissions.has.mockReturnValue(false); vm.openEdit(row); await vm.submit(); await vm.reload()
      expect(api.save).not.toHaveBeenCalled(); expect(vm.rows).toEqual([])
    })
    it('sends the expected version on delete and ignores late write success after logout', async () => {
      const vm = render(); await flushPromises(); const response = pending(); api.remove.mockReturnValueOnce(response.promise)
      const deleting = vm.doDelete(row); await flushPromises(); expect(api.remove).toHaveBeenCalledWith(row.id, row.rowVersion)
      identity(); response.resolve({ code: 0 }); await deleting; expect(ui.success).not.toHaveBeenCalled()
    })
    if (name === 'rate') it('preserves hidden fees as null and omits both hidden and read-only fee changes', async () => {
      const vm = render(); await flushPromises(); vm.openEdit({ ...rate, laborRate: null, laborRateWritable: false, overheadRateWritable: false })
      expect(vm.form.laborRate).toBeNull(); await vm.submit()
      expect(api.save.mock.calls[0]![0].laborRate).toBeUndefined(); expect(api.save.mock.calls[0]![0].overheadRate).toBeUndefined()
    })
  })
}
