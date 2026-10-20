import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ orderMappings: vi.fn(), saveOrderMapping: vi.fn(), resolve: vi.fn() }))
vi.mock('@/api/sys/iam', () => ({ iamApi: api }))
import View from './OrderDepartmentMappings.vue'
import { AUTH_EVENT } from '@/auth/identity'
const tenantId = '10000000-0000-0000-0000-000000000001', actorId = '20000000-0000-0000-0000-000000000002'
const id = '30000000-0000-0000-0000-000000000001', dept = '40000000-0000-0000-0000-000000000001'
const editor = { tenantId, actorId, referencesVersion: 'refs1', mappings: [{ id, code: 'SALES', departmentId: dept, version: 'v1', enabled: true, available: true, orderCount: 7 }], departments: [{ id: dept, name: 'Sales team', available: true }] }
let view: ReturnType<typeof mount> | undefined
async function render() {
  view = mount(View, { global: { directives: { loading: {}, permission: {} }, stubs: {
    ElButton: { props: ['disabled', 'nativeType'], template: '<button :type="nativeType || \'button\'" :disabled="disabled"><slot /></button>' }
  } } }); await flushPromises(); return view.vm as any
}
beforeEach(() => {
  localStorage.clear(); vi.resetAllMocks(); api.orderMappings.mockResolvedValue(structuredClone(editor))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: () => Promise<unknown>) => run() } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('explicit order department binding', () => {
  it('shows impact, preserves the original code and explicitly submits target, reason and CAS', async () => {
    const vm = await render(); expect(view!.text()).toContain('Sales team'); expect(view!.text()).toContain('7')
    await vm.open(id); expect(view!.get<HTMLInputElement>('[aria-label="订单部门编码"]').element.disabled).toBe(true)
    vm.draft.enabled = false; vm.draft.reason = 'No longer authoritative'
    api.saveOrderMapping.mockImplementation(async (_input, operationId) => ({ operationId, kind: 'dept-order-mapping', state: 'COMMITTED', entityId: id }))
    await vm.save()
    expect(api.saveOrderMapping.mock.calls[0]![0]).toMatchObject({ id, code: 'SALES', departmentId: dept, enabled: false, reason: 'No longer authoritative', expectedVersion: 'v1', expectedReferencesVersion: 'refs1' })
    expect(vm.pending).toBeNull(); expect(vm.editing).toBe(false)
  })
  it('keeps the draft on CAS conflict but requires closing and rereading before a new save', async () => {
    const vm = await render(); await vm.open(id); vm.draft.reason = 'Keep draft'
    api.saveOrderMapping.mockRejectedValue({ response: { status: 412 } }); await vm.save()
    expect(vm.conflict).toBe(true); expect(vm.draft.reason).toBe('Keep draft')
    await vm.save(); expect(api.saveOrderMapping).toHaveBeenCalledTimes(1)
    api.resolve.mockResolvedValue({ ...vm.pending, state: 'CLOSED' }); await vm.resolve(true)
    await vm.save(); expect(api.saveOrderMapping).toHaveBeenCalledTimes(1)
    api.orderMappings.mockResolvedValue({ ...editor, referencesVersion: 'refs2' }); await vm.open(id, true)
    expect(vm.draft.reason).toBe('Keep draft'); expect(vm.draft.expectedReferencesVersion).toBe('refs2'); expect(vm.conflict).toBe(false)
    expect(api.saveOrderMapping).toHaveBeenCalledTimes(1)
  })
  it('resumes the original uncertain operation after remount without saving the draft or replaying a write', async () => {
    let vm = await render(); await vm.open(); vm.draft.reason = 'private mapping rationale'
    api.saveOrderMapping.mockRejectedValue(new Error('ack lost')); await vm.save(); const original = vm.pending
    expect(JSON.stringify(localStorage)).not.toContain('private mapping rationale')
    view!.unmount(); vm = await render(); expect(vm.pending).toEqual(original)
    await vm.open(); expect(vm.editing).toBe(false)
    api.resolve.mockResolvedValue({ ...original, state: 'COMMITTED' }); await vm.resolve(false)
    expect(vm.pending).toBeNull(); expect(api.saveOrderMapping).toHaveBeenCalledTimes(1)
  })
  it('clears old identity data and refuses its delayed editor response', async () => {
    const vm = await render(); let complete!: (value: unknown) => void
    api.orderMappings.mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
    const opening = vm.open(id); localStorage.setItem('userName', 'different'); window.dispatchEvent(new Event(AUTH_EVENT))
    complete(editor); await opening; expect(vm.editor).toBeNull(); expect(vm.editing).toBe(false)
    expect(view!.text()).not.toContain('Sales team')
  })
  it.each([401, 403])('clears all protected data when save loses authority (%s)', async status => {
    const vm = await render(); await vm.open(id); vm.draft.reason = 'private'
    api.saveOrderMapping.mockRejectedValue({ response: { status } }); await vm.save()
    expect(vm.editor).toBeNull(); expect(vm.draft.reason).toBe(''); expect(vm.editing).toBe(false)
  })
  it('keeps committed success separate from refresh failure and never replays', async () => {
    const vm = await render(); await vm.open(id)
    api.saveOrderMapping.mockImplementation(async (_input, operationId) => ({ operationId, kind: 'dept-order-mapping', state: 'COMMITTED' }))
    api.orderMappings.mockRejectedValue(new Error('offline')); await vm.save()
    expect(vm.pending).toBeNull(); expect(vm.editor).toBeNull(); expect(vm.editing).toBe(false); expect(vm.notice).toContain('已保存')
    await vm.save(); expect(api.saveOrderMapping).toHaveBeenCalledTimes(1)
  })
})
