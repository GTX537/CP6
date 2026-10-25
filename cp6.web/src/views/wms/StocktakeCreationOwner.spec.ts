import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), createPlan: vi.fn(), creationContext: vi.fn(), creationScope: vi.fn(), createOperation: vi.fn(), readCreation: vi.fn(), closeCreation: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), push: vi.fn() }))
const permissions = vi.hoisted(() => ({ loaded: true, has: vi.fn((_key: string) => true) }))
vi.mock('@/api/wms/stockTake', () => ({ stockTakeApi: api }))
vi.mock('element-plus', () => ({ ElMessage: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permissions }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: ui.push }) }))
import View from './StockTakeListView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const department = '30000000-0000-0000-0000-000000000003', stockTakeId = '40000000-0000-0000-0000-000000000004'
const wrap = (data: any) => ({ code: 0, message: '', data })
const reference = () => localStorage.getItem(`cp6_stocktake_creation_v1:${scope.tenantId}:${scope.actorId}`)
let wrapper: ReturnType<typeof shallowMount> | undefined
function render(): any {
  wrapper = shallowMount(View, { global: { directives: { permission: {} }, config: { warnHandler: () => {} }, stubs: {
    CpPageShell: { template: '<div><slot/><slot name="actions"/></div>' }, CpListPage: true, CpFormDialog: true,
  } } }); return wrapper.vm
}
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); permissions.loaded = true; permissions.has.mockReturnValue(true)
  for (const mock of Object.values(api)) mock.mockReset()
  api.search.mockResolvedValue(wrap([])); ui.push.mockResolvedValue(undefined)
  api.creationScope.mockResolvedValue(wrap(scope))
  api.creationContext.mockResolvedValue(wrap({ ...scope, authorityVersion: 'current', allowsUnbound: false, departments: [{ id: department, code: 'D', name: 'Department' }] }))
  api.createOperation.mockImplementation(async command => wrap({ operationId: command.operationId, stockTakeId, stockTakeNo: 'ST1', departmentId: command.departmentId }))
  const held = new Set<string>()
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: async (key: string, options: any, run: (lock: any) => Promise<any>) => {
    if (held.has(key)) return run(null)
    held.add(key); try { return await run({ name: key }) } finally { held.delete(key) }
  } } })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
describe('Stocktake creation page', () => {
  it('retains a confirmed receipt when navigation fails after commit', async () => {
    const vm = render(); await flushPromises(); await vm.openPlan(); vm.creationDepartment = department; vm.planForm.targetWarehouseCd = 'W01'
    ui.push.mockRejectedValueOnce(new Error('navigation failed')); await expect(vm.onCreatePlan()).rejects.toThrow()
    expect(vm.creationResult.status).toBe('COMMITTED'); expect(vm.lastSavedNo).toBe('ST1')
    expect(vm.creationError).toContain('登録は完了'); expect(reference()).toBeNull(); expect(api.createOperation).toHaveBeenCalledTimes(1)
  })
  it('requires an explicit current department and uses the original-operation creation path', async () => {
    const vm = render(); await flushPromises(); await vm.openPlan()
    expect(api.creationContext).toHaveBeenCalledTimes(1); expect(vm.creationDepartment).toBeNull()
    vm.planForm.targetWarehouseCd = 'W01'
    await expect(vm.onCreatePlan()).rejects.toThrow(); expect(api.createOperation).not.toHaveBeenCalled()
    vm.creationDepartment = department; await vm.onCreatePlan()
    expect(api.createOperation.mock.calls[0]![0]).toMatchObject({ departmentId: department, expectedAuthorityVersion: 'current', plan: { targetWarehouseCd: 'W01' } })
    expect(api.createPlan).not.toHaveBeenCalled(); expect(reference()).toBeNull(); expect(vm.lastSavedNo).toBe('ST1')
  })
  it('freezes the snapshot request, blocks duplicates and ignores a late old-identity receipt', async () => {
    const vm = render(); await flushPromises(); await vm.openPlan(); vm.planForm.targetWarehouseCd = 'W01'; vm.creationDepartment = department
    const response = pending(); api.createOperation.mockReturnValueOnce(response.promise)
    const saving = vm.onCreatePlan(); await flushPromises()
    await expect(vm.onCreatePlan()).rejects.toThrow(); vm.planForm.targetWarehouseCd = 'W02'
    expect(api.createOperation.mock.calls[0]![0].plan.targetWarehouseCd).toBe('W01')
    const operationId = reference(); identity(); response.resolve(wrap({ operationId, stockTakeId, stockTakeNo: 'OLD', departmentId: department }))
    await expect(saving).rejects.toThrow(); expect(ui.push).not.toHaveBeenCalled(); expect(ui.success).not.toHaveBeenCalled()
    expect(vm.lastSavedNo).toBe(''); expect(vm.creationContext).toBeNull(); expect(reference()).toBe(operationId)
  })
  it('recovers after reload and never replaces an unknown creation with a new submission', async () => {
    let vm = render(); await flushPromises(); await vm.openPlan(); vm.creationDepartment = department; vm.planForm.targetWarehouseCd = 'W01'
    api.createOperation.mockRejectedValueOnce(new Error('response lost')); await expect(vm.onCreatePlan()).rejects.toThrow()
    const operationId = reference(); expect(operationId).not.toBeNull()
    wrapper!.unmount(); wrapper = undefined; vm = render(); await flushPromises(); expect(vm.creationPending).toBe(operationId)
    await vm.openPlan(); expect(vm.planDialog).toBe(false); expect(api.createOperation).toHaveBeenCalledTimes(1)
    api.readCreation.mockResolvedValueOnce(wrap({ operationId, status: 'UNKNOWN' })); await vm.resolveCreation(false)
    expect(reference()).toBe(operationId)
    api.readCreation.mockResolvedValueOnce(wrap({ operationId, status: 'COMMITTED', result: { operationId, stockTakeId, stockTakeNo: 'ST1', departmentId: department } }))
    await vm.resolveCreation(false); expect(reference()).toBeNull(); expect(vm.lastSavedNo).toBe('ST1'); expect(api.createOperation).toHaveBeenCalledTimes(1)
  })
  it('does not need query for first creation and preserves an unresolved reference when recovery is denied', async () => {
    permissions.has.mockImplementation(key => key === 'wms-stocktake:add')
    const vm = render(); await flushPromises(); await vm.openPlan(); vm.creationDepartment = department; vm.planForm.targetWarehouseCd = 'W01'
    api.createOperation.mockRejectedValueOnce({ response: { status: 403 } }); await expect(vm.onCreatePlan()).rejects.toBeDefined()
    const operationId = reference(); expect(operationId).not.toBeNull(); expect(api.creationScope).not.toHaveBeenCalled()
    permissions.has.mockReturnValue(true); await vm.loadCreationScope(); api.readCreation.mockRejectedValueOnce({ response: { status: 403 } })
    await vm.resolveCreation(false); expect(reference()).toBe(operationId); expect(vm.lastSavedNo).toBe('')
  })
})
