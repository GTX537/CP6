import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
const take = vi.hoisted(() => ({ search: vi.fn(), get: vi.fn(), createPlan: vi.fn(), creationContext: vi.fn(), creationScope: vi.fn(), createOperation: vi.fn(), readCreation: vi.fn(), closeCreation: vi.fn(), startCount: vi.fn(), updateCounts: vi.fn(), submit: vi.fn(), approve: vi.fn(), cancel: vi.fn() }))
const stock = vi.hoisted(() => ({ search: vi.fn(), history: vi.fn(), transactions: vi.fn(), setQcStatus: vi.fn() }))
const wms = vi.hoisted(() => ({ kpi: vi.fn(), trend: vi.fn(), warehouseValue: vi.fn(), alerts: vi.fn() }))
const mes = vi.hoisted(() => ({ summary: vi.fn(), processProgress: vi.fn(), delayAlerts: vi.fn(), defectTop5: vi.fn(), recentCompleted: vi.fn(), machineHeatmap: vi.fn(), dailyTrend: vi.fn() }))
const messages = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), confirm: vi.fn(), push: vi.fn() }))
const hub = vi.hoisted(() => ({ on: vi.fn(), off: vi.fn(), onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(), state: 'Disconnected' }))
vi.mock('@/api/wms/stockTake', () => ({ stockTakeApi: take }))
vi.mock('@/api/wms/stock', () => ({ stockApi: stock }))
vi.mock('@/api/wms/wmsDashboard', () => ({ wmsDashboardApi: wms }))
vi.mock('@/api/mes/mes', () => ({ mesDashboardApi: mes }))
vi.mock('@/utils/wmsHub', () => ({ getWmsConnection: () => hub, startWmsConnection: vi.fn().mockResolvedValue(undefined) }))
vi.mock('element-plus', () => ({ ElMessage: messages, ElMessageBox: { confirm: messages.confirm } }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('vue-router', () => ({ useRoute: () => route, useRouter: () => ({ push: messages.push }) }))
import Take from './StockTakeView.vue'
import TakeList from './StockTakeListView.vue'
import Stock from './StockQueryView.vue'
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, actionKeys: new Set(), has: (key: string) => key.startsWith('wms-stocktake:') }) }))
import Wms from './WmsDashboardView.vue'
import Mes from '../mes/MesDashboardView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const route = reactive({ query: {} as Record<string, string> })
let view: ReturnType<typeof shallowMount> | undefined
function render(component: any): any {
  view = shallowMount(component, { global: { directives: { permission: {}, loading: {} }, config: { warnHandler: () => {} }, stubs: {
    ElCard: { template: '<div><slot/><slot name="header"/></div>' }, ElAlert: true, ElTable: true, ElTableColumn: true,
    CpPageShell: { template: '<div><slot/><slot name="actions"/></div>' }, CpListPage: true, CpFormDialog: true,
  } } }); return view.vm
}
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
const scope = { tenantId: '10000000-0000-0000-0000-000000000001', actorId: '20000000-0000-0000-0000-000000000002' }
const stockTakeId = '40000000-0000-0000-0000-000000000004'
const receipt = (operationId: string, stockTakeNo: string) => ({ code: 0, data: { operationId, stockTakeId, stockTakeNo, departmentId: null } })
const document = { stockTakeNo: 'ST1', stockTakeType: 1, plannedDate: '2026-10-21', status: 1, targetWarehouseCd: 'W1', details: [{ lineNo: 1, productCd: 'P', lotNo: '', locationCd: 'L', bookQty: 2, countedQty: 1 }] }
beforeEach(() => {
  localStorage.clear(); route.query = {}; vi.clearAllMocks(); vi.useFakeTimers()
  for (const api of [take, stock, wms, mes]) Object.values(api).forEach(fn => fn.mockReset().mockResolvedValue({ data: [] }))
  take.creationScope.mockResolvedValue({ code: 0, data: scope })
  take.creationContext.mockResolvedValue({ code: 0, data: { ...scope, authorityVersion: 'current', allowsUnbound: true, departments: [] } })
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: async (_key: string, _options: any, run: (lock: any) => Promise<any>) => run({}) } })
  messages.confirm.mockReset().mockResolvedValue('confirm'); messages.push.mockReset().mockResolvedValue(undefined)
  wms.kpi.mockResolvedValue({ data: { totalStockValue: 5 } }); wms.alerts.mockResolvedValue({ data: { expiryAlerts: [], delayedInbounds: [] } })
  stock.transactions.mockResolvedValue({ data: { items: [] } }); mes.summary.mockResolvedValue({ data: { totalGoodQty: 5 } })
})
afterEach(() => { view?.unmount(); view = undefined; vi.useRealTimers() })
describe('stocktake, inventory and dashboards Owner UI', () => {
  it('freezes counts before awaiting and preserves edits made during save and read denial', async () => {
    const vm = render(Take); Object.assign(vm.form, structuredClone(document))
    let done!: (x: unknown) => void; take.updateCounts.mockImplementationOnce(() => new Promise(r => { done = r }))
    const save = vm.onSaveCounts(); await vm.onSaveCounts(); expect(take.updateCounts).toHaveBeenCalledTimes(1)
    vm.form.details[0].countedQty = 7; expect(take.updateCounts.mock.calls[0]![1][0].countedQty).toBe(1)
    done({}); await save; expect(vm.form.details[0].countedQty).toBe(7); expect(take.get).not.toHaveBeenCalled()
    take.updateCounts.mockResolvedValue({}); take.get.mockRejectedValue({ response: { status: 403 } }); await vm.onSaveCounts()
    expect(vm.form.stockTakeNo).toBe('ST1'); expect(messages.success).toHaveBeenCalledTimes(2)
  })
  it('does not approve an obsolete identity or new route after confirmation, and prevents duplicate confirmation', async () => {
    const vm = render(Take); Object.assign(vm.form, structuredClone(document)); let done!: (x: unknown) => void
    messages.confirm.mockImplementationOnce(() => new Promise(r => { done = r })); const pending = vm.onApprove(); await vm.onApprove()
    expect(messages.confirm).toHaveBeenCalledTimes(1); identity(); done('ok'); await pending; expect(take.approve).not.toHaveBeenCalled()
  })
  it('blocks unknown count retries and discards old detail reads without replacing a new draft', async () => {
    const vm = render(Take); Object.assign(vm.form, structuredClone(document))
    take.updateCounts.mockRejectedValue({ response: { status: 503, data: { message: 'OPERATIONS_OUTCOME_UNKNOWN' } } })
    await vm.onSaveCounts(); await vm.onSaveCounts(); expect(take.updateCounts).toHaveBeenCalledTimes(1)
    identity(); let done!: (x: unknown) => void; take.get.mockImplementationOnce(() => new Promise(r => { done = r }))
    const read = vm.load('OLD'); identity(); done({ data: document }); await read; expect(vm.form.details).toEqual([])
  })
  it('freezes plan creation, retains its committed number and never navigates after identity change', async () => {
    const vm = render(TakeList); await flushPromises(); await vm.openPlan(); vm.planForm.targetWarehouseCd = 'W1'; let done!: (x: unknown) => void
    take.createOperation.mockImplementationOnce(() => new Promise(r => { done = r })); const save = vm.onCreatePlan(); await flushPromises()
    await expect(vm.onCreatePlan()).rejects.toThrow(); expect(take.createOperation).toHaveBeenCalledTimes(1)
    vm.planForm.targetWarehouseCd = 'W2'; expect(take.createOperation.mock.calls[0]![0].plan.targetWarehouseCd).toBe('W1')
    done(receipt(take.createOperation.mock.calls[0]![0].operationId, 'NEW')); await save; expect(vm.lastSavedNo).toBe('NEW')
    await vm.openPlan(); take.createOperation.mockImplementationOnce(() => new Promise(r => { done = r }))
    const stale = vm.onCreatePlan(); await flushPromises(); const operation = take.createOperation.mock.calls[1]![0].operationId
    identity(); done(receipt(operation, 'OLD'))
    await expect(stale).rejects.toThrow(); expect(messages.push).toHaveBeenCalledTimes(1)
  })
  it('retains unknown plan references and rejects another submission', async () => {
    const vm = render(TakeList); await flushPromises(); await vm.openPlan()
    take.createOperation.mockImplementationOnce(async c => receipt(c.operationId, 'CONFIRMED')); await vm.onCreatePlan()
    await vm.openPlan(); take.createOperation.mockRejectedValue({ response: { status: 503 } })
    await expect(vm.onCreatePlan()).rejects.toBeDefined(); await expect(vm.onCreatePlan()).rejects.toThrow(); expect(take.createOperation).toHaveBeenCalledTimes(2)
    expect(vm.lastSavedNo).toBe('CONFIRMED'); expect(vm.creationPending).not.toBeNull()
  })
  it('retains confirmed approval state when follow-up reading is forbidden', async () => {
    const vm = render(Take); Object.assign(vm.form, structuredClone(document)); vm.form.status = 3
    take.get.mockRejectedValue({ response: { status: 403 } }); await vm.onApprove()
    expect(take.approve).toHaveBeenCalledTimes(1); expect(vm.form.status).toBe(4); expect(vm.form.stockTakeNo).toBe('ST1')
  })
  it('isolates inventory history across target and identity changes and clears rejected history', async () => {
    const vm = render(Stock); let done!: (x: unknown) => void; stock.history.mockImplementationOnce(() => new Promise(r => { done = r }))
    const old = vm.openHistory({ id: 'ONE' }); stock.history.mockResolvedValueOnce({ data: { stock: { id: 'TWO' }, transactions: [] } }); await vm.openHistory({ id: 'TWO' })
    done({ data: { stock: { id: 'ONE' }, transactions: [{}] } }); await old; expect(vm.historyStock.id).toBe('TWO')
    identity(); expect(vm.historyStock).toBeNull(); stock.history.mockRejectedValueOnce({ response: { status: 403 } }); await vm.openHistory({ id: 'TWO' }); expect(vm.historyTxns).toEqual([])
  })
  it.each([Wms, Mes])('discards old identity dashboard results and clears forbidden refreshes', async component => {
    const vm = render(component); await flushPromises(); const api = component === Wms ? wms.kpi : mes.summary
    let done!: (x: unknown) => void; api.mockImplementationOnce(() => new Promise(r => { done = r }))
    const pending = component === Wms ? vm.loadKpi() : vm.loadAll(); identity(); done({ data: { totalStockValue: 999, totalGoodQty: 999 } }); await pending
    expect(component === Wms ? vm.kpi.totalStockValue : vm.summary.totalGoodQty).toBe(0)
    api.mockRejectedValueOnce({ response: { status: 403 } }); await (component === Wms ? vm.loadKpi() : vm.loadAll())
    expect(Object.keys(vm.readErrors).length).toBeGreaterThan(0); expect(component === Wms ? vm.kpi.totalStockValue : vm.summary.totalGoodQty).toBe(0)
  })
  it('never renders raw realtime payloads; only Owner query results fill the event list', async () => {
    const vm = render(Wms); await flushPromises(); const handler = hub.on.mock.calls.find(call => call[0] === 'StockChanged')![1]
    handler({ txnNo: 'PRIVATE', qty: 999 }); expect(vm.events).toEqual([]); expect(messages.success).not.toHaveBeenCalled()
    stock.transactions.mockResolvedValueOnce({ data: { items: [{ txnNo: 'ALLOWED', txnDateTime: '2026-10-22T00:00:00', qty: 1 }] } }); await vm.loadEvents(); expect(vm.events[0].txnNo).toBe('ALLOWED')
  })
})
