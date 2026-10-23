import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ standaloneObjects: vi.fn(), standaloneMappings: vi.fn(), saveStandaloneMapping: vi.fn(), resolve: vi.fn() }))
vi.mock('@/api/sys/iam', () => ({ iamApi: api }))
import View from './StandaloneDepartmentMappings.vue'
import { AUTH_EVENT } from '@/auth/identity'
const tenantId = '10000000-0000-0000-0000-000000000001', actorId = '20000000-0000-0000-0000-000000000002'
const id = '30000000-0000-0000-0000-000000000001', dept = '40000000-0000-0000-0000-000000000001'
const editor = { tenantId, actorId, referencesVersion: 'refs1', mappings: [{ id, objectKind: 'stock', objectId: '50000000-0000-0000-0000-000000000001', objectName: 'W/L/P/LOT', departmentId: dept, version: 'v1', enabled: true, available: true,  }], departments: [{ id: dept, name: 'Sales team', available: true }] }
let view: ReturnType<typeof mount> | undefined
async function render() {
  view = mount(View, { global: { directives: { loading: {}, permission: {} }, stubs: {
    ElButton: { props: ['disabled', 'nativeType'], template: '<button :type="nativeType || \'button\'" :disabled="disabled"><slot /></button>' }
  } } }); await flushPromises(); return view.vm as any
}
beforeEach(() => {
  localStorage.clear(); vi.resetAllMocks(); api.standaloneMappings.mockResolvedValue(structuredClone(editor))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: () => Promise<unknown>) => run() } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('explicit standalone department binding', () => {
  it('selects a real cross-dock identity and submits its explicit department binding', async () => {
    const vm = await render(); await vm.open()
    await view!.get('[aria-label="对象类型"]').setValue('cross-dock')
    api.standaloneObjects.mockResolvedValue([{ id: 'cross-dock-uuid', name: 'XD-EXISTING' }])
    vm.search = 'XD'; await vm.findObjects(); expect(api.standaloneObjects).toHaveBeenCalledWith('cross-dock', 'XD')
    vm.draft.objectId = 'cross-dock-uuid'; vm.draft.departmentId = dept; vm.draft.reason = 'Verified existing record owner'
    api.saveStandaloneMapping.mockImplementation(async (_input, operationId) => ({ operationId, kind: 'dept-standalone-mapping', state: 'COMMITTED', entityId: id }))
    await vm.save(); expect(api.saveStandaloneMapping.mock.calls[0]![0]).toMatchObject({ objectKind: 'cross-dock', objectId: 'cross-dock-uuid', departmentId: dept })
  })
  it('selects a real slotting-plan identity and submits its explicit department binding', async () => {
    const vm = await render(); await vm.open()
    await view!.get('[aria-label="对象类型"]').setValue('slotting-plan')
    api.standaloneObjects.mockResolvedValue([{ id: 'slotting-plan-uuid', name: 'SLP-EXISTING' }])
    vm.search = 'SLP'; await vm.findObjects(); expect(api.standaloneObjects).toHaveBeenCalledWith('slotting-plan', 'SLP')
    vm.draft.objectId = 'slotting-plan-uuid'; vm.draft.departmentId = dept; vm.draft.reason = 'Verified existing record owner'
    api.saveStandaloneMapping.mockImplementation(async (_input, operationId) => ({ operationId, kind: 'dept-standalone-mapping', state: 'COMMITTED', entityId: id }))
    await vm.save(); expect(api.saveStandaloneMapping.mock.calls[0]![0]).toMatchObject({ objectKind: 'slotting-plan', objectId: 'slotting-plan-uuid', departmentId: dept })
  })
  it('selects a real machine identity and submits its explicit department binding', async () => {
    const vm = await render(); await vm.open()
    await view!.get('[aria-label="对象类型"]').setValue('machine')
    api.standaloneObjects.mockResolvedValue([{ id: 'machine-uuid', name: 'MC1 / Verified machine' }])
    vm.search = 'MC1'; await vm.findObjects(); expect(api.standaloneObjects).toHaveBeenCalledWith('machine', 'MC1')
    vm.draft.objectId = 'machine-uuid'; vm.draft.departmentId = dept; vm.draft.reason = 'Verified equipment owner'
    api.saveStandaloneMapping.mockImplementation(async (_input, operationId) => ({ operationId, kind: 'dept-standalone-mapping', state: 'COMMITTED', entityId: id }))
    await vm.save(); expect(api.saveStandaloneMapping.mock.calls[0]![0]).toMatchObject({ objectKind: 'machine', objectId: 'machine-uuid', departmentId: dept })
  })
  it('shows impact, preserves the original code and explicitly submits target, reason and CAS', async () => {
    const vm = await render(); expect(view!.text()).toContain('Sales team'); expect(view!.text()).toContain('W/L/P/LOT')
    await vm.open(id); expect(view!.get<HTMLSelectElement>('[aria-label="对象类型"]').element.disabled).toBe(true)
    vm.draft.enabled = false; vm.draft.reason = 'No longer authoritative'
    api.saveStandaloneMapping.mockImplementation(async (_input, operationId) => ({ operationId, kind: 'dept-standalone-mapping', state: 'COMMITTED', entityId: id }))
    await vm.save()
    expect(api.saveStandaloneMapping.mock.calls[0]![0]).toMatchObject({ id, objectKind: 'stock', objectId: '50000000-0000-0000-0000-000000000001', departmentId: dept, enabled: false, reason: 'No longer authoritative', expectedVersion: 'v1', expectedReferencesVersion: 'refs1' })
    expect(vm.pending).toBeNull(); expect(vm.editing).toBe(false)
  })
  it('keeps the draft on CAS conflict but requires closing and rereading before a new save', async () => {
    const vm = await render(); await vm.open(id); vm.draft.reason = 'Keep draft'
    api.saveStandaloneMapping.mockRejectedValue({ response: { status: 412 } }); await vm.save()
    expect(vm.conflict).toBe(true); expect(vm.draft.reason).toBe('Keep draft')
    await vm.save(); expect(api.saveStandaloneMapping).toHaveBeenCalledTimes(1)
    api.resolve.mockResolvedValue({ ...vm.pending, state: 'CLOSED' }); await vm.resolve(true)
    await vm.save(); expect(api.saveStandaloneMapping).toHaveBeenCalledTimes(1)
    api.standaloneMappings.mockResolvedValue({ ...editor, referencesVersion: 'refs2' }); await vm.open(id, true)
    expect(vm.draft.reason).toBe('Keep draft'); expect(vm.draft.expectedReferencesVersion).toBe('refs2'); expect(vm.conflict).toBe(false)
    expect(api.saveStandaloneMapping).toHaveBeenCalledTimes(1)
  })
  it('resumes the original uncertain operation after remount without saving the draft or replaying a write', async () => {
    let vm = await render(); await vm.open(); vm.draft.objectId = editor.mappings[0]!.objectId; vm.draft.departmentId = dept; vm.draft.reason = 'private mapping rationale'
    api.saveStandaloneMapping.mockRejectedValue(new Error('ack lost')); await vm.save(); const original = vm.pending
    expect(JSON.stringify(localStorage)).not.toContain('private mapping rationale')
    view!.unmount(); vm = await render(); expect(vm.pending).toEqual(original)
    await vm.open(); expect(vm.editing).toBe(false)
    api.resolve.mockResolvedValue({ ...original, state: 'COMMITTED' }); await vm.resolve(false)
    expect(vm.pending).toBeNull(); expect(api.saveStandaloneMapping).toHaveBeenCalledTimes(1)
  })
  it('clears old identity data and refuses its delayed editor response', async () => {
    const vm = await render(); let complete!: (value: unknown) => void
    api.standaloneMappings.mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
    const opening = vm.open(id); localStorage.setItem('userName', 'different'); window.dispatchEvent(new Event(AUTH_EVENT))
    complete(editor); await opening; expect(vm.editor).toBeNull(); expect(vm.editing).toBe(false)
    expect(view!.text()).not.toContain('Sales team')
  })
  it.each([401, 403])('clears all protected data when save loses authority (%s)', async status => {
    const vm = await render(); await vm.open(id); vm.draft.reason = 'private'
    api.saveStandaloneMapping.mockRejectedValue({ response: { status } }); await vm.save()
    expect(vm.editor).toBeNull(); expect(vm.draft.reason).toBe(''); expect(vm.editing).toBe(false)
  })
  it('keeps committed success separate from refresh failure and never replays', async () => {
    const vm = await render(); await vm.open(id); vm.draft.reason = 'confirmed'
    api.saveStandaloneMapping.mockImplementation(async (_input, operationId) => ({ operationId, kind: 'dept-standalone-mapping', state: 'COMMITTED' }))
    api.standaloneMappings.mockRejectedValue(new Error('offline')); await vm.save()
    expect(vm.pending).toBeNull(); expect(vm.editor).toBeNull(); expect(vm.editing).toBe(false); expect(vm.notice).toContain('已保存')
    await vm.save(); expect(api.saveStandaloneMapping).toHaveBeenCalledTimes(1)
  })
  it('requires a searched object and clears a selection when object kind changes', async () => {
    const vm = await render(); await vm.open(); vm.draft.departmentId = dept; vm.draft.reason = 'verified'
    await vm.save(); expect(api.saveStandaloneMapping).not.toHaveBeenCalled()
    api.standaloneObjects.mockResolvedValue([{ id: editor.mappings[0]!.objectId, name: 'W/L/P/LOT' }])
    vm.search = 'P'; await vm.findObjects(); expect(api.standaloneObjects).toHaveBeenCalledWith('stock', 'P')
    vm.draft.objectId = editor.mappings[0]!.objectId; vm.draft.objectKind = 'work-order'; vm.resetObject()
    expect(vm.draft.objectId).toBe(''); expect(vm.objects).toEqual([])
    await vm.save(); expect(api.saveStandaloneMapping).not.toHaveBeenCalled()
  })
  it('discards an object lookup completed after an identity change', async () => {
    const vm = await render(); await vm.open(); vm.search = 'P'
    let complete!: (value: unknown) => void
    api.standaloneObjects.mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
    const finding = vm.findObjects(); localStorage.setItem('userName', 'different'); window.dispatchEvent(new Event(AUTH_EVENT))
    complete([{ id, name: 'private object' }]); await finding
    expect(vm.objects).toEqual([]); expect(vm.editor).toBeNull(); expect(vm.draft.objectId).toBe('')
  })

})
