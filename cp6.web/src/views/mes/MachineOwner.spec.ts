import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), registerDowntime: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), confirm: vi.fn() }))
const permissions = vi.hoisted(() => ({ loaded: true, has: vi.fn(() => true) }))
vi.mock('@/api/mes/mes', () => ({ machineApi: api }))
vi.mock('element-plus', () => ({ ElMessage: ui, ElMessageBox: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permissions }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from './MachineListView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
function render(): any { wrapper = shallowMount(View, { global: { directives: { permission: {}, loading: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return wrapper.vm }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
const machine = { machineCd: 'MC1', machineName: 'private machine', status: 1 }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permissions.loaded = true; permissions.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.search.mockResolvedValue({ data: [machine] }); api.get.mockResolvedValue({ data: machine })
  api.create.mockResolvedValue({ data: { machineCd: 'MC1' } }); api.update.mockResolvedValue({}); api.delete.mockResolvedValue({})
  api.registerDowntime.mockResolvedValue({ data: { downtimeNo: 'DT1' } }); ui.confirm.mockResolvedValue(undefined)
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('Machine independent Owner page', () => {
  it('clears identity data and ignores delayed reads from the old session', async () => {
    const vm = render(); await flushPromises(); const response = pending(); api.search.mockReturnValueOnce(response.promise)
    vm.openDowntime(machine); const read = vm.search(); identity(); response.resolve({ data: [machine] }); await read
    expect(vm.rows).toEqual([]); expect(vm.dtDialogVisible).toBe(false); expect(vm.dtForm.machineCd).toBe('')
  })
  it('drops forbidden and out-of-order list results', async () => {
    const vm = render(); await flushPromises(); const response = pending(); api.search.mockReturnValueOnce(response.promise)
    const old = vm.search(); api.search.mockRejectedValueOnce({ response: { status: 403 } }); await vm.search()
    response.resolve({ data: [machine] }); await old; expect(vm.rows).toEqual([])
  })
  it('freezes downtime writes, blocks duplicates and retains uncertainty without resend', async () => {
    const vm = render(); await flushPromises(); vm.openDowntime(machine); const response = pending()
    api.registerDowntime.mockReturnValueOnce(response.promise); const write = vm.onRegisterDowntime(); await vm.onRegisterDowntime()
    vm.dtForm.description = 'late edit'; expect(api.registerDowntime).toHaveBeenCalledTimes(1)
    expect(api.registerDowntime.mock.calls[0]![0].description).not.toBe('late edit')
    response.resolve({ data: { downtimeNo: 'DT1' } }); await write
    vm.openDowntime(machine); api.registerDowntime.mockRejectedValueOnce({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } })
    await vm.onRegisterDowntime(); await vm.onRegisterDowntime(); expect(api.registerDowntime).toHaveBeenCalledTimes(2)
    expect(vm.intent.uncertain.value).toBe(true)
    api.search.mockRejectedValueOnce({ response: { status: 403 } }); await vm.search()
    Object.assign(vm.dtForm, { machineCd: 'MC1' }); await vm.onRegisterDowntime()
    expect(api.registerDowntime).toHaveBeenCalledTimes(2); expect(vm.intent.uncertain.value).toBe(true)
  })
  it('cannot confirm an old identity delete or bypass a missing UI action', async () => {
    const vm = render(); await flushPromises(); const confirmation = pending(); ui.confirm.mockReturnValueOnce(confirmation.promise)
    const deleting = vm.onDelete(machine); identity(); confirmation.resolve(undefined); await deleting; expect(api.delete).not.toHaveBeenCalled()
    permissions.has.mockReturnValue(false); Object.assign(vm.form, machine); await vm.onSave(); expect(api.create).not.toHaveBeenCalled()
    vm.openDowntime(machine); await vm.onRegisterDowntime(); expect(api.registerDowntime).not.toHaveBeenCalled()
  })
  it('rejects a late edit load and does not report old-session write success', async () => {
    const vm = render(); await flushPromises(); const response = pending(); api.get.mockReturnValueOnce(response.promise)
    const edit = vm.openEdit(machine); identity(); response.resolve({ data: machine }); await edit
    expect(vm.dialogVisible).toBe(false); expect(vm.form.machineCd).toBe('')
    Object.assign(vm.form, machine); const write = pending(); api.create.mockReturnValueOnce(write.promise)
    const saving = vm.onSave(); identity(); write.resolve({ data: { machineCd: 'MC1' } }); await saving; expect(ui.success).not.toHaveBeenCalled()
  })
})
