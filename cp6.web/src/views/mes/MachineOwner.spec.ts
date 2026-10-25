import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), registerDowntime: vi.fn(),
  creationContext: vi.fn(), creationScope: vi.fn(), createOperation: vi.fn(), readCreation: vi.fn(), closeCreation: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), confirm: vi.fn() }))
const permissions = vi.hoisted(() => ({ loaded: true, has: vi.fn((_key: string) => true) }))
vi.mock('@/api/mes/mes', () => ({ machineApi: api }))
vi.mock('element-plus', () => ({ ElMessage: ui, ElMessageBox: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permissions }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import View from './MachineListView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingMachineCreation } from '@/auth/machineCreationRecovery'
let wrapper: ReturnType<typeof shallowMount> | undefined
function render(): any { wrapper = shallowMount(View, { global: { directives: { permission: {}, loading: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true } } }); return wrapper.vm }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
const machine = { machineCd: 'MC1', machineName: 'private machine', status: 1 }
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const department = '30000000-0000-0000-0000-000000000003', machineId = '40000000-0000-0000-0000-000000000004'
const wrap = (data: any) => ({ code: 0, message: '', data })
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permissions.loaded = true; permissions.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.search.mockResolvedValue({ data: [machine] }); api.get.mockResolvedValue({ data: machine })
  api.create.mockResolvedValue({ data: { machineCd: 'MC1' } }); api.update.mockResolvedValue({}); api.delete.mockResolvedValue({})
  api.registerDowntime.mockResolvedValue({ data: { downtimeNo: 'DT1' } }); ui.confirm.mockResolvedValue(undefined)
  api.creationScope.mockResolvedValue(wrap(scope))
  api.creationContext.mockResolvedValue(wrap({ ...scope, authorityVersion: 'current', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Department' }] }))
  api.createOperation.mockImplementation(async command => wrap({ operationId: command.operationId, machineId, machineCd: command.machine.machineCd, departmentId: command.departmentId }))
  const held = new Set<string>()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: async (key: string, options: any, run: (lock: any) => Promise<any>) => {
    expect(options.ifAvailable).toBe(true); if (held.has(key)) return run(null)
    held.add(key); try { return await run({ name: key }) } finally { held.delete(key) }
  } } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('Machine independent Owner page', () => {
  it('loads current allowed departments and requires an explicit choice before creating', async () => {
    api.creationContext.mockResolvedValue({ code: 0, data: { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002',
      authorityVersion: 'current', allowsUnbound: false, departments: [{ id: '30000000-0000-0000-0000-000000000003', code: 'D', name: 'Department' }] } })
    const vm = render(); await flushPromises(); await vm.openCreate()
    expect(api.creationContext).toHaveBeenCalledTimes(1); expect(vm.creationDepartment).toBeNull()
    Object.assign(vm.form, machine); await vm.onSave(); expect(api.createOperation).not.toHaveBeenCalled()
    vm.creationDepartment = department; await vm.onSave()
    expect(api.createOperation).toHaveBeenCalledTimes(1)
    expect(api.createOperation.mock.calls[0]![0]).toMatchObject({ departmentId: department, expectedAuthorityVersion: 'current', machine })
    expect(api.create).not.toHaveBeenCalled(); expect(pendingMachineCreation(scope)).toBeNull()
  })
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
    await vm.openCreate(); Object.assign(vm.form, machine); vm.creationDepartment = department
    const write = pending(); api.createOperation.mockReturnValueOnce(write.promise)
    const saving = vm.onSave(); await flushPromises(); expect(api.createOperation).toHaveBeenCalledTimes(1)
    const operationId = pendingMachineCreation(scope); identity()
    write.resolve(wrap({ operationId, machineCd: 'MC1', machineId, departmentId: department })); await saving
    expect(ui.success).not.toHaveBeenCalled(); expect(vm.creationResult).toBeNull(); expect(pendingMachineCreation(scope)).toBe(operationId)
  })
  it('retains the original after an uncertain create, restores it after reload, and only queries or closes it', async () => {
    let vm = render(); await flushPromises(); await vm.openCreate(); Object.assign(vm.form, machine); vm.creationDepartment = department
    api.createOperation.mockRejectedValueOnce(new Error('response lost')); await vm.onSave()
    const operationId = pendingMachineCreation(scope); expect(operationId).not.toBeNull()
    expect(vm.creationPending).toBe(operationId); expect(vm.dialogVisible).toBe(false)
    wrapper!.unmount(); wrapper = undefined; vm = render(); await flushPromises()
    expect(vm.creationPending).toBe(operationId); await vm.openCreate(); expect(vm.dialogVisible).toBe(false)
    api.readCreation.mockResolvedValue(wrap({ operationId, status: 'UNKNOWN' })); await vm.resolveCreation(false)
    expect(vm.creationPending).toBe(operationId)
    api.closeCreation.mockResolvedValue(wrap({ operationId, status: 'COMMITTED', result: { operationId, machineId, machineCd: 'MC1', departmentId: department } }))
    await vm.resolveCreation(true); expect(vm.creationPending).toBeNull(); expect(vm.creationResult.status).toBe('COMMITTED')
    expect(api.createOperation).toHaveBeenCalledTimes(1); expect(api.closeCreation).toHaveBeenCalledExactlyOnceWith(operationId)
  })
  it('freezes new device input and rejects overlapping submits without replacing the original key', async () => {
    const vm = render(); await flushPromises(); await vm.openCreate(); Object.assign(vm.form, machine); vm.creationDepartment = department
    const write = pending(); api.createOperation.mockReturnValueOnce(write.promise)
    const saving = vm.onSave(); await flushPromises(); const operationId = pendingMachineCreation(scope)
    Object.assign(vm.form, { machineName: 'late edit' }); vm.creationDepartment = null; await vm.onSave()
    expect(api.createOperation).toHaveBeenCalledTimes(1); expect(api.createOperation.mock.calls[0]![0].machine.machineName).toBe('private machine')
    write.resolve(wrap({ operationId, machineId, machineCd: 'MC1', departmentId: department })); await saving
    expect(pendingMachineCreation(scope)).toBeNull()
  })
  it('ignores old department context responses and clears protected content after current denial', async () => {
    const vm = render(); await flushPromises(); const context = pending(); api.creationContext.mockReturnValueOnce(context.promise)
    const opening = vm.openCreate(); identity(); context.resolve(wrap({ ...scope, authorityVersion: 'old', allowsUnbound: false, departments: [] })); await opening
    expect(vm.creationContext).toBeNull(); expect(vm.dialogVisible).toBe(false)
    api.creationContext.mockRejectedValueOnce({ response: { status: 403 } }); await vm.openCreate()
    expect(vm.rows).toEqual([]); expect(vm.creationScope).toBeNull()
  })
  it('preserves add-only creation without turning recovery query permission into a creation grant', async () => {
    permissions.has.mockImplementation(key => key === 'mes-machine:add')
    const vm = render(); await flushPromises(); await vm.openCreate(); Object.assign(vm.form, machine); vm.creationDepartment = department
    await vm.onSave(); expect(api.createOperation).toHaveBeenCalledTimes(1); expect(api.creationScope).not.toHaveBeenCalled()
  })
  it('recovers a pending creation despite another legacy write being uncertain, without clearing that uncertainty', async () => {
    const vm = render(); await flushPromises(); await vm.openCreate(); Object.assign(vm.form, machine); vm.creationDepartment = department
    api.createOperation.mockRejectedValueOnce(new Error('lost')); await vm.onSave(); const operationId = pendingMachineCreation(scope)
    vm.openDowntime(machine); api.registerDowntime.mockRejectedValueOnce(new Error('legacy response lost')); await vm.onRegisterDowntime()
    expect(vm.intent.uncertain.value).toBe(true)
    api.readCreation.mockResolvedValue(wrap({ operationId, status: 'COMMITTED', result: { operationId, machineId, machineCd: 'MC1', departmentId: department } }))
    await vm.resolveCreation(false); expect(vm.creationPending).toBeNull(); expect(vm.intent.uncertain.value).toBe(true)
    await vm.onRegisterDowntime(); expect(api.registerDowntime).toHaveBeenCalledTimes(1)
  })
  it('retains a creation interrupted by a forbidden list read without trapping it behind legacy uncertainty', async () => {
    const vm = render(); await flushPromises(); await vm.openCreate(); Object.assign(vm.form, machine); vm.creationDepartment = department
    const write = pending(); api.createOperation.mockReturnValueOnce(write.promise)
    const saving = vm.onSave(); await flushPromises(); const operationId = pendingMachineCreation(scope)
    api.search.mockRejectedValueOnce({ response: { status: 403 } }); await vm.search()
    write.resolve(wrap({ operationId, machineId, machineCd: 'MC1', departmentId: department })); await saving
    expect(ui.success).not.toHaveBeenCalled(); expect(vm.intent.uncertain.value).toBe(false); expect(pendingMachineCreation(scope)).toBe(operationId)
    await vm.loadCreationScope(); api.readCreation.mockResolvedValue(wrap({ operationId, status: 'COMMITTED', result: { operationId, machineId, machineCd: 'MC1', departmentId: department } }))
    await vm.resolveCreation(false); expect(vm.creationPending).toBeNull()
    await vm.openCreate(); expect(vm.dialogVisible).toBe(true)
  })
})
