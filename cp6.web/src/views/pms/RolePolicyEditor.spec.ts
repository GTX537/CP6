import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import type { IamPolicyEditor, IamPolicyMode } from '@/api/sys/iamPolicy'
const api = vi.hoisted(() => ({ read: vi.fn(), save: vi.fn(), resolve: vi.fn() }))
vi.mock('@/api/sys/iamPolicy', () => ({ iamPolicyApi: api }))
vi.mock('@/api/sys/iam', () => ({ iamApi: api }))
import View from './RolePolicyEditor.vue'
import { AUTH_EVENT } from '@/auth/identity'
function snapshot(mode: IamPolicyMode, roleId: number | null = null): IamPolicyEditor {
  return {
    tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002',
    mode, roleId, version: 'v1', referencesVersion: 'refs1', roles: [{ roleId: 72, roleName: 'A', enable: true }, { roleId: 73, roleName: 'B', enable: true }],
    catalog: { version: 'cat1', menus: [{ menuId: 1, menuName: 'Orders', menuKey: 'order', parentId: null, enable: true }],
      actions: [{ menuId: 1, actionCode: 'read', actionName: 'Read', sort: 0 }], scopes: [{ key: 'order', name: 'Orders', supports: [1, 4, 5], default: 1 }],
      departments: [{ id: 'd1', deptName: 'Own', available: true }], fields: [{ key: 'order', fields: [{ name: 'Amount', label: 'Amount' }, { name: 'Name', label: 'Name' }] }, { key: 'other', fields: [{ name: 'Cost', label: 'Cost' }] }] },
    function: { menuIds: [1], actions: [{ menuId: 1, actionCode: 'read' }] }, dataScopes: [], fieldPerms: [{ resourceKey: 'order', fieldName: 'Amount', access: 3 }], warnings: []
  }
}
let view: ReturnType<typeof mount> | undefined
async function render(mode: IamPolicyMode = 'fields') {
  view = mount(View, { props: { mode }, global: { directives: { permission: {} }, stubs: {
    ElButton: { props: ['disabled', 'nativeType'], template: '<button :type="nativeType || \'button\'" :disabled="disabled"><slot /></button>' },
    ElAlert: { props: ['title'], template: '<p role="status">{{title}}</p>' }
  } } }); await flushPromises(); const vm = view.vm as any; await vm.load(72); return vm
}
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks()
  api.read.mockImplementation(async (mode, roleId) => snapshot(mode, roleId ?? null))
  api.save.mockImplementation(async (mode, _role, _input, id) => ({ operationId: id, kind: `role-policy-${mode}`, state: 'COMMITTED' }))
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, _options: unknown, run: () => Promise<unknown>) => run() } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('closed role policy editor', () => {
  it('preserves navigation ancestors and removes descendant grants when a parent is unchecked', async () => {
    const vm = await render('actions')
    vm.editor.catalog.menus.push({ menuId: 2, menuName: 'Child', menuKey: 'child', parentId: 1, enable: true })
    vm.menuIds = []; vm.actions = []
    vm.toggleMenu(2, true); expect(vm.menuIds).toEqual([2, 1])
    vm.actions = [{ menuId: 2, actionCode: 'read' }]
    vm.toggleMenu(1, false); expect(vm.menuIds).toEqual([]); expect(vm.actions).toEqual([])
    vm.toggleMenu(1, true); expect(vm.menuIds).toEqual([1, 2])
    vm.toggleMenu(2, false); expect(vm.menuIds).toEqual([])
  })
  it('sends the complete explicit field resource including all defaults, never the other resource', async () => {
    const vm = await render(); vm.fields[0].access = 1; vm.reason = 'reset fields'; await vm.save()
    expect(api.save.mock.calls[0]![2]).toMatchObject({ resourceKey: 'order', fields: [{ resourceKey: 'order', fieldName: 'Amount', access: 1 }, { resourceKey: 'order', fieldName: 'Name', access: 1 }] })
    expect(api.save.mock.calls[0]![2]).not.toHaveProperty('scopes')
    expect(vm.pending).toBeNull()
  })
  it('preserves scope draft after CAS, closes original, then reads new catalog before explicit save', async () => {
    const vm = await render('scopes'); vm.scopes[0].scopeType = 4; vm.scopes[0].customDeptIds = ['d1']; vm.reason = 'scope draft'
    api.save.mockRejectedValue({ response: { status: 412 } }); await vm.save()
    expect(vm.conflict).toBe(true); const original = vm.pending; await vm.save(); expect(api.save).toHaveBeenCalledTimes(1)
    api.resolve.mockResolvedValue({ ...original, state: 'CLOSED' }); await vm.resolve(true)
    api.read.mockResolvedValue({ ...snapshot('scopes', 72), version: 'v2', referencesVersion: 'refs2' }); await vm.load(72, true)
    expect(vm.scopes[0]).toMatchObject({ scopeType: 4, customDeptIds: ['d1'] }); expect(vm.reason).toBe('scope draft')
    expect(vm.editor.version).toBe('v2'); expect(api.save).toHaveBeenCalledTimes(1)
    expect(JSON.stringify(localStorage)).not.toContain('scope draft')
  })
  it('discards a late role read after another role was selected', async () => {
    const vm = await render('actions'); let release!: (v: IamPolicyEditor) => void
    api.read.mockImplementation((_mode, role) => role === 72 ? new Promise(resolve => { release = resolve }) : Promise.resolve(snapshot('actions', 73)))
    const old = vm.load(72); await vm.load(73); release({ ...snapshot('actions', 72), function: { menuIds: [999], actions: [] } }); await old
    expect(vm.selectedRoleId).toBe(73); expect(vm.menuIds).toEqual([1]); expect(vm.editor.roleId).toBe(73)
  })
  it('clears permissions and drafts on current denial or shared identity change', async () => {
    const vm = await render(); api.save.mockRejectedValue({ response: { status: 403 } }); await vm.save()
    expect(vm.editor).toBeNull(); expect(vm.fields).toEqual([]); expect(vm.roles).toEqual([])
    await vm.load(72); vm.reason = 'draft'; localStorage.setItem('cp6_auth_epoch', crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT))
    expect(vm.editor).toBeNull(); expect(vm.reason).toBe('')
  })
  it('keeps commit success and blocks another save while the separate refresh is unavailable', async () => {
    const vm = await render(); api.read.mockRejectedValue(new Error('offline')); await vm.save()
    expect(vm.notice).toContain('授权配置已保存'); expect(vm.needsReload).toBe(true)
    await vm.save(); expect(api.save).toHaveBeenCalledTimes(1)
    api.read.mockResolvedValue(snapshot('fields', 72)); await vm.load(72); expect(api.save).toHaveBeenCalledTimes(1)
  })
  it('drops actions when the associated menu is removed and strips departments for a noncustom scope', async () => {
    const vm = await render('actions'); vm.toggleMenu(1, false); expect(vm.actions).toEqual([]); await vm.save()
    expect(api.save.mock.calls[0]![2]).toMatchObject({ menuIds: [], actions: [] })
    vm.scopes = [{ resourceKey: 'order', scopeType: 1, customDeptIds: ['d1'] }]; vm.normalizeScope('order'); expect(vm.scopes[0].customDeptIds).toEqual([])
  })
})
