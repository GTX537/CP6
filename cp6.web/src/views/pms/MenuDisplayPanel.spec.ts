import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import MenuDisplayPanel from './MenuDisplayPanel.vue'
const api = vi.hoisted(() => ({ treeCurrent: vi.fn(), displayPreview: vi.fn(), displayExecute: vi.fn(), treeOriginal: vi.fn(), confirm: vi.fn() }))
vi.mock('@/api/sys/menu', () => ({ menuApi: api }))
vi.mock('element-plus', () => ({ ElMessageBox: { confirm: api.confirm } }))
const node = { menuId: 1, menuName: '名称 ', icon: null, enable: true, parentId: null, orderNo: 0 }
let wrapper: ReturnType<typeof mount> | undefined
async function render() {
  wrapper = mount(MenuDisplayPanel, { props: { menuId: 1 }, global: { stubs: {
    ElButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
    ElForm: { template: '<form><slot /></form>' }, ElFormItem: { template: '<div><slot /></div>' }, ElInput: true, ElSwitch: true, ElCheckbox: true,
  } } }); await flushPromises(); return wrapper.vm as any
}
beforeEach(() => {
  vi.resetAllMocks(); localStorage.clear(); sessionStorage.clear(); localStorage.setItem('cp6_auth_epoch', 'display-one')
  api.treeCurrent.mockResolvedValue({ revision: '9007199254740993', menus: [node] })
  api.displayPreview.mockResolvedValue({ affectedTenantsCount: 2, affectedRolesCount: 3 })
  api.confirm.mockResolvedValue('confirm'); api.displayExecute.mockImplementation(async command => ({ operationRef: command.operationRef, appliedRevision: 'next' }))
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })
it('preserves exact fields and bigint version, previews and confirms before writing', async () => {
  const vm = await render(); Object.assign(vm.draft, { menuName: '新名称 ', reason: '原因 ', noIcon: false, icon: '', enable: false }); await vm.submit()
  const command = api.displayExecute.mock.calls[0]![0]
  expect(command).toEqual(api.displayPreview.mock.calls[0]![0]); expect(command).toMatchObject({ expectedRevision: '9007199254740993', menuName: '新名称 ', icon: '', enable: false, reason: '原因 ' })
  expect(wrapper!.emitted('changed')).toHaveLength(1); expect(vm.pending).toBeNull()
})
it('keeps conflict draft and lets explicit current read supply the new version', async () => {
  const vm = await render(); vm.draft.menuName = '草稿'; vm.draft.reason = '编辑'
  api.displayExecute.mockRejectedValueOnce({ response: { status: 412, data: { code: 'MENU_TREE_VERSION_CHANGED' } } }); await vm.submit()
  expect(vm.revision).toBeNull(); expect(vm.draft.menuName).toBe('草稿'); expect(vm.pending).toBeNull()
  api.treeCurrent.mockResolvedValue({ revision: 'new', menus: [{ ...node, menuName: '别人编辑' }] }); await vm.load(true)
  expect(vm.draft.menuName).toBe('草稿'); expect(vm.current.menuName).toBe('别人编辑'); await vm.submit()
  expect(api.displayExecute.mock.calls.at(-1)![0].expectedRevision).toBe('new')
})
it('survives refresh and retains unknown through 404 and authorization denial before exact retry', async () => {
  let vm = await render(); vm.draft.reason = '编辑'; api.displayExecute.mockRejectedValueOnce(new Error('unknown')); await vm.submit()
  const original = structuredClone(api.displayExecute.mock.calls[0]![0]); wrapper!.unmount(); wrapper = undefined; vm = await render()
  expect(vm.pending).toEqual(original); api.treeOriginal.mockRejectedValueOnce({ response: { status: 404 } }); await vm.recover(false)
  api.displayExecute.mockRejectedValueOnce({ response: { status: 403 } }); await vm.recover(true); expect(vm.pending).toEqual(original)
  await vm.recover(true); expect(api.displayExecute.mock.calls.at(-1)![0]).toEqual(original); expect(vm.pending).toBeNull()
})
it('rejects obsolete selected-node reads and old-identity commit completion', async () => {
  const vm = await render(); let finish!: (v: unknown) => void
  api.treeCurrent.mockReturnValueOnce(new Promise(r => { finish = r })); const reading = vm.load(true); await wrapper!.setProps({ menuId: 2 })
  finish({ revision: 'late', menus: [node] }); await reading; expect(vm.current).toBeNull(); expect(vm.revision).toBeNull()
  await wrapper!.setProps({ menuId: 1 }); await flushPromises(); vm.draft.reason = '编辑'
  api.displayExecute.mockReturnValueOnce(new Promise(r => { finish = r })); const writing = vm.submit(); await flushPromises()
  localStorage.setItem('cp6_auth_epoch', 'display-other'); window.dispatchEvent(new Event('cp6-auth-changed')); finish({ appliedRevision: 'late' }); await writing
  expect(wrapper!.emitted('changed')).toBeUndefined(); expect(wrapper!.text()).toBe(''); expect(vm.pending).not.toBeNull()
})
it('storage failure sends no write and read failure does not fabricate current values', async () => {
  api.treeCurrent.mockRejectedValueOnce(new Error('read unavailable')); const vm = await render(); expect(vm.revision).toBeNull()
  await vm.load(false); vm.draft.reason = '编辑'; const storage = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
  try { await vm.submit(); expect(api.displayExecute).not.toHaveBeenCalled(); expect(vm.storageError).toContain('本次尚未发送') } finally { storage.mockRestore() }
})
