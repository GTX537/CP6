import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
const api = vi.hoisted(() => ({ batchUpdatePrice: vi.fn(), searchPriceCorrection: vi.fn() }))
const messages = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), info: vi.fn(), error: vi.fn(), confirm: vi.fn() }))
vi.mock('@/api/erp/order', () => ({ orderApi: api }))
vi.mock('element-plus', () => ({ ElMessage: messages, ElMessageBox: { confirm: messages.confirm } }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string, values?: Record<string, unknown>) =>
  key.replace(/\{(\w+)\}/g, (_, name: string) => String(values?.[name] ?? name)) }) }))
import OrderPriceCorrectionView from './OrderPriceCorrectionView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'

let view: ReturnType<typeof mount> | undefined
async function render() {
  view = mount(OrderPriceCorrectionView, { global: { directives: { loading: {}, permission: {} }, stubs: {
    ElCard: { template: '<div><slot /></div>' }, ElForm: { template: '<div><slot /></div>' },
    ElFormItem: { template: '<div><slot /></div>' }, ElInput: true, ElInputNumber: true, ElDatePicker: true,
    ElCheckbox: true, ElTable: true, ElTableColumn: true, ElPagination: true, ElIcon: true, CpTag: true,
    ElButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
    ElAlert: { props: ['title'], template: '<p role="status">{{title}}</p>' },
  } } })
  await flushPromises()
  const vm = view.vm as any
  vm.query.baseCd = 'B'
  vm.onSelectionChange([{ webOrderNo: 'ORDER-1', webOrderDetailNo: 1, individualUnitPriceAfter: 4,
    priceChangeReason: 'draft', provisionalPriceFlg: false, rowVersion: 'original-version' }])
  return vm
}
beforeEach(() => {
  localStorage.clear()
  vi.clearAllMocks(); api.batchUpdatePrice.mockReset(); messages.confirm.mockReset().mockResolvedValue('confirm')
  api.searchPriceCorrection.mockResolvedValue({ code: 0, data: { rows: [], total: 0 } })
})
afterEach(() => { view?.unmount(); view = undefined })
describe('price correction write outcome', () => {
  it.each([
    { response: { status: 503, data: { message: 'PRICE_CORRECTION_OUTCOME_UNKNOWN' } } },
    new Error('network response lost'),
  ])('preserves draft and refuses resubmission of an unknown outcome', async failure => {
    const vm = await render(); api.batchUpdatePrice.mockRejectedValue(failure)
    await vm.onSubmit()
    expect(vm.outcomeUnknown).toBe(true); expect(vm.selectedRows[0].priceChangeReason).toBe('draft')
    expect(view!.find('[role=status]').exists()).toBe(true)
    await vm.onSubmit(); expect(api.batchUpdatePrice).toHaveBeenCalledTimes(1)
    expect(api.searchPriceCorrection).not.toHaveBeenCalled()
    vm.resetQuery(); await vm.onSubmit(); expect(api.batchUpdatePrice).toHaveBeenCalledTimes(1)
    expect(vm.outcomeUnknown).toBe(true)
  })
  it.each([401, 409])('shows a known %s refusal and keeps input without replay', async status => {
    const vm = await render(); api.batchUpdatePrice.mockRejectedValue({ response: { status } })
    await vm.onSubmit()
    expect(vm.submitNotice).toBeTruthy(); expect(vm.outcomeUnknown).toBe(false)
    expect(vm.selectedRows[0].priceChangeReason).toBe('draft')
    expect(api.batchUpdatePrice).toHaveBeenCalledTimes(1); expect(api.searchPriceCorrection).not.toHaveBeenCalled()
  })
  it('sends one frozen intent while confirmation is pending and distinguishes durable intent from delivery', async () => {
    const vm = await render()
    let confirm!: () => void
    messages.confirm.mockImplementation(() => new Promise<void>(resolve => { confirm = resolve }))
    api.batchUpdatePrice.mockResolvedValue({ code: 0, data: { updatedCount: 1, wfRequestedCount: 2,
      conflictedKeys: [], workflowDeliveryState: 'AwaitingAdapter' } })
    const first = vm.onSubmit(); await vm.onSubmit(); expect(messages.confirm).toHaveBeenCalledTimes(1)
    vm.selectedRows[0].individualUnitPriceAfter = 99
    confirm(); await first
    expect(api.batchUpdatePrice).toHaveBeenCalledTimes(1)
    expect(api.batchUpdatePrice.mock.calls[0]![0].items[0].individualUnitPriceAfter).toBe(4)
    expect(messages.success.mock.calls[0]![0]).toContain('外部送信待ち')
    expect(api.searchPriceCorrection).toHaveBeenCalledTimes(1)
  })
  it('never submits the prior identity draft after confirmation spans an account change', async () => {
    const vm = await render()
    let confirm!: () => void
    messages.confirm.mockImplementation(() => new Promise<void>(resolve => { confirm = resolve }))
    const pending = vm.onSubmit()
    localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT))
    expect(vm.selectedRows).toHaveLength(0)
    confirm(); await pending
    expect(api.batchUpdatePrice).not.toHaveBeenCalled()
    expect(vm.outcomeUnknown).toBe(false); expect(vm.submitNotice).toBeTruthy()
  })
})
