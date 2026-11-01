import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
const mock = vi.hoisted(() => ({ context: vi.fn(), board: vi.fn(), detail: vi.fn() }))
vi.mock('@/api/mes/schedulingBoard', () => ({ schedulingBoardApi: mock }))
vi.mock('@/auth/browserAuthentication', () => ({ authenticationCanRead: () => true }))
vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ has: () => true }) }))
import View from './SchedulingBoardPanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const reference = (id: string) => ({ id, owner: 'owner', version: '1', digest: id })
const interval = { start: '2026-11-01T01:30:00-04:00', end: '2026-11-01T01:30:00-05:00' }
const site = { siteRef: reference('site'), zoneId: 'America/New_York', defaultWindow: { start: '2026-11-01T00:00:00-04:00', end: '2026-11-15T00:00:00-05:00' }, processes: [reference('process')], workgroups: [], resources: [] }
const bar = { operationId: 'A', woId: 'wo', workOrderNo: 'SIM-WO', productLabel: '模拟产品', processLabel: '同一道工序', routeSortOrder: 1, occurrenceRef: reference('occ-a'), snapshotRef: reference('snapshot'), woExpected: { revision: '10' }, scheduleRevision: '5', plannedInterval: interval, actualInterval: null, actualCoverage: 'UNKNOWN', deliveryDate: null, state: 'NOT_STARTED', workOrderState: 'RELEASED', priority: 'NORMAL', coverage: 'COMPLETE', mode: 'SCHEDULING_V1', delayed: false, conflicts: [] }
const kpi = Object.fromEntries(['totalOrders', 'inProgressOrders', 'completedOrders', 'delayedOrders', 'completionRate', 'avgDelayDays', 'denominator', 'cancelledCount', 'unknownCount'].map(k => [k, { value: k === 'totalOrders' ? '3' : '0', unit: 'orders', coverage: 'COMPLETE' }]))
const page = { captureId: 'cut', queryScopeHash: 'hash', asOf: '2026-11-01T12:00:00Z', expiresAt: '2026-11-01T12:30:00Z', zoneId: site.zoneId, bars: [bar], nextCursor: 20, kpi: { ...kpi, formulaVersion: 'BOARD_FILTERED_OPS_V1' }, coverage: 'COMPLETE', warnings: [], dataKind: 'SIMULATED_UPSTREAM' }
let view: VueWrapper | undefined
function pending() { let resolve!: (value: any) => void; const promise = new Promise(r => { resolve = r }); return { promise, resolve } }
async function render() { view = mount(View); await flushPromises() }
async function load() { await view!.get('form').trigger('submit'); await flushPromises() }
async function choose() { await view!.get('[aria-label="看板工厂"]').setValue('site') }
async function click(text: string) { await view!.findAll('button').find(b => b.text() === text)!.trigger('click'); await flushPromises() }
beforeEach(() => { vi.resetAllMocks(); localStorage.clear(); localStorage.setItem(AUTH_EPOCH, 'first'); mock.context.mockResolvedValue({ tenantId: 'tenant', actorId: 'actor', environmentId: 'env', sites: [site] }); mock.board.mockResolvedValue(structuredClone(page)) })
afterEach(() => { view?.unmount(); view = undefined })
it('requires an explicit authorized site on first visit, then uses its factory business-day window', async () => {
  await render(); expect(mock.board).not.toHaveBeenCalled(); expect(view!.text()).toContain('未知'); await choose(); await load()
  expect(mock.board.mock.calls[0]![0]).toMatchObject({ siteId: 'site', from: site.defaultWindow.start, to: site.defaultWindow.end, zoneId: site.zoneId, statuses: 'ANY' })
  expect(view!.get('[data-metric="totalOrders"]').text()).toBe('3'); expect(view!.text()).toContain('模拟数据')
})
it('ignores a slow older filter response after a newer query succeeds', async () => {
  await render(); await choose(); const old = pending(); mock.board.mockReturnValueOnce(old.promise); await load()
  await view!.get('[aria-label="看板状态"]').setValue('COMPLETED'); mock.board.mockResolvedValueOnce({ ...page, captureId: 'new', bars: [{ ...bar, workOrderNo: 'NEW-WO' }] }); await load()
  old.resolve({ ...page, bars: [{ ...bar, workOrderNo: 'OLD-WO' }] }); await flushPromises(); expect(view!.text()).toContain('NEW-WO'); expect(view!.text()).not.toContain('OLD-WO')
})
it('pages the frozen capture and retains it as stale on expiry without showing zero KPIs', async () => {
  await render(); await choose(); await load(); mock.board.mockRejectedValueOnce({ response: { status: 409, data: { message: 'BOARD_CAPTURE_EXPIRED' } } }); await click('下一页')
  expect(mock.board.mock.calls[1]!.slice(1)).toEqual(['cut', 20]); expect(view!.text()).toContain('STALE'); expect(view!.text()).toContain('SIM-WO'); expect(view!.get('[data-metric="totalOrders"]').text()).toBe('3')
})
it('distinguishes first-load failure, complete empty results, unplanned data and partial statistics', async () => {
  await render(); await choose(); mock.board.mockRejectedValueOnce(new Error('offline')); await load(); expect(view!.text()).toContain('尚无成功数据：未知'); expect(view!.find('[data-metric="totalOrders"]').exists()).toBe(false)
  mock.board.mockResolvedValueOnce({ ...page, bars: [], kpi: { ...page.kpi, totalOrders: { value: '0', unit: 'orders', coverage: 'COMPLETE' } } }); await load(); expect(view!.text()).toContain('没有匹配操作'); expect(view!.get('[data-metric="totalOrders"]').text()).toBe('0')
  mock.board.mockResolvedValueOnce({ ...page, coverage: 'PARTIAL', bars: [{ ...bar, plannedInterval: null }], kpi: Object.fromEntries(Object.keys(kpi).map(k => [k, { value: null, unit: 'orders', coverage: 'PARTIAL' }])) }); await load()
  expect(view!.get('[data-metric="totalOrders"]').text()).toBe('权限不完整'); expect(view!.get('[aria-label="未排操作"]').text()).toContain('SIM-WO'); expect(view!.find('.planned').exists()).toBe(false)
})
it('day/week display preserves UTC instants across DST and rejects offset-free inputs', async () => {
  await render(); await choose(); await load(); const title = view!.get('.planned').attributes('title'); expect(title).toContain('GMT-4'); expect(title).toContain('GMT-5')
  expect(view!.get('.axis').text()).not.toContain('23:00'); expect(view!.get('.axis').text()).toContain('00:00')
  await view!.get('[aria-label="时间轴尺度"]').setValue('week'); expect(view!.get('.planned').attributes('title')).toBe(title); expect(mock.board).toHaveBeenCalledTimes(1)
  await view!.get('[aria-label="看板开始"]').setValue('2026-11-01T01:30:00'); await load(); expect(mock.board).toHaveBeenCalledTimes(1); expect(view!.text()).toContain('带时区')
})
it('opens exact operation details and preserves legacy inspected status without inventing root authority', async () => {
  await render(); await choose(); await load(); mock.detail.mockResolvedValue({ operation: { ...bar, occurrenceRef: null, snapshotRef: null, workOrderState: 'LEGACY_INSPECTED', mode: 'LEGACY_READ_ONLY' }, completeForRoot: false, rootScheduleContext: null, zoneId: site.zoneId })
  await view!.get('.row-label').trigger('click'); await flushPromises(); expect(mock.detail).toHaveBeenCalledWith('A'); expect(view!.text()).toContain('历史已检验（非 CLOSED）'); expect(view!.text()).toContain('历史参考资料，只读')
})
it('clears private data on identity change and rejects late board and detail responses', async () => {
  await render(); await choose(); await load(); const old = pending(); mock.board.mockReturnValueOnce(old.promise); await load()
  localStorage.setItem(AUTH_EPOCH, 'second'); window.dispatchEvent(new Event(AUTH_EVENT)); old.resolve(page); await flushPromises(); expect(view!.text()).not.toContain('SIM-WO'); expect(view!.find('[data-metric="totalOrders"]').exists()).toBe(false)
})
it('a detail authorization failure also invalidates an in-flight board refresh', async () => {
  await render(); await choose(); await load(); const old = pending(); mock.board.mockReturnValueOnce(old.promise); await load(); mock.detail.mockRejectedValueOnce({ response: { status: 403 } })
  await view!.get('.row-label').trigger('click'); await flushPromises(); old.resolve(page); await flushPromises(); expect(view!.text()).not.toContain('SIM-WO')
})
