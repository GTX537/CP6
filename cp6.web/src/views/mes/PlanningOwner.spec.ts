import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
const board = vi.hoisted(() => ({ capture: vi.fn(), getBars: vi.fn(), getKpi: vi.fn(), reschedule: vi.fn(), autoArrange: vi.fn() }))
const report = vi.hoisted(() => ({ summary: vi.fn(), exportCsv: vi.fn() }))
const ui = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), confirm: vi.fn() }))
const permission = vi.hoisted(() => ({ loaded: true, has: vi.fn(() => true) }))
vi.mock('@/api/mes/mes', () => ({ planningBoardApi: board }))
vi.mock('@/api/mes/planAchievement', () => ({ planAchievementApi: report }))
vi.mock('element-plus', () => ({ ElMessage: ui, ElMessageBox: ui }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => permission }))
vi.mock('vue-i18n', async original => ({ ...await original<typeof import('vue-i18n')>(), useI18n: () => ({ t: (key: string) => key }) }))
import Board from './PlanningBoardView.vue'
import Report from './PlanAchievementView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
let wrapper: ReturnType<typeof shallowMount> | undefined
function render(component: any): any { wrapper = shallowMount(component, { global: { directives: { permission: {} }, config: { warnHandler: () => {} }, stubs: { ElTable: true, ElTableColumn: true, ElDialog: true } } }); return wrapper.vm }
function pending() { let resolve!: (value: any) => void; const promise = new Promise<any>(r => { resolve = r }); return { promise, resolve } }
function identity() { localStorage.setItem(AUTH_EPOCH, crypto.randomUUID()); window.dispatchEvent(new Event(AUTH_EVENT)) }
const bar = { id: 'one', workOrderNo: 'private order', processCd: 'P', taskCd: 'T', processStatus: 0, planStartTime: '2026-10-22T08:00:00', planEndTime: '2026-10-22T10:00:00', goodQty: 5 }
const kpi = { totalOrders: 1, inProgressOrders: 0, completedOrders: 0, delayedOrders: 0, completionRate: 0, avgDelayDays: 0 }
const summary = { totalWorkOrders: 1, totalPlannedQty: 10, totalGoodQty: 5, totalDefectQty: 0, achievementRate: .5, defectRate: 0, onTargetCount: 0, rows: [{ groupKey: 'private', groupLabel: 'private product', plannedQty: 10, goodQty: 5, achievementRate: .5 }] }
beforeEach(() => {
  vi.useFakeTimers(); localStorage.clear(); vi.clearAllMocks(); permission.loaded = true; permission.has.mockReturnValue(true)
  for (const mock of [...Object.values(board), ...Object.values(report)]) mock.mockReset()
  board.getBars.mockResolvedValue({ data: [bar] }); board.getKpi.mockResolvedValue({ data: kpi })
  board.capture.mockResolvedValue({ data: { bars: [bar], kpi, mode: 'LEGACY_READ_ONLY', canApplySchedule: false } })
  board.reschedule.mockResolvedValue({ data: {} }); board.autoArrange.mockResolvedValue({ data: { changed: 1 } }); ui.confirm.mockResolvedValue('confirm')
  report.summary.mockResolvedValue({ data: summary }); report.exportCsv.mockResolvedValue(new Blob(['private csv']))
  vi.stubGlobal('URL', class extends URL { static createObjectURL = vi.fn(() => 'blob:test'); static revokeObjectURL = vi.fn() })
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers() })

describe('planning current identity and read boundaries', () => {
  it('clears board and its open detail on identity change, ignoring a delayed result', async () => {
    const vm = render(Board); await flushPromises(); vm.onBarClick(bar)
    const response = pending(); board.capture.mockReturnValueOnce(response.promise); board.getBars.mockReturnValueOnce(response.promise.then(value => ({ data: value.data.bars })))
    const loading = vm.reload(); identity(); response.resolve({ data: { bars: [bar], kpi } }); await loading
    expect(vm.bars).toEqual([]); expect(vm.kpi.totalOrders).toBe(0); expect(vm.dialogVisible).toBe(false); expect(vm.editing).toBeNull()
  })
  it('clears forbidden board data and stops polling', async () => {
    const vm = render(Board); await flushPromises(); const denied = { response: { status: 403 } }
    board.capture.mockRejectedValueOnce(denied); board.getBars.mockRejectedValueOnce(denied)
    await vm.reload().catch(() => {}); expect(vm.bars).toEqual([]); expect(vm.kpi.totalOrders).toBe(0)
    const calls = board.capture.mock.calls.length + board.getBars.mock.calls.length
    await vi.advanceTimersByTimeAsync(61000); expect(board.capture.mock.calls.length + board.getBars.mock.calls.length).toBe(calls)
  })
  it('stops the board refresh timer on unmount', async () => {
    render(Board); await flushPromises(); wrapper!.unmount(); wrapper = undefined
    const calls = board.capture.mock.calls.length + board.getBars.mock.calls.length
    await vi.advanceTimersByTimeAsync(61000); expect(board.capture.mock.calls.length + board.getBars.mock.calls.length).toBe(calls)
  })
  it('does not turn a legacy read-only row or old action into schedule authority', async () => {
    const vm = render(Board); await flushPromises(); vm.onBarClick(bar); await vm.onReschedule(); await vm.onAutoArrange()
    expect(board.reschedule).not.toHaveBeenCalled(); expect(board.autoArrange).not.toHaveBeenCalled()
  })
  it('rejects a stale report filter result after the newer selection loaded', async () => {
    const vm = render(Report); await flushPromises(); const response = pending(); report.summary.mockReturnValueOnce(response.promise)
    const old = vm.loadSummary(); vm.query.productCd = 'NEW'; report.summary.mockResolvedValueOnce({ data: { ...summary, totalGoodQty: 77 } }); await vm.loadSummary()
    response.resolve({ data: summary }); await old; expect(vm.summary.totalGoodQty).toBe(77)
  })
  it('clears report totals and details on source authority loss', async () => {
    const vm = render(Report); await flushPromises(); report.summary.mockRejectedValueOnce({ response: { status: 403 } })
    await vm.loadSummary().catch(() => {}); expect(vm.summary.totalWorkOrders).toBe(0); expect(vm.rows).toEqual([])
  })
  it('never downloads a response for the prior identity', async () => {
    const vm = render(Report); await flushPromises(); const response = pending(); report.exportCsv.mockReturnValueOnce(response.promise)
    const exporting = vm.exportCsv(); identity(); response.resolve(new Blob(['private csv'])); await exporting
    expect(URL.createObjectURL).not.toHaveBeenCalled(); expect(ui.success).not.toHaveBeenCalled(); expect(vm.rows).toEqual([])
  })
  it('requires its own export action and deduplicates a frozen export request', async () => {
    const vm = render(Report); await flushPromises(); permission.has.mockReturnValue(false); await vm.exportCsv(); expect(report.exportCsv).not.toHaveBeenCalled()
    permission.has.mockReturnValue(true); const response = pending(); report.exportCsv.mockReturnValueOnce(response.promise)
    vm.query.productCd = 'P1'; const exporting = vm.exportCsv(); await vm.exportCsv(); vm.query.productCd = 'P2'
    expect(report.exportCsv).toHaveBeenCalledTimes(1); expect(report.exportCsv.mock.calls[0]![0].productCd).toBe('P1')
    response.resolve(new Blob(['old filter'])); await exporting; expect(URL.createObjectURL).not.toHaveBeenCalled()
  })
})
