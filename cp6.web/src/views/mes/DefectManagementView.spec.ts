import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), getCategories: vi.fn() }))
const messages = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), confirm: vi.fn() }))
vi.mock('@/api/mes/mes', () => ({ defectRecordApi: api }))
vi.mock('element-plus', () => ({ ElMessage: messages, ElMessageBox: { confirm: messages.confirm } }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
import View from './DefectManagementView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let view: ReturnType<typeof shallowMount> | undefined
function render(): any {
  view = shallowMount(View, { global: { directives: { permission: {} }, config: { warnHandler: () => {} }, stubs: {
    ElCard: { template: '<div><slot/></div>' }, ElTable: true, ElDialog: true,
  } } }); return view.vm
}
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
const draft = { defectNo: '', workOrderNo: 'WO1', categoryCd: 'D07', defectDescription: 'observed', defectQty: 1.12345678 }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks()
  for (const mock of Object.values(api)) mock.mockReset()
  api.search.mockResolvedValue({ code: 0, data: { total: 0, items: [] } }); api.getCategories.mockResolvedValue({ code: 0, data: [] })
  api.create.mockResolvedValue({ code: 0, data: { defectNo: 'DF1' } }); api.update.mockResolvedValue({ code: 0 }); api.delete.mockResolvedValue({ code: 0 })
  messages.confirm.mockReset().mockResolvedValue(true)
})
afterEach(() => { view?.unmount(); view = undefined })
describe('independent defect Owner page', () => {
  it('freezes create and keeps its confirmed identity when edits or refresh failures occur', async () => {
    const vm = render(); await flushPromises(); vm.openCreate(); Object.assign(vm.form, draft)
    const request = pending(); api.create.mockReturnValueOnce(request.promise)
    const save = vm.onSave(); await vm.onSave(); vm.form.defectDescription = 'edited while pending'
    expect(api.create).toHaveBeenCalledTimes(1); expect(api.create.mock.calls[0]![0].defectDescription).toBe('observed')
    api.search.mockRejectedValueOnce({ response: { status: 403 } }); request.resolve({ code: 0, data: { defectNo: 'CONFIRMED' } }); await save
    expect(vm.lastSavedNo).toBe('CONFIRMED'); expect(vm.form.defectNo).toBe('CONFIRMED'); expect(vm.form.defectDescription).toBe('edited while pending')
    expect(vm.intent.uncertain.value).toBe(false); expect(vm.readError).toBeTruthy()
    await vm.onSave(); expect(api.update).toHaveBeenCalledTimes(1); expect(api.create).toHaveBeenCalledTimes(1)
  })
  it('blocks repeat writes and new drafts after an unknown result, retaining the last confirmed number', async () => {
    const vm = render(); await flushPromises(); vm.openCreate(); Object.assign(vm.form, draft); await vm.onSave(); expect(vm.lastSavedNo).toBe('DF1')
    vm.openCreate(); Object.assign(vm.form, draft); api.create.mockRejectedValue({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } })
    await vm.onSave(); await vm.onSave(); await vm.onDelete({ defectNo: 'DF1' }); vm.openCreate()
    expect(api.create).toHaveBeenCalledTimes(2); expect(api.delete).not.toHaveBeenCalled(); expect(vm.lastSavedNo).toBe('DF1'); expect(vm.intent.uncertain.value).toBe(true)
  })
  it('freezes delete confirmation target, deduplicates it and rejects old identity confirmations', async () => {
    const vm = render(); await flushPromises(); const first = pending(); messages.confirm.mockReturnValueOnce(first.promise)
    const row = { defectNo: 'DF1' }; const remove = vm.onDelete(row); row.defectNo = 'DF2'; await vm.onDelete(row)
    first.resolve(true); await remove; expect(api.delete).toHaveBeenCalledExactlyOnceWith('DF1')
    const second = pending(); messages.confirm.mockReturnValueOnce(second.promise); const old = vm.onDelete(row); identity(); second.resolve(true); await old
    expect(api.delete).toHaveBeenCalledTimes(1)
  })
  it('does not resurrect old detail or list results after newer requests, drafts or identity changes', async () => {
    const vm = render(); await flushPromises(); const old = pending(); api.get.mockReturnValueOnce(old.promise)
    const read = vm.openEdit({ defectNo: 'OLD' }); vm.openCreate(); Object.assign(vm.form, draft); old.resolve({ code: 0, data: { ...draft, defectNo: 'OLD' } }); await read
    expect(vm.form.defectNo).toBe(''); expect(vm.dialogVisible).toBe(true)
    const page = pending(); api.search.mockReturnValueOnce(page.promise); const search = vm.search(); await vm.search(); page.resolve({ data: { total: 99, items: [{ defectNo: 'OLD' }] } }); await search
    expect(vm.total).toBe(0); expect(vm.rows).toEqual([])
    const categories = pending(); api.getCategories.mockReturnValueOnce(categories.promise); const load = vm.loadCategories(); identity(); categories.resolve({ data: [{ categoryCd: 'PRIVATE' }] }); await load
    expect(vm.allCategories).toEqual([]); expect(vm.form.defectNo).toBe(''); expect(vm.dialogVisible).toBe(false)
  })
  it('clears denied reads and ignores save completion from an old identity', async () => {
    const vm = render(); await flushPromises(); api.search.mockResolvedValueOnce({ data: { total: 1, items: [{ defectNo: 'PRIVATE' }] } }); await vm.search()
    api.search.mockRejectedValueOnce({ response: { status: 403 } }); await vm.search(); expect(vm.rows).toEqual([]); expect(vm.total).toBe(0)
    vm.openCreate(); Object.assign(vm.form, draft); const request = pending(); api.create.mockReturnValueOnce(request.promise); const save = vm.onSave()
    identity(); request.resolve({ data: { defectNo: 'OLD' } }); await save; expect(vm.lastSavedNo).toBe(''); expect(messages.success).not.toHaveBeenCalled()
  })
  it('does not classify cancelled deletion as an unknown write or let a pending detail reopen a deleted target', async () => {
    const vm = render(); await flushPromises(); messages.confirm.mockRejectedValueOnce('cancel'); await vm.onDelete({ defectNo: 'DF1' })
    expect(vm.intent.uncertain.value).toBe(false); expect(api.delete).not.toHaveBeenCalled()
    const detail = pending(); api.get.mockReturnValueOnce(detail.promise); const read = vm.openEdit({ defectNo: 'DF1' }); await vm.onDelete({ defectNo: 'DF1' })
    detail.resolve({ code: 0, data: { ...draft, defectNo: 'DF1' } }); await read; expect(vm.dialogVisible).toBe(false)
  })
  it('does not clear a new draft on a late dialog close or export a snapshot from an old identity', async () => {
    const vm = render(); await flushPromises(); vm.openCreate(); Object.assign(vm.form, draft); vm.onDialogClosed(); expect(vm.form.workOrderNo).toBe('WO1')
    api.search.mockResolvedValueOnce({ data: { total: 1, items: [{ ...draft, defectNo: 'DF1' }] } }); await vm.search()
    localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); vm.exportCsv(); expect(vm.rows).toEqual([]); expect(vm.total).toBe(0)
  })
})
