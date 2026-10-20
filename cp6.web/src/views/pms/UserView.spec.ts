import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ userEditor: vi.fn(), saveUser: vi.fn(), resolve: vi.fn(), getList: vi.fn() }))
vi.mock('@/api/sys/iam', () => ({ iamApi: api }))
vi.mock('@/api/sys/user', () => ({ userApi: api }))
import UserView from './UserView.vue'
import { AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const value = { ...scope, referencesVersion: 'refs1', user: null, roles: [{ roleId: 71, roleName: 'role', enable: true }], departments: [], managers: [] }
let view: ReturnType<typeof mount> | undefined
async function render() {
  view = mount(UserView, { global: { directives: { loading: {}, permission: {} }, stubs: {
    ElInput: true, ElButton: { props: ['disabled', 'nativeType'], template: '<button :type="nativeType || \'button\'" :disabled="disabled"><slot /></button>' },
    ElAlert: { props: ['title'], template: '<p role="status">{{title}}</p>' }, ElTable: true, ElTableColumn: true, ElPagination: true,
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot /></div>' }
  } } }); await flushPromises(); return view.vm as any
}
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); api.userEditor.mockResolvedValue(structuredClone(value)); api.getList.mockResolvedValue({ rows: [], total: 0 })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: () => Promise<unknown>) => run() } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('user administration', () => {
  it('clears the form and list after current permission is withdrawn', async () => {
    const vm = await render(); await vm.openEditor(); vm.draft.userName = 'sensitive'; vm.draft.password = 'secret'
    api.saveUser.mockRejectedValue({ response: { status: 403 } }); await vm.save()
    expect(vm.editor).toBeNull(); expect(vm.visible).toBe(false); expect(vm.draft.userName).toBe(''); expect(vm.rows).toEqual([])
  })
  it('keeps nonsecret draft after CAS conflict and closes the original before loading new versions', async () => {
    const vm = await render(); await vm.openEditor(); vm.draft.userName = 'draft'; vm.draft.password = 'secret'; vm.draft.roleIds = [71]; vm.draft.roleId = 71; vm.draft.reason = 'approved'
    api.saveUser.mockRejectedValue({ response: { status: 412, data: { code: 'IAM_REFERENCES_CHANGED' } } }); await vm.save(); await flushPromises()
    expect(vm.conflict).toBe(true); expect(vm.draft.password).toBe(''); expect(vm.draft.userName).toBe('draft')
    expect(JSON.stringify(localStorage)).not.toContain('secret'); expect(JSON.stringify(localStorage)).not.toContain('draft')
    const original = vm.pending; await vm.save(); expect(api.saveUser).toHaveBeenCalledTimes(1)
    api.resolve.mockResolvedValue({ ...original, state: 'CLOSED' }); await vm.resolve(true)
    api.userEditor.mockResolvedValue({ ...value, referencesVersion: 'refs2' }); await vm.reloadDraft()
    expect(vm.draft.userName).toBe('draft'); expect(vm.draft.expectedReferencesVersion).toBe('refs2'); expect(vm.conflict).toBe(false)
    expect(api.saveUser).toHaveBeenCalledTimes(1)
  })
  it('sends primary and additional roles together and clears a primary removed from the set', async () => {
    const vm = await render(); await vm.openEditor(); vm.draft.roleIds = [71]; vm.draft.roleId = 71; await flushPromises()
    vm.draft.roleIds = []; await flushPromises(); expect(vm.draft.roleId).toBeNull()
    vm.draft.userName = 'new'; vm.draft.password = 'secret'; vm.draft.reason = 'approved'; vm.draft.roleIds = [71]; vm.draft.roleId = 71
    api.saveUser.mockImplementation(async (_input, id) => ({ operationId: id, kind: 'user-create', state: 'COMMITTED' }))
    await vm.save(); expect(api.saveUser.mock.calls[0]![0]).toMatchObject({ roleIds: [71], roleId: 71, expectedVersion: 'absent', expectedReferencesVersion: 'refs1' })
    expect(vm.visible).toBe(false); expect(vm.draft.password).toBe(''); expect(vm.pending).toBeNull()
  })
  it('restores pending state after page reload without replaying saved input', async () => {
    let vm = await render(); await vm.openEditor(); api.saveUser.mockRejectedValue(new Error('lost')); await vm.save()
    const original = vm.pending; view!.unmount(); vm = await render()
    expect(vm.pending).toEqual(original); await vm.save(); expect(api.saveUser).toHaveBeenCalledTimes(1)
    api.resolve.mockResolvedValue({ ...original, state: 'COMMITTED' }); await vm.resolve(false)
    expect(vm.pending).toBeNull(); expect(api.saveUser).toHaveBeenCalledTimes(1)
  })
  it('discards an editor response and sensitive draft when another tab changes identity', async () => {
    const vm = await render(); let complete!: (value: unknown) => void
    api.userEditor.mockImplementation(() => new Promise(resolve => { complete = resolve })); const opening = vm.openEditor()
    vm.draft.password = 'secret'; localStorage.setItem('userName', 'other'); window.dispatchEvent(new Event(AUTH_EVENT)); complete(value)
    await opening; expect(vm.editor).toBeNull(); expect(vm.visible).toBe(false); expect(vm.draft.password).toBe('')
  })
})
