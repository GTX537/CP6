import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, disposePinia, type Pinia } from 'pinia'
const api = vi.hoisted(() => ({ searchList: vi.fn(), exportListCsv: vi.fn(), getByWebOrderNo: vi.fn(),
  lookupByHaibaiNo: vi.fn(), creditCheck: vi.fn(), create: vi.fn(), update: vi.fn() }))
const messages = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn(), confirm: vi.fn() }))
vi.mock('@/api/http', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }))
vi.mock('@/api/erp/order', () => ({ orderApi: api }))
vi.mock('element-plus', () => ({ ElMessage: messages, ElMessageBox: { confirm: messages.confirm } }))
vi.mock('vue-i18n', async importOriginal => ({ ...await importOriginal<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
import OrderListView from './OrderListView.vue'
import OrderEntryView from './OrderEntryView.vue'
import { AUTH_EVENT, AUTH_EPOCH } from '@/auth/identity'

let pinia: Pinia
let view: ReturnType<typeof mount> | undefined
function render(entry = false): any {
  pinia = createPinia()
  view = mount(entry ? OrderEntryView : OrderListView, { global: { plugins: [pinia],
    directives: { loading: {}, permission: {} }, stubs: {
      CpListPage: { props: ['columns', 'fetch'], template: '<div><span v-for="c in columns" :key="c.prop" data-column>{{c.prop}}</span><slot name="toolbar" /></div>' },
      ElCard: { template: '<div><slot /></div>' }, ElSteps: true, ElStep: true, ElInput: true,
      ElTag: true, ElForm: { template: '<div><slot /></div>' }, ElFormItem: { template: '<div><slot /></div>' },
      ElRadioGroup: true, ElRadioButton: true, ElCheckbox: true, ElIcon: true, CpTag: true,
      ElButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
      Step1HeaderAndDetails: true, Step2BasicInfo: true, Step3ProcessInfo: true, OrderCancelDialog: true,
    } } })
  return view.vm as any
}
function changeIdentity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
const query = { page: 1, size: 20, filters: {} }
const row = { webOrderNo: 'ORDER-1', webOrderDetailNo: 1, individualUnitPrice: 17, amount: 34 }
const document = { webOrderNo: 'ORDER-1', customerCd: 'C', orderType: '1', details: [{ ...row, productCd: 'P', processes: [], processNotes: [], materials: [] }] }
beforeEach(() => {
  localStorage.clear(); vi.clearAllMocks()
  Object.values(api).forEach(mock => mock.mockReset())
  messages.confirm.mockReset().mockResolvedValue('confirm')
  api.creditCheck.mockResolvedValue({ code: 0, data: { isOver: false } })
})
afterEach(() => { view?.unmount(); view = undefined; if (pinia) disposePinia(pinia) })

describe('ordinary Order protected reads', () => {
  it('keeps same-identity draft after leaving the page but clears it on identity change while unmounted', async () => {
    const vm = render(true), store = vm.store
    store.loadFromDto(document); store.markDirty()
    view!.unmount(); view = undefined
    expect(store.order.webOrderNo).toBe('ORDER-1'); expect(store.isDirty).toBe(true)
    changeIdentity()
    expect(store.order.details).toEqual([]); expect(store.isDirty).toBe(false)
  })
  it('hides list columns and clears failed results instead of retaining earlier values', async () => {
    const vm = render()
    api.searchList.mockResolvedValueOnce({ code: 0, data: { rows: [row], total: 1 } })
    await vm.fetchList(query); await flushPromises()
    expect(view!.text()).toContain('individualUnitPrice')
    api.searchList.mockResolvedValueOnce({ code: 0, data: { rows: [{ ...row, priceReadable: false, amountReadable: false }], total: 1 } })
    await vm.fetchList(query); await flushPromises()
    expect(view!.text()).not.toContain('individualUnitPrice'); expect(view!.text()).not.toContain('amount')
    api.searchList.mockRejectedValueOnce({ response: { status: 403 } })
    expect(await vm.fetchList(query)).toEqual({ rows: [], total: 0 })
  })
  it('rejects stale identity or older search results and clears cancel selection', async () => {
    const vm = render(); let resolve!: (value: unknown) => void
    api.searchList.mockImplementationOnce(() => new Promise(r => { resolve = r }))
    const old = vm.fetchList(query)
    vm.openCancelDialog(row); changeIdentity()
    resolve({ code: 0, data: { rows: [row], total: 1 } })
    expect(await old).toEqual({ rows: [], total: 0 }); expect(vm.cancelDialogVisible).toBe(false)
    api.searchList.mockImplementationOnce(() => new Promise(r => { resolve = r }))
    const older = vm.fetchList(query)
    api.searchList.mockResolvedValueOnce({ code: 0, data: { rows: [], total: 0 } })
    await vm.fetchList(query); resolve({ code: 0, data: { rows: [row], total: 1 } })
    expect(await older).toEqual({ rows: [], total: 0 }); expect(vm.showPrices).toBe(false)
  })
  it('does not download a completed blob belonging to a previous identity or duplicate the request', async () => {
    const vm = render(); let resolve!: (value: Blob) => void
    api.exportListCsv.mockImplementationOnce(() => new Promise(r => { resolve = r }))
    const create = vi.fn(); URL.createObjectURL = create
    const request = vm.onExportCsv(); await vm.onExportCsv()
    expect(api.exportListCsv).toHaveBeenCalledTimes(1)
    changeIdentity(); resolve(new Blob(['protected']))
    await request; expect(create).not.toHaveBeenCalled()
  })
  it('clears previous detail on denied read and does not accept a late response after New', async () => {
    const vm = render(true); vm.store.loadFromDto(document); vm.searchNo = 'OTHER'
    api.getByWebOrderNo.mockRejectedValueOnce({ response: { status: 403 } })
    await vm.onLoad(); expect(vm.store.order.details).toEqual([])
    let resolve!: (value: unknown) => void
    api.getByWebOrderNo.mockImplementationOnce(() => new Promise(r => { resolve = r }))
    const request = vm.onLoad(); vm.onNewClick()
    resolve({ code: 0, data: document }); await request
    expect(vm.store.order.details).toEqual([])
  })
  it('keeps an unsaved draft when replacing it is cancelled', async () => {
    const vm = render(true); vm.store.loadFromDto(document); vm.store.markDirty(); vm.searchNo = 'OTHER'
    messages.confirm.mockRejectedValueOnce('cancel')
    await vm.onLoad()
    expect(vm.store.order.webOrderNo).toBe('ORDER-1'); expect(api.getByWebOrderNo).not.toHaveBeenCalled()
  })
  it('clears detail and ignores pending read after cross-tab identity change', async () => {
    const vm = render(true); let resolve!: (value: unknown) => void
    vm.store.loadFromDto(document); vm.searchNo = 'ORDER-1'
    api.getByWebOrderNo.mockImplementationOnce(() => new Promise(r => { resolve = r }))
    const request = vm.onLoad(); changeIdentity(); resolve({ code: 0, data: document }); await request
    expect(vm.store.order.details).toEqual([]); expect(vm.searchNo).toBe('')
  })
  it('treats a committed write with unavailable readback as saved and clears the old document', async () => {
    const vm = render(true); vm.store.loadFromDto(document)
    api.create.mockResolvedValueOnce({ code: 0, data: null, webOrderNo: 'SAVED', readbackUnavailable: true })
    await vm.onSave()
    expect(api.create).toHaveBeenCalledTimes(1); expect(vm.store.order.details).toEqual([])
    expect(messages.success).toHaveBeenCalled()
  })
})
