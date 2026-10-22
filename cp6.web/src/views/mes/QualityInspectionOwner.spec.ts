import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const api = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), create: vi.fn(), update: vi.fn(), getTemplate: vi.fn(), getAllTemplateCodes: vi.fn() }))
const work = vi.hoisted(() => ({ get: vi.fn() }))
const messages = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn() }))
vi.mock('@/api/mes/mes', () => ({ qualityInspectionApi: api, workOrderApi: work }))
vi.mock('element-plus', () => ({ ElMessage: messages }))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }), useRouter: () => ({ push: vi.fn() }) }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
import Entry from './QualityInspectionEntryView.vue'
import List from './QualityInspectionListView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let view: ReturnType<typeof shallowMount> | undefined
function render(component: any = Entry): any {
  view = shallowMount(component, { global: { directives: { permission: {} }, config: { warnHandler: () => {} }, stubs: { ElCard: { template: '<div><slot/></div>' }, ElTable: true } } })
  return view.vm
}
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
const draft = { inspectionNo: '', workOrderNo: 'WO1', inspectorCd: 'OP', inspectionQty: 1.12345678, remarks: 'original', items: [] }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks()
  for (const mock of Object.values(api)) mock.mockReset()
  api.getAllTemplateCodes.mockResolvedValue({ data: [] }); api.search.mockResolvedValue({ data: { total: 0, items: [] } })
  api.create.mockResolvedValue({ data: { inspectionNo: 'QI1' } }); api.update.mockResolvedValue({ code: 0 }); work.get.mockReset().mockResolvedValue({ data: {} })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('quality inspection Owner pages', () => {
  it('freezes a write, deduplicates it, and binds the confirmed number without erasing pending edits', async () => {
    const vm = render(); await flushPromises(); Object.assign(vm.form, draft)
    const request = pending(); api.create.mockReturnValueOnce(request.promise)
    const save = vm.onSave(); await vm.onSave(); vm.form.remarks = 'new edit'
    expect(api.create).toHaveBeenCalledTimes(1); expect(api.create.mock.calls[0]![0].remarks).toBe('original')
    request.resolve({ data: { inspectionNo: 'CONFIRMED' } }); await save
    expect(vm.form.inspectionNo).toBe('CONFIRMED'); expect(vm.form.remarks).toBe('new edit'); expect(vm.lastSavedNo).toBe('CONFIRMED')
    api.get.mockRejectedValueOnce({ response: { status: 403 } }); await vm.onSave()
    expect(api.update).toHaveBeenCalledTimes(1); expect(api.create).toHaveBeenCalledTimes(1); expect(vm.intent.uncertain.value).toBe(false)
  })
  it('retains confirmed identity on read failure and blocks another create after an unknown result', async () => {
    const vm = render(); await flushPromises(); Object.assign(vm.form, draft)
    api.get.mockRejectedValueOnce({ response: { status: 403 } }); await vm.onSave()
    expect(vm.form.inspectionNo).toBe('QI1'); expect(vm.lastSavedNo).toBe('QI1'); expect(vm.readError).toBeTruthy()
    api.update.mockRejectedValue({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } })
    await vm.onSave(); await vm.onSave(); expect(api.update).toHaveBeenCalledTimes(1); expect(vm.intent.uncertain.value).toBe(true)
  })
  it('ignores old identity saves and late detail/template results after local edits', async () => {
    const vm = render(); await flushPromises(); Object.assign(vm.form, draft)
    const detail = pending(); api.get.mockReturnValueOnce(detail.promise); const read = vm.loadByNo('OLD'); vm.form.workOrderNo = 'NEW'
    detail.resolve({ code: 0, data: { ...draft, inspectionNo: 'OLD' } }); await read; expect(vm.form.workOrderNo).toBe('NEW')
    vm.form.templateCd = 'T1'; const tpl = pending(); api.getTemplate.mockReturnValueOnce(tpl.promise); const template = vm.loadTemplate(); vm.form.remarks = 'edited'
    tpl.resolve({ data: [{ itemSeqNo: 1, itemName: 'old template' }] }); await template; expect(vm.form.items).toEqual([])
    Object.assign(vm.form, draft); const request = pending(); api.create.mockReturnValueOnce(request.promise); const save = vm.onSave(); identity()
    request.resolve({ data: { inspectionNo: 'OLD' } }); await save; expect(vm.form.inspectionNo).toBe(''); expect(vm.lastSavedNo).toBe(''); expect(messages.success).not.toHaveBeenCalled()
  })
  it('clears denied list results and never exports a prior identity snapshot', async () => {
    const vm = render(List); await flushPromises()
    const request = pending(); api.search.mockReturnValueOnce(request.promise); const old = vm.search(); await vm.search()
    request.resolve({ data: { total: 1, items: [{ inspectionNo: 'OLD' }] } }); await old; expect(vm.rows).toEqual([])
    api.search.mockResolvedValueOnce({ data: { total: 1, items: [{ ...draft, inspectionNo: 'PRIVATE' }] } }); await vm.search()
    api.search.mockRejectedValueOnce({ response: { status: 403 } }); await vm.search(); expect(vm.rows).toEqual([])
    api.search.mockResolvedValueOnce({ data: { total: 1, items: [{ ...draft, inspectionNo: 'PRIVATE' }] } }); await vm.search()
    localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); vm.exportCsv(); expect(vm.rows).toEqual([]); expect(vm.total).toBe(0)
  })
})
