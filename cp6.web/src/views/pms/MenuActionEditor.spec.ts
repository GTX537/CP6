import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
const api = vi.hoisted(() => ({ read: vi.fn(), readMenuActions: vi.fn(), saveMenuActions: vi.fn(), resolve: vi.fn() }))
vi.mock('@/api/sys/iamPolicy', () => ({ iamPolicyApi: api }))
vi.mock('@/api/sys/iam', () => ({ iamApi: api }))
import View from './RolePermView.vue'
import Editor from './MenuActionEditor.vue'
import { AUTH_EVENT } from '@/auth/identity'
const tenantId = '10000000-0000-0000-0000-000000000001', actorId = '20000000-0000-0000-0000-000000000002'
const snapshot = (menuId = 1) => ({ tenantId, actorId, menuId, version: 'v1', referencesVersion: 'refs1', catalogVersion: 'cat1',
  actions: [{ actionCode: 'read', actionName: 'Read', sort: 0 }] })
const global = { directives: { permission: {} }, stubs: {
  ElButton: { props: ['disabled', 'nativeType'], template: '<button :type="nativeType || \'button\'" :disabled="disabled"><slot /></button>' },
  ElAlert: { props: ['title'], template: '<p>{{title}}</p>' }
} }
let view: ReturnType<typeof mount> | undefined
async function render() {
  view = mount(Editor, { global }); await flushPromises()
  const vm = view.vm as any; vm.menuId = 1; await vm.loadMenu(false); vm.reason = 'Owned definition change'
  return vm
}
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks()
  api.read.mockResolvedValue({ tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002',
    mode: 'actions', roleId: null, version: 'v1', referencesVersion: 'refs1', roles: [], warnings: [],
    catalog: { version: 'cat1', menus: [{ menuId: 1, menuName: 'Orders', parentId: null, enable: true }],
      actions: [], scopes: [], fields: [], departments: [] }, function: { menuIds: [], actions: [] }, dataScopes: [], fieldPerms: [] })
  api.readMenuActions.mockImplementation(async id => snapshot(id))
  api.saveMenuActions.mockImplementation(async (_input, id) => ({ operationId: id, kind: 'menu-actions', state: 'COMMITTED' }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: () => Promise<unknown>) => run() } })
})
afterEach(() => view?.unmount())
it('offers action definition maintenance from the existing function permission page', async () => {
  view = mount(View, { global })
  await flushPromises()
  expect(view.text()).toContain('菜单动作定义')
})
it('saves the explicit full set and empty removal with all current versions', async () => {
  const vm = await render(); vm.actions = []; await vm.save()
  expect(api.saveMenuActions.mock.calls[0]![0]).toEqual({ menuId: 1, expectedVersion: 'v1', expectedReferencesVersion: 'refs1',
    expectedCatalogVersion: 'cat1', reason: 'Owned definition change', actions: [] })
  expect(vm.pending).toBeNull(); expect(vm.notice).toContain('已保存')
})
it('rejects duplicate codes and invalid sort without creating a recovery operation', async () => {
  const vm = await render(); vm.actions.push({ actionCode: 'read', actionName: 'Other', sort: 2 }); await vm.save()
  expect(api.saveMenuActions).not.toHaveBeenCalled()
  vm.actions = [{ actionCode: 'new', actionName: '', sort: 2147483648 }]; await vm.save()
  expect(api.saveMenuActions).not.toHaveBeenCalled(); expect(localStorage.getItem(`cp6_iam_operation_v1:${tenantId}:${actorId}`)).toBeNull()
})
it('preserves referenced actions after rejection and closes exact operation before a fresh reread', async () => {
  const vm = await render(); vm.actions = []
  api.saveMenuActions.mockRejectedValue({ response: { status: 409, data: { code: 'IAM_MENU_ACTION_REFERENCED' } } })
  await vm.save(); expect(vm.notice).toContain('仍被角色引用'); expect(vm.actions).toEqual([])
  const original = vm.pending; await vm.save(); expect(api.saveMenuActions).toHaveBeenCalledTimes(1)
  api.resolve.mockResolvedValue({ ...original, state: 'CLOSED' }); await vm.resolve(true)
  api.readMenuActions.mockResolvedValue({ ...snapshot(), version: 'v2', referencesVersion: 'refs2' }); await vm.loadMenu(true)
  expect(vm.actions).toEqual([]); expect(vm.reason).toBe('Owned definition change'); expect(vm.editor.version).toBe('v2')
  expect(api.resolve).toHaveBeenCalledWith(original.operationId, 'menu-actions', true)
})
it('preserves conflict draft only for the same target after closing and rereading', async () => {
  const vm = await render(); vm.actions[0].actionName = 'Draft'
  api.saveMenuActions.mockRejectedValue({ response: { status: 412 } }); await vm.save()
  expect(vm.conflict).toBe(true); const original = vm.pending
  api.resolve.mockResolvedValue({ ...original, state: 'CLOSED' }); await vm.resolve(true); await vm.loadMenu(true)
  expect(vm.actions[0].actionName).toBe('Draft')
  vm.menuId = 2; await vm.loadMenu(true); expect(vm.actions[0].actionName).toBe('Read'); expect(vm.reason).toBe('')
})
it('a reloaded tab can resolve uncertain original without selecting or replaying a menu write', async () => {
  const vm = await render(); api.saveMenuActions.mockRejectedValue(new Error('lost response')); await vm.save()
  const original = vm.pending; expect(JSON.stringify(localStorage)).not.toContain('Owned definition change')
  view!.unmount(); view = mount(Editor, { global }); await flushPromises(); const other = view.vm as any
  expect(other.menuId).toBeNull(); expect(other.pending.operationId).toBe(original.operationId)
  api.resolve.mockResolvedValue({ ...original, state: 'UNKNOWN' }); await other.resolve(false)
  expect(other.pending.operationId).toBe(original.operationId)
  api.resolve.mockResolvedValue({ ...original, state: 'COMMITTED' }); await other.resolve(false)
  expect(other.pending).toBeNull(); expect(other.notice).toContain('不再重放'); expect(api.saveMenuActions).toHaveBeenCalledTimes(1)
})
it('keeps commit success when its separate refresh fails and disallows another write', async () => {
  const vm = await render(); api.readMenuActions.mockRejectedValue(new Error('offline')); await vm.save()
  expect(vm.notice).toContain('菜单动作已保存'); expect(vm.needsReload).toBe(true); expect(vm.pending).toBeNull()
  await vm.save(); expect(api.saveMenuActions).toHaveBeenCalledTimes(1)
  api.readMenuActions.mockImplementation(async id => snapshot(id)); await vm.reload()
  expect(vm.needsReload).toBe(false); expect(api.saveMenuActions).toHaveBeenCalledTimes(1)
})
it('ignores late menu reads and clears values after identity changes or current denial', async () => {
  const vm = await render(); let release!: (v: ReturnType<typeof snapshot>) => void
  api.readMenuActions.mockImplementation(id => id === 1 ? new Promise(resolve => { release = resolve }) : Promise.resolve(snapshot(id)))
  const old = vm.loadMenu(false); vm.menuId = 2; await vm.loadMenu(false); release({ ...snapshot(), actions: [] }); await old
  expect(vm.editor.menuId).toBe(2); expect(vm.actions).toHaveLength(1)
  localStorage.setItem('cp6_auth_epoch', crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT))
  expect(vm.editor).toBeNull(); expect(vm.actions).toEqual([]); expect(vm.menus).toEqual([])
  api.read.mockRejectedValue({ response: { status: 403 } }); await vm.reload()
  expect(vm.scope).toBeNull()
})
it('requires a new read when another tab finishes the pending operation', async () => {
  const vm = await render(); api.saveMenuActions.mockRejectedValue(new Error('offline')); await vm.save()
  localStorage.removeItem(`cp6_iam_operation_v1:${tenantId}:${actorId}`); window.dispatchEvent(new StorageEvent('storage'))
  expect(vm.pending).toBeNull(); expect(vm.needsReload).toBe(true); await vm.save()
  expect(api.saveMenuActions).toHaveBeenCalledTimes(1)
})
