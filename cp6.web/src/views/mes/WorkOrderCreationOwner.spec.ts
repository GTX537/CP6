import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
const api = vi.hoisted(() => ({ get: vi.fn(), create: vi.fn(), update: vi.fn(), issue: vi.fn(), expandFromOrder: vi.fn(), creationContext: vi.fn(), creationScope: vi.fn(), createOperation: vi.fn(), readCreation: vi.fn(), closeCreation: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn(), confirm: vi.fn() }))
const permissions = vi.hoisted(() => ({ loaded: true, has: vi.fn((_key: string) => true) }))
vi.mock('@/api/mes/mes', () => ({ workOrderApi: api }))
vi.mock('element-plus', () => ({ ElMessage: ui, ElMessageBox: { confirm: ui.confirm } }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permissions }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('vue-router', () => ({ useRoute: () => route, useRouter: () => ({ push: vi.fn() }) }))
import View from './WorkOrderEntryView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const route = reactive({ query: {} as Record<string,string> })
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const department = '30000000-0000-0000-0000-000000000003', workOrderId = '40000000-0000-0000-0000-000000000004'
const wrap = (data: any) => ({ code: 0, message: '', data })
const reference = () => localStorage.getItem('cp6_work_order_creation_v1:' + scope.tenantId + ':' + scope.actorId)
let wrapper: ReturnType<typeof shallowMount> | undefined
function render(): any {
  wrapper = shallowMount(View, { global: { directives: { permission: {} }, config: { warnHandler: () => {} }, stubs: {
    ElCard: { template: '<div><slot/><slot name="header"/></div>' }
  } } }); return wrapper.vm
}
function fill(vm: any) { Object.assign(vm.form, { productCd: 'P', productionQty: 3, processes: [{ processCd: 'P1', taskCd: 'T1' }], materials: [] }) }
function deferred() { let resolve!: (v: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
beforeEach(() => {
  localStorage.clear(); route.query = {}; vi.clearAllMocks(); permissions.loaded = true; permissions.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  ui.confirm.mockResolvedValue('confirm'); api.creationScope.mockResolvedValue(wrap(scope))
  api.creationContext.mockResolvedValue(wrap({ ...scope, authorityVersion: 'current', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Department' }] }))
  api.createOperation.mockImplementation(async command => wrap({ operationId: command.operationId, workOrderId, workOrderNo: 'WO1', departmentId: command.departmentId }))
  const held = new Set<string>()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: async (key: string, _options: any, run: (lock: any) => Promise<any>) => {
    if (held.has(key)) return run(null)
    held.add(key); try { return await run({ name: key }) } finally { held.delete(key) }
  } } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('Work order creation page', () => {
  it('requires explicit department for an unlinked order and preserves committed identity without query', async () => {
    permissions.has.mockImplementation(key => key === 'mes-work-order:add')
    const vm = render(); await flushPromises(); fill(vm)
    expect(vm.creationDepartment).toBeNull(); await vm.onSave(); expect(api.createOperation).not.toHaveBeenCalled()
    vm.creationDepartment = department; await vm.onSave()
    expect(api.createOperation.mock.calls[0]![0]).toMatchObject({ departmentId: department, expectedAuthorityVersion: 'current', workOrder: { productCd: 'P', productionQty: 3 } })
    expect(vm.form.workOrderNo).toBe('WO1'); expect(reference()).toBeNull()
    expect(api.create).not.toHaveBeenCalled(); expect(api.get).not.toHaveBeenCalled(); expect(api.creationScope).not.toHaveBeenCalled()
  })
  it('retains the order-derived path with no independent department assignment', async () => {
    const vm = render(); await flushPromises(); fill(vm); vm.form.webOrderNo = 'ORDER1'; await vm.onSave()
    expect(api.createOperation.mock.calls[0]![0]).toMatchObject({ departmentId: null, workOrder: { webOrderNo: 'ORDER1' } })
  })
  it('freezes graph, prevents duplicates and ignores old-identity completion without dropping its reference', async () => {
    const vm = render(); await flushPromises(); fill(vm); vm.creationDepartment = department
    const response = deferred(); api.createOperation.mockReturnValueOnce(response.promise)
    const first = vm.onSave(); await flushPromises(); await vm.onSave(); expect(api.createOperation).toHaveBeenCalledTimes(1)
    vm.form.processes[0].taskCd = 'NEW'; expect(api.createOperation.mock.calls[0]![0].workOrder.processes[0].taskCd).toBe('T1')
    const operationId = reference(); identity(); response.resolve(wrap({ operationId, workOrderId, workOrderNo: 'OLD', departmentId: department })); await first
    expect(vm.form.workOrderNo).toBe(''); expect(vm.creationContext).toBeNull(); expect(reference()).toBe(operationId); expect(ui.success).not.toHaveBeenCalled()
  })
  it('recovers after reload, never resends unknown and preserves receipt if readback is denied', async () => {
    let vm = render(); await flushPromises(); fill(vm); vm.creationDepartment = department
    api.createOperation.mockRejectedValueOnce(new Error('lost')); await vm.onSave(); const operationId = reference(); expect(operationId).not.toBeNull()
    wrapper!.unmount(); wrapper = undefined; vm = render(); await flushPromises(); fill(vm); vm.creationDepartment = department
    await vm.onSave(); expect(api.createOperation).toHaveBeenCalledTimes(1)
    api.readCreation.mockResolvedValueOnce(wrap({ operationId, status: 'UNKNOWN' })); await vm.resolveCreation(false); expect(reference()).toBe(operationId)
    api.readCreation.mockResolvedValueOnce(wrap({ operationId, status: 'COMMITTED', result: { operationId, workOrderId, workOrderNo: 'WO1', departmentId: department } }))
    api.get.mockRejectedValueOnce({ response: { status: 403 } }); await vm.resolveCreation(false)
    expect(reference()).toBeNull(); expect(vm.form.workOrderNo).toBe('WO1'); expect(vm.creationResult.status).toBe('COMMITTED')
    expect(vm.recoveredOnly).toBe(true); await vm.onSave(); await vm.onIssue(); expect(api.update).not.toHaveBeenCalled(); expect(api.issue).not.toHaveBeenCalled()
  })
  it('retains reference on current recovery denial and closes an unknown original explicitly', async () => {
    const vm = render(); await flushPromises(); fill(vm); vm.creationDepartment = department
    api.createOperation.mockRejectedValueOnce(new Error('lost')); await vm.onSave(); const operationId = reference()
    api.readCreation.mockRejectedValueOnce({ response: { status: 403 } }); await vm.resolveCreation(false); expect(reference()).toBe(operationId)
    await vm.loadCreationScope(); api.closeCreation.mockResolvedValueOnce(wrap({ operationId, status: 'CLOSED' })); await vm.resolveCreation(true)
    expect(reference()).toBeNull(); expect(api.closeCreation).toHaveBeenCalledExactlyOnceWith(operationId); expect(api.createOperation).toHaveBeenCalledTimes(1)
  })
  it('detects a changed login marker before freezing or submitting the previous identity draft', async () => {
    const vm = render(); await flushPromises(); fill(vm); vm.creationDepartment = department
    localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()) // The corresponding identity event has not arrived yet.
    await vm.onSave(); expect(api.createOperation).not.toHaveBeenCalled(); expect(vm.form.productCd).toBe(''); expect(reference()).toBeNull()
  })

})
