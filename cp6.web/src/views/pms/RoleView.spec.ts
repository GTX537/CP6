import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
const api = vi.hoisted(() => ({ roleEditor: vi.fn(), roleReferences: vi.fn(), saveRole: vi.fn(), resolve: vi.fn() }))
vi.mock('@/api/sys/iam', () => ({ iamApi: api }))
import RoleView from './RoleView.vue'
import { AUTH_EVENT } from '@/auth/identity'
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const role = { roleId: 0, roleName: 'Existing zero', description: null, enable: true, orderNo: 0, createDate: '2026-10-20', version: 'v1' }
const data = { ...scope, referencesVersion: 'refs1', roles: [role] }
let view: ReturnType<typeof mount> | undefined
async function render() {
  view = mount(RoleView, { global: { directives: { loading: {}, permission: {} }, stubs: {
    ElButton: { props: ['disabled', 'nativeType'], template: '<button :type="nativeType || \'button\'" :disabled="disabled"><slot /></button>' },
    ElAlert: { props: ['title'], template: '<p role="status">{{title}}</p>' }, ElTable: true, ElTableColumn: true,
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot /></div>' }
  } } }); await flushPromises(); return view.vm as any
}
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks(); api.roleEditor.mockResolvedValue(structuredClone(data))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: () => Promise<unknown>) => run() } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('role lifecycle editing', () => {
  it('edits role zero as an existing identity and sends only approved fields', async () => {
    const vm = await render(); await vm.open(0); vm.draft.roleName = 'renamed'
    api.saveRole.mockImplementation(async (_input, _create, id) => ({ operationId: id, kind: 'role-update', state: 'COMMITTED' }))
    await vm.save(); expect(api.saveRole).toHaveBeenCalledWith(expect.objectContaining({ roleId: 0, expectedVersion: 'v1' }), false, expect.any(String))
    expect(api.saveRole.mock.calls[0]![0]).not.toHaveProperty('tenantId'); expect(vm.visible).toBe(false)
  })
  it('preserves draft after CAS and explicitly closes original before refreshing versions', async () => {
    const vm = await render(); await vm.open(0); vm.draft.roleName = 'draft'
    api.saveRole.mockRejectedValue({ response: { status: 412 } }); await vm.save()
    expect(vm.conflict).toBe(true); expect(vm.draft.roleName).toBe('draft')
    const original = vm.pending; await vm.save(); expect(api.saveRole).toHaveBeenCalledTimes(1)
    api.resolve.mockResolvedValue({ ...original, state: 'CLOSED' }); await vm.resolve(true)
    api.roleEditor.mockResolvedValue({ ...data, referencesVersion: 'refs2', roles: [{ ...role, version: 'v2' }] }); await vm.open(0, true)
    expect(vm.draft.roleName).toBe('draft'); expect(vm.draft.expectedVersion).toBe('v2')
    expect(JSON.stringify(localStorage)).not.toContain('draft'); expect(api.saveRole).toHaveBeenCalledTimes(1)
  })
  it('clears state on permission loss and discards late editor responses after identity change', async () => {
    const vm = await render(); await vm.open(0)
    api.saveRole.mockRejectedValue({ response: { status: 403 } }); await vm.save()
    expect(vm.editor).toBeNull(); expect(vm.visible).toBe(false)
    let release!: (value: unknown) => void
    api.roleEditor.mockImplementation(() => new Promise(resolve => { release = resolve }))
    const late = vm.open(0); localStorage.setItem('cp6_auth_epoch', crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT))
    release(data); await late; expect(vm.editor).toBeNull(); expect(vm.draft.roleName).toBe('')
  })
  it('keeps committed status when refresh fails and never replays on refresh', async () => {
    const vm = await render(); await vm.open(0)
    api.saveRole.mockImplementation(async (_input, _create, id) => ({ operationId: id, kind: 'role-update', state: 'COMMITTED' }))
    api.roleEditor.mockRejectedValue(new Error('refresh failed')); await vm.save()
    expect(vm.notice).toContain('角色已保存'); expect(vm.pending).toBeNull()
    api.roleEditor.mockResolvedValue(data); await vm.load(); expect(api.saveRole).toHaveBeenCalledTimes(1)
  })
})
