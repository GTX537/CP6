import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ departmentEditor: vi.fn(), departmentPreview: vi.fn(), departmentReferences: vi.fn(), saveDepartment: vi.fn(), resolve: vi.fn() }))
vi.mock('@/api/sys/iam', () => ({ iamApi: api }))
import View from './DeptTreeView.vue'
import { AUTH_EVENT } from '@/auth/identity'
const tenantId = '10000000-0000-0000-0000-000000000001', actorId = '20000000-0000-0000-0000-000000000002'
const a = '30000000-0000-0000-0000-000000000001', b = '30000000-0000-0000-0000-000000000002', child = '30000000-0000-0000-0000-000000000003'
const department = (id: string, name: string, parentId: string | null = null) => ({ id, deptCode: name, deptName: name, parentId, leaderId: null, sort: 0, enable: true, available: true, diagnostic: null, version: 'v1', path: '/' + id + '/' })
const editor = { tenantId, actorId, treeVersion: 'tree1', referencesVersion: 'refs1', departments: [department(a, 'A'), department(b, 'B'), department(child, 'Child', a)], leaders: [] }
const preview = { id: child, oldParentId: a, newParentId: b, treeVersion: 'tree1', referencesVersion: 'refs1', version: 'v1', affectedDescendantCount: 3, affectedUsersCount: 7, scopeRefs: [], approvalRouteWarnings: [], previewVersion: 'preview1' }
let view: ReturnType<typeof mount> | undefined
async function render() {
  view = mount(View, { global: { directives: { loading: {}, permission: {} }, stubs: {
    ElButton: { props: ['disabled', 'nativeType'], template: '<button :type="nativeType || \'button\'" :disabled="disabled"><slot /></button>' },
    ElAlert: { props: ['title'], template: '<p role="status">{{title}}</p>' }, ElTable: true, ElTableColumn: true,
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot /></div>' }
  } } }); await flushPromises(); return view.vm as any
}
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); api.departmentEditor.mockResolvedValue(structuredClone(editor)); api.departmentPreview.mockResolvedValue(structuredClone(preview))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: () => Promise<unknown>) => run() } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('organization change and recovery', () => {
  it('requires current target preview and discards a late preview after changing the target', async () => {
    const vm = await render(); await vm.open('dept-move', child); vm.draft.parentId = b; await flushPromises()
    await vm.save(); expect(api.saveDepartment).not.toHaveBeenCalled()
    let complete!: (data: unknown) => void; api.departmentPreview.mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
    const preparing = vm.prepareMove(); vm.draft.parentId = null; await flushPromises(); complete(preview); await preparing
    expect(vm.preview).toBeNull(); expect(vm.busy).toBe(false)
    vm.draft.parentId = b; await flushPromises(); await vm.prepareMove(); expect(view!.text()).toContain('用户 7 人')
    api.saveDepartment.mockImplementation(async (_input, kind, id) => ({ operationId: id, kind, state: 'COMMITTED', entityId: child }))
    await vm.save(); expect(api.saveDepartment).toHaveBeenCalledTimes(1)
    expect(api.saveDepartment.mock.calls[0]![0]).toMatchObject({ id: child, parentId: b, previewVersion: 'preview1', expectedTreeVersion: 'tree1' })
  })
  it('keeps draft through 412 but requires closing and a new preview before another explicit move', async () => {
    const vm = await render(); await vm.open('dept-move', child); vm.draft.parentId = b; vm.draft.reason = 'keep this reason'; await flushPromises(); await vm.prepareMove()
    api.saveDepartment.mockRejectedValue({ response: { status: 412, data: { code: 'IAM_REFERENCES_CHANGED' } } }); await vm.save()
    expect(vm.conflict).toBe(true); expect(vm.preview).toBeNull(); expect(vm.draft.reason).toBe('keep this reason'); const original = vm.pending
    await vm.save(); expect(api.saveDepartment).toHaveBeenCalledTimes(1)
    api.resolve.mockResolvedValue({ ...original, state: 'CLOSED' }); await vm.resolve(true); await vm.reloadDraft()
    expect(vm.draft.parentId).toBe(b); expect(vm.draft.reason).toBe('keep this reason'); await vm.save(); expect(api.saveDepartment).toHaveBeenCalledTimes(1)
    expect(vm.preview).toBeNull(); expect(vm.pending).toBeNull()
  })
  it('recovers a reload by original operation without persisting draft or replaying a move', async () => {
    let vm = await render(); await vm.open('dept-create'); vm.draft.deptName = 'private draft'; api.saveDepartment.mockRejectedValue(new Error('ack lost')); await vm.save()
    const original = vm.pending; expect(JSON.stringify(localStorage)).not.toContain('private draft'); view!.unmount(); vm = await render()
    expect(vm.pending).toEqual(original); await vm.save(); expect(api.saveDepartment).toHaveBeenCalledTimes(1)
    api.resolve.mockResolvedValue({ ...original, state: 'COMMITTED' }); await vm.resolve(false)
    expect(vm.pending).toBeNull(); expect(api.saveDepartment).toHaveBeenCalledTimes(1)
  })
  it('clears old identity and ignores its delayed preview', async () => {
    const vm = await render(); await vm.open('dept-move', child); vm.draft.parentId = b; await flushPromises()
    let complete!: (data: unknown) => void; api.departmentPreview.mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
    const preparing = vm.prepareMove(); localStorage.setItem('userName', 'another'); window.dispatchEvent(new Event(AUTH_EVENT)); complete(preview); await preparing
    expect(vm.editor).toBeNull(); expect(vm.preview).toBeNull(); expect(vm.tree).toEqual([]); expect(vm.visible).toBe(false)
  })
  it('shows anomalies separately and clears all page data on permission withdrawal', async () => {
    const bad = { ...department('40000000-0000-0000-0000-000000000001', 'orphan', 'missing'), available: false, diagnostic: 'IAM_TREE_UNHEALTHY' }
    api.departmentEditor.mockResolvedValue({ ...editor, departments: [...editor.departments, bad] }); const vm = await render()
    expect(vm.tree.map((x: any) => x.id)).toEqual([a, b]); expect(view!.text()).toContain('orphan')
    await vm.open('dept-update', child); vm.draft.deptName = 'sensitive'; api.saveDepartment.mockRejectedValue({ response: { status: 403 } }); await vm.save()
    expect(vm.editor).toBeNull(); expect(vm.draft.deptName).toBe(''); expect(vm.visible).toBe(false)
  })
  it('keeps committed success distinct from a failed directory refresh', async () => {
    const vm = await render(); await vm.open('dept-create')
    api.saveDepartment.mockImplementation(async (_input, kind, id) => ({ operationId: id, kind, state: 'COMMITTED' }))
    api.departmentEditor.mockRejectedValue(new Error('directory offline')); await vm.save()
    expect(vm.pending).toBeNull(); expect(vm.visible).toBe(false); expect(vm.notice).toContain('Main 已保存')
    expect(api.saveDepartment).toHaveBeenCalledTimes(1)
  })
})
