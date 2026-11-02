import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { nextTick } from 'vue'
import MenuView from './MenuView.vue'
import { usePlatformStore } from '@/stores/platform'

const api = vi.hoisted(() => ({ getAll: vi.fn(), add: vi.fn(), update: vi.fn(), updateTree: vi.fn(), del: vi.fn(), profile: vi.fn(), confirm: vi.fn(), success: vi.fn(),
  treeCurrent: vi.fn(), treePreview: vi.fn(), treeExecute: vi.fn(), treeOriginal: vi.fn() }))
vi.mock('@/api/sys/menu', () => ({ menuApi: api }))
vi.mock('@/api/sys/auth', () => ({ authApi: { profile: api.profile } }))
vi.mock('@/api/sys/rolePerm', () => ({ rolePermApi: { myActions: vi.fn() } }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('element-plus', async importOriginal => ({ ...await importOriginal<object>(),
  ElMessage: { success: api.success, error: vi.fn(), warning: vi.fn() }, ElMessageBox: { confirm: api.confirm } }))

const menu = (menuName = '原菜单') => ({ menuId: 1, menuName, routePath: '/menu', menuKey: 'menu', icon: null, parentId: null, orderNo: 1, enable: true })
const profile = (menuName = '新导航') => ({ userName: 'one', menus: [{ id: 1, menuName, routePath: '/menu', icon: null, parentId: null, orderNo: 1 }] })
function deferred() {
  let resolve!: (value?: any) => void, reject!: (reason: unknown) => void
  const promise = new Promise<any>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
let wrapper: ReturnType<typeof mount> | undefined
async function render() {
  const pinia = createPinia()
  wrapper = mount(MenuView, { global: { plugins: [pinia], directives: { permission: {}, loading: {} }, stubs: {
    ElButton: { props: ['loading', 'disabled'], template: '<button :disabled="loading || disabled"><slot /></button>' },
    ElForm: { template: '<form><slot /></form>', methods: { validate: () => Promise.resolve(true), clearValidate: () => {} } },
    ElFormItem: { template: '<div><slot /></div>' }, ElInput: true, ElInputNumber: true,
    ElTree: { template: '<div />', methods: { setCurrentKey: () => {}, filter: () => {} } },
    ElIcon: true, ElTooltip: true, ElTag: true, ElDropdown: true, ElDrawer: true,
    ElSelect: true, ElOption: true, ElSwitch: true, ElSegmented: true, ElButtonGroup: true,
  } } })
  await flushPromises()
  // Exercise actual component handlers; API boundaries alone are mocked.
  return { view: wrapper, vm: wrapper.vm as any, pinia }
}
async function edit(vm: any, name = '已保存') { vm.editorForm.menuName = name; await nextTick() }
function writes() { return [api.add, api.update, api.updateTree, api.del, api.treeExecute].reduce((n, fn) => n + fn.mock.calls.length, 0) }
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear()
  localStorage.setItem('cp6_authed', '1'); localStorage.setItem('userName', 'one')
  localStorage.setItem('menus', JSON.stringify(profile('旧导航').menus))
  api.getAll.mockResolvedValue([menu()]); api.profile.mockResolvedValue(profile())
  api.confirm.mockResolvedValue('confirm'); api.update.mockResolvedValue(menu('已保存'))
  api.add.mockResolvedValue(menu()); api.updateTree.mockResolvedValue(undefined); api.del.mockResolvedValue(undefined)
  api.treeCurrent.mockResolvedValue({ revision: '9007199254740993', menus: [menu()] })
  api.treePreview.mockResolvedValue({ revision: '9007199254740993', changedMenuIds: [1], affectedTenantsCount: 2, affectedRolesCount: 3, menuReferences: '4', actionReferences: '5' })
  api.treeExecute.mockImplementation(async command => ({ operationRef: command.operationRef, appliedRevision: '9007199254740994' }))
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })

describe('menu write confirmation and independent read recovery (#243)', () => {
  it('still refreshes navigation after confirmed save when list GET fails', async () => {
    const { view, vm } = await render(); await edit(vm)
    api.getAll.mockRejectedValueOnce(new Error('list offline'))
    await vm.saveDetails().catch(() => {})
    expect(api.update).toHaveBeenCalledTimes(1)
    expect(api.profile).toHaveBeenCalledTimes(1)
    expect(view.text()).toContain('写入已确认成功')
    expect(view.text()).toContain('重试列表读取')
    expect(JSON.parse(localStorage.getItem('menus')!)).toEqual(profile().menus)
  })
  it('shows profile failure and recovers only that GET', async () => {
    const { view, vm } = await render(); await edit(vm)
    api.profile.mockRejectedValueOnce(new Error('profile offline'))
    await vm.saveDetails()
    expect(view.text()).toContain('重试导航读取')
    const reads = api.getAll.mock.calls.length
    await vm.refreshActiveNavigation()
    expect(writes()).toBe(1); expect(api.getAll).toHaveBeenCalledTimes(reads)
    expect(view.text()).not.toContain('重试导航读取')
  })
  it('starts both reads independently and recovers both without menu writes', async () => {
    const { vm } = await render(); await edit(vm)
    const list = deferred(); api.getAll.mockReturnValueOnce(list.promise)
    api.profile.mockRejectedValueOnce(new Error('profile offline'))
    const save = vm.saveDetails(); await flushPromises()
    expect(api.profile).toHaveBeenCalledTimes(1)
    list.reject(new Error('list offline')); await save
    await vm.refreshData(); await vm.refreshActiveNavigation()
    expect(writes()).toBe(1); expect(api.getAll).toHaveBeenCalledTimes(3); expect(api.profile).toHaveBeenCalledTimes(2)
  })
  it('does not claim success or retry writes after an ambiguous write failure', async () => {
    const { view, vm } = await render(); await edit(vm)
    api.update.mockRejectedValueOnce(new Error('timeout'))
    await vm.saveDetails().catch(() => {})
    expect(api.success).not.toHaveBeenCalled(); expect(api.profile).not.toHaveBeenCalled()
    expect(vm.formDirty).toBe(true); expect(view.text()).toContain('未确认写入结果')
    await vm.refreshData()
    expect(writes()).toBe(1); expect(view.text()).not.toContain('写入已确认成功')
  })
  it('asks before discarding a new draft; cancel or GET failure preserves it', async () => {
    const { vm } = await render(); await edit(vm, '新草稿')
    api.confirm.mockRejectedValueOnce('cancel'); await vm.refreshData()
    expect(api.getAll).toHaveBeenCalledTimes(1)
    api.getAll.mockRejectedValueOnce(new Error('offline')); await vm.refreshData().catch(() => {})
    expect(vm.editorForm.menuName).toBe('新草稿'); expect(vm.formDirty).toBe(true)
    await vm.refreshData(); expect(vm.editorForm.menuName).toBe('原菜单')
  })
  it('does not replace edits or a new selection made while a read is in flight', async () => {
    const { vm } = await render(); const list = deferred()
    api.getAll.mockReturnValueOnce(list.promise); const read = vm.refreshData()
    await edit(vm, '读取中的新草稿'); list.resolve([menu('迟到列表')]); await read
    expect(vm.editorForm.menuName).toBe('读取中的新草稿'); expect(vm.formDirty).toBe(true)
    expect(wrapper!.text()).toContain('重试列表读取')
  })
  it('preserves a new create draft made during the post-save list read', async () => {
    const { vm } = await render(); await edit(vm)
    const list = deferred(); api.getAll.mockReturnValueOnce(list.promise)
    const save = vm.saveDetails(); await flushPromises()
    await vm.openCreate(null); await edit(vm, '新的创建草稿')
    list.resolve([menu('已保存')]); await save
    expect(vm.isCreating).toBe(true); expect(vm.editorForm.menuName).toBe('新的创建草稿')
    expect(vm.formDirty).toBe(true); expect(writes()).toBe(1)
  })
  it('keeps a tree draft made during a tree write and does not replay it', async () => {
    localStorage.setItem('cp6_isPlatformAdmin', '1')
    const { vm } = await render(); vm.handleNodeDrop(); await nextTick()
    const write = deferred(); api.treeExecute.mockReturnValueOnce(write.promise)
    const save = vm.saveStructure(); await flushPromises()
    vm.menuTree[0].orderNo = 9
    vm.structureDirty = true
    write.resolve({ appliedRevision: 'next' }); await save
    expect(vm.structureDirty).toBe(true); expect(vm.menuTree[0].orderNo).toBe(9)
    expect(api.treeCurrent).toHaveBeenCalledTimes(1); expect(api.profile).toHaveBeenCalledTimes(1)
    expect(writes()).toBe(1)
  })
  it('rejects old success and old failure after a newer read', async () => {
    const { vm } = await render()
    for (const fails of [false, true]) {
      const old = deferred(); api.getAll.mockReturnValueOnce(old.promise).mockResolvedValueOnce([menu('最新列表')])
      const first = vm.loadData(); await vm.loadData()
      if (fails) old.reject(new Error('late failure')); else old.resolve([menu('过时列表')])
      await first; expect(vm.editorForm.menuName).toBe('最新列表'); expect(wrapper!.text()).not.toContain('重试列表读取')
    }
  })
  it.each(['missing', 'bad-item', 'cycle'])('does not publish malformed profile: %s', async kind => {
    const { vm } = await render(); const old = localStorage.getItem('menus')
    const event = vi.fn(); window.addEventListener('cp6-menu-updated', event)
    try {
      api.profile.mockResolvedValueOnce(kind === 'missing' ? {} : { menus: kind === 'bad-item' ? [{}] : [{ ...profile().menus[0], parentId: 1 }] })
      await vm.refreshActiveNavigation()
      expect(localStorage.getItem('menus')).toBe(old); expect(event).not.toHaveBeenCalled()
      expect(wrapper!.text()).toContain('重试导航读取')
      api.profile.mockResolvedValueOnce({ menus: [] }); await vm.refreshActiveNavigation()
      expect(localStorage.getItem('menus')).toBe('[]'); expect(event).toHaveBeenCalledTimes(1)
    } finally { window.removeEventListener('cp6-menu-updated', event) }
  })
  it.each(['unmount', 'storage', 'response-check', 'impersonation'])('drops both reads after %s', async kind => {
    const { vm, pinia } = await render(); const list = deferred(), nav = deferred()
    const old = localStorage.getItem('menus')
    api.getAll.mockReturnValueOnce(list.promise); api.profile.mockReturnValueOnce(nav.promise)
    const reads = Promise.all([vm.loadData(), vm.refreshActiveNavigation()])
    if (kind === 'unmount') { wrapper!.unmount(); wrapper = undefined }
    else if (kind === 'impersonation') usePlatformStore(pinia).setImpersonation({ tenantName: 'other', userName: 'other', expiresAt: 99 })
    else { localStorage.setItem('userName', 'other'); if (kind === 'storage') window.dispatchEvent(new Event('storage')) }
    list.resolve([menu('过时列表')]); nav.resolve(profile('过时导航')); await reads
    expect(localStorage.getItem('menus')).toBe(old); expect(vm.editorForm.menuName).not.toBe('过时列表')
  })
  it('discards an older navigation request once a new write starts', async () => {
    const { vm } = await render(); const old = deferred()
    api.profile.mockReturnValueOnce(old.promise); const read = vm.refreshActiveNavigation()
    await edit(vm); await vm.saveDetails(); old.resolve(profile('过时导航')); await read
    expect(JSON.parse(localStorage.getItem('menus')!)).toEqual(profile().menus)
  })
  it('does not let old navigation errors replace a newer successful read', async () => {
    const { vm } = await render(); const old = deferred()
    api.profile.mockReturnValueOnce(old.promise)
    const read = vm.refreshActiveNavigation(); await vm.refreshActiveNavigation()
    old.reject(new Error('late failure')); await read
    expect(wrapper!.text()).not.toContain('重试导航读取')
    expect(JSON.parse(localStorage.getItem('menus')!)).toEqual(profile().menus)
  })
  it('retains stored menus and exposes retry when local publication fails', async () => {
    const { vm } = await render(); const old = localStorage.getItem('menus')
    const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementationOnce(() => { throw new Error('quota') })
    try {
      await vm.refreshActiveNavigation()
      expect(localStorage.getItem('menus')).toBe(old); expect(wrapper!.text()).toContain('重试导航读取')
    } finally { storage.mockRestore() }
  })
  it('does not process a write completion after identity changes', async () => {
    const { vm } = await render(); await edit(vm)
    const write = deferred(); api.update.mockReturnValueOnce(write.promise)
    const save = vm.saveDetails(); await flushPromises()
    localStorage.setItem('userName', 'other'); window.dispatchEvent(new Event('focus'))
    write.resolve(menu('已保存')); await save
    expect(api.success).not.toHaveBeenCalled(); expect(api.profile).not.toHaveBeenCalled()
    expect(vm.formDirty).toBe(true)
    await vm.saveDetails(); expect(writes()).toBe(1)
  })
  it.each(['create', 'delete', 'tree'])('uses the same recovery boundary for %s', async kind => {
    if (kind === 'tree') localStorage.setItem('cp6_isPlatformAdmin', '1')
    const { vm } = await render()
    api.getAll.mockRejectedValueOnce(new Error('offline')); api.profile.mockRejectedValueOnce(new Error('offline'))
    if (kind === 'tree') api.treeCurrent.mockRejectedValueOnce(new Error('offline'))
    if (kind === 'create') { await vm.openCreate(null); await vm.saveDetails().catch(() => {}) }
    if (kind === 'delete') await vm.deleteSelected().catch(() => {})
    if (kind === 'tree') { vm.handleNodeDrop(); await vm.saveStructure() }
    expect(writes()).toBe(1); expect(api.profile).toHaveBeenCalledTimes(1)
    await vm.refreshData(); await vm.refreshActiveNavigation(); expect(writes()).toBe(1)
  })
})

describe('versioned shared tree commands', () => {
  beforeEach(() => { localStorage.setItem('cp6_isPlatformAdmin', '1') })
  it('previews global references and preserves exact large version before committing', async () => {
    const { vm } = await render(); vm.handleNodeDrop(); await nextTick(); await vm.saveStructure()
    expect(api.confirm).toHaveBeenCalledWith(expect.stringContaining('2 个租户、3 个角色'), '共享菜单树影响', expect.anything())
    expect(api.treeExecute.mock.calls[0]![0]).toEqual(api.treePreview.mock.calls[0]![0])
    expect(api.treeExecute.mock.calls[0]![0].expectedRevision).toBe('9007199254740993')
    expect(api.updateTree).not.toHaveBeenCalled(); expect(api.profile).toHaveBeenCalledTimes(1)
  })
  it('preserves conflict draft and reads differences without enabling blind overwrite', async () => {
    const { vm, view } = await render(); vm.handleNodeDrop(); await nextTick()
    api.treeExecute.mockRejectedValueOnce({ response: { status: 412, data: { code: 'MENU_TREE_VERSION_CHANGED' } } })
    await vm.saveStructure(); expect(vm.structureDirty).toBe(true); expect(vm.treePending).toBeNull(); expect(vm.treeRevision).toBeNull()
    api.treeCurrent.mockResolvedValueOnce({ revision: 'new', menus: [menu('别人修改')] })
    await vm.inspectCurrentTree(); expect(view.text()).toContain('旧草稿保留'); expect(vm.treeRevision).toBeNull()
    await vm.saveStructure(); expect(api.treeExecute).toHaveBeenCalledTimes(1)
  })
  it('restores unknown across remount and keeps exact request through query miss and later denial', async () => {
    let page = await render(); page.vm.handleNodeDrop(); await nextTick()
    api.treeExecute.mockRejectedValueOnce(new Error('connection lost')); await page.vm.saveStructure()
    const original = structuredClone(api.treeExecute.mock.calls[0]![0]); wrapper!.unmount(); wrapper = undefined
    page = await render(); expect(page.vm.treePending).toEqual(original)
    api.treeOriginal.mockRejectedValueOnce({ response: { status: 404 } }); await page.vm.recoverTree(false)
    api.treeExecute.mockRejectedValueOnce({ response: { status: 403, data: { code: 'MENU_TREE_ORIGINAL_FORBIDDEN' } } }); await page.vm.recoverTree(true)
    expect(page.vm.treePending).toEqual(original); await page.vm.recoverTree(true)
    expect(api.treeExecute.mock.calls.at(-1)![0]).toEqual(original); expect(page.vm.treePending).toBeNull()
    expect(page.view.text()).toContain('原操作已确认'); expect(api.profile).toHaveBeenCalledTimes(1)
  })
  it('does not send when durable pending storage fails', async () => {
    const { vm } = await render(); vm.handleNodeDrop(); await nextTick()
    const original = Storage.prototype.setItem
    const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function(this: Storage, key, value) {
      if (key.startsWith('cp6-menu-tree-pending-v1:')) throw new Error('quota')
      return original.call(this, key, value)
    })
    try { await vm.saveStructure(); expect(api.treeExecute).not.toHaveBeenCalled(); expect(vm.treeStorageError).toContain('本次尚未发送') }
    finally { storage.mockRestore() }
  })
  it('ignores delayed commit after same-name session epoch changes', async () => {
    const { vm } = await render(); vm.handleNodeDrop(); await nextTick(); const pending = deferred(); api.treeExecute.mockReturnValueOnce(pending.promise)
    const save = vm.saveStructure(); await flushPromises()
    localStorage.setItem('cp6_auth_epoch', 'new-session'); window.dispatchEvent(new Event('cp6-auth-changed'))
    pending.resolve({ appliedRevision: 'next' }); await save
    expect(vm.identityChanged).toBe(true); expect(vm.treePending).not.toBeNull(); expect(api.profile).not.toHaveBeenCalled()
  })
  it('requires platform identity for tree writes and never falls back after version read fails', async () => {
    api.treeCurrent.mockRejectedValueOnce(new Error('offline')); const { vm, pinia } = await render()
    expect(api.getAll).not.toHaveBeenCalled(); expect(vm.canDrag).toBe(false)
    usePlatformStore(pinia).isPlatformAdmin = false; vm.handleNodeDrop(); await vm.saveStructure()
    expect(api.treePreview).not.toHaveBeenCalled(); expect(api.treeExecute).not.toHaveBeenCalled()
  })
})
