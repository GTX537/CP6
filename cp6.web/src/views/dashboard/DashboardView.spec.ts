import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { nextTick, ref } from 'vue'
import DashboardView from './DashboardView.vue'
import { usePlatformStore } from '@/stores/platform'
import { usePermissionStore } from '@/stores/permission'

const api = vi.hoisted(() => ({ summary: vi.fn(), unshipped: vi.fn(), dwell: vi.fn(), start: vi.fn(), on: vi.fn(), off: vi.fn() }))
vi.mock('@/api/sys/dashboard', () => ({ dashboardApi: { getSummary: api.summary } }))
vi.mock('@/api/erp/order', () => ({ orderApi: { searchUnshipped: api.unshipped } }))
vi.mock('@/api/wms/stockDwell', () => ({ stockDwellApi: { summary: api.dwell } }))
vi.mock('@/api/sys/rolePerm', () => ({ rolePermApi: { myActions: vi.fn() } }))
vi.mock('@/utils/signalr', () => ({ startConnection: api.start, getConnection: () => ({ on: api.on, off: api.off }) }))
vi.mock('@/utils/format', () => ({ formatQty: (n: number) => String(n) }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key, locale: ref('zh-CN') }) }))
vi.mock('vue-router', () => ({ useRouter: () => ({ resolve: () => ({ matched: [] }), push: () => Promise.resolve() }) }))
vi.mock('@/composables/useBreakpoint', () => ({ useBreakpoint: () => ({ isMobile: ref(false) }) }))

function deferred() {
  let resolve!: (value: unknown) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function summary(todayOrders = 0) {
  return { summary: { todayOrders, monthOrders: 0, activeWorkOrders: 0, monthCompleted: 0,
    pendingOutbound: 0, stockWarnings: 0, pendingApprovals: 0, totalProducts: 0 }, recentOrders: [], workOrderStatus: [] }
}
const dwell = () => ({ code: 0, data: { asOfDate: '2026-10-16', groupBy: 'product',
  totalQty: 0, totalValue: 0, over90Qty: 0, over90Value: 0, rows: [] } })
let wrapper: ReturnType<typeof mount> | undefined
function render() {
  const pinia = createPinia()
  usePermissionStore(pinia).loaded = true
  wrapper = mount(DashboardView, { global: { plugins: [pinia], stubs: {
    ElButton: { props: ['loading'], template: '<button :disabled="loading"><slot /></button>' },
    ElEmpty: { props: ['description'], template: '<div class="empty">{{ description }}</div>' },
    ElIcon: { template: '<span><slot /></span>' }, ElSwitch: true, ElTag: true,
    ElTable: true, ElTableColumn: true, ElPagination: true, ElProgress: true,
    DonutChart: { template: '<div class="donut"><slot name="empty" /></div>' },
  } } })
  return { view: wrapper, pinia }
}
function event(name: string) {
  const found = api.on.mock.calls.find(call => call[0] === name)
  expect(found).toBeDefined()
  return found![1] as (...args: unknown[]) => void
}
beforeEach(() => {
  vi.resetAllMocks()
  localStorage.clear()
  sessionStorage.clear()
  localStorage.setItem('cp6_authed', '1')
  localStorage.setItem('userName', 'one')
  api.summary.mockResolvedValue(summary())
  api.unshipped.mockResolvedValue({ code: 0, data: { items: [], total: 0 } })
  api.dwell.mockResolvedValue(dwell())
  api.start.mockResolvedValue(undefined)
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined })

describe('Dashboard visible states and lifecycle', () => {
  it('shows pending placeholders, then true zero and empty results only after success', async () => {
    const pending = deferred()
    api.summary.mockReturnValue(pending.promise)
    api.unshipped.mockReturnValue(pending.promise)
    api.dwell.mockReturnValue(pending.promise)
    const { view } = render()
    expect(view.find('.kpis').text()).toContain('—')
    expect(view.find('.kpis').text()).not.toContain('0')
    expect(view.text()).not.toContain('dashboard.noData')
    expect(view.findAll('[role="status"]').every(p => p.text() === '加载…')).toBe(true)
    // Let each API return its own valid empty contract on independent refresh.
    api.summary.mockResolvedValue(summary())
    api.unshipped.mockResolvedValue({ code: 0, data: { rows: [], total: 0 } })
    api.dwell.mockResolvedValue(dwell())
    pending.reject(new Error('offline'))
    await flushPromises()
    const buttons = view.findAll('button[aria-label="刷新"]')
    for (const button of buttons) await button.trigger('click')
    await flushPromises()
    expect(view.find('.kpis').text()).toContain('0')
    expect(view.find('.kpis').text()).not.toContain('—')
    expect(view.findAll('.empty').filter(x => x.text() === 'dashboard.noData')).toHaveLength(3)
  })

  it('summary failure does not block widgets or SignalR; refreshes remain grouped', async () => {
    api.summary.mockRejectedValue({ response: { status: 500 } })
    const { view } = render()
    await flushPromises()
    expect(view.find('.kpis').text()).toContain('请求失败')
    expect(api.unshipped).toHaveBeenCalledTimes(1)
    expect(api.dwell).toHaveBeenCalledTimes(1)
    expect(api.start).toHaveBeenCalledTimes(1)
    event('NewOperLog')()
    event('BusinessNotification')({ title: 'new notice' })
    await flushPromises()
    expect(api.summary).toHaveBeenCalledTimes(3)
    expect(api.unshipped).toHaveBeenCalledTimes(1)
    expect(api.dwell).toHaveBeenCalledTimes(1)
    await view.findAll('button[aria-label="刷新"]')[1]!.trigger('click')
    await flushPromises()
    expect(api.unshipped).toHaveBeenCalledTimes(2)
    expect(api.summary).toHaveBeenCalledTimes(3)
    expect(api.dwell).toHaveBeenCalledTimes(1)
  })

  it('clears old summary on denied refresh; late success cannot hide denial, and late failure cannot hide newer data', async () => {
    const { view } = render()
    await flushPromises()
    const old = deferred(), newest = deferred()
    api.summary.mockReturnValueOnce(old.promise).mockReturnValueOnce(newest.promise)
    event('NewOperLog')(); event('NewOperLog')()
    newest.reject({ response: { status: 403 } })
    await flushPromises()
    old.resolve(summary(99))
    await flushPromises()
    expect(view.find('.kpis').text()).toContain('403')
    expect(view.find('.kpis').text()).not.toContain('99')
    expect(view.find('.ship').exists()).toBe(false)
    const lateFailure = deferred()
    api.summary.mockReturnValueOnce(lateFailure.promise).mockResolvedValueOnce(summary(7))
    event('NewOperLog')(); event('NewOperLog')()
    await flushPromises()
    lateFailure.reject(new Error('offline'))
    await flushPromises()
    expect(view.find('.kpis').text()).toContain('7')
    expect(view.find('.kpis').text()).not.toContain('请求失败')
  })

  it('shows failures per widget and never presents incomplete payloads as no data', async () => {
    api.unshipped.mockRejectedValue({ response: { status: 403 } })
    api.dwell.mockResolvedValue({ code: 0, data: { rows: [] } })
    const { view } = render()
    await flushPromises()
    const statuses = view.findAll('[role="status"]').map(p => p.text())
    expect(statuses).toEqual(['403 · sec.event.8', '请求失败'])
    expect(view.findAll('.empty').filter(x => x.text() === 'dashboard.noData')).toHaveLength(1)
  })

  it('clears displayed data and rejects pending results on observed session and impersonation changes', async () => {
    api.summary.mockResolvedValue(summary(42))
    const { view, pinia } = render()
    await flushPromises()
    event('BusinessNotification')({ title: 'old session notice' })
    await flushPromises()
    const old = deferred()
    api.summary.mockReturnValueOnce(old.promise)
    event('NewOperLog')()
    localStorage.setItem('userName', 'two')
    window.dispatchEvent(new StorageEvent('storage', { key: 'userName' }))
    await nextTick()
    old.resolve(summary(99))
    await flushPromises()
    expect(view.find('.kpis').text()).not.toContain('99')
    expect(view.text()).not.toContain('old session notice')
    expect(view.findAll('[role="status"]').every(p => p.text() === '请求失败')).toBe(true)
    await view.findAll('button[aria-label="刷新"]')[0]!.trigger('click')
    await flushPromises()
    expect(view.find('.kpis').text()).toContain('42')
    usePlatformStore(pinia).setImpersonation({ tenantName: 'other', userName: 'three', expiresAt: 100 })
    await nextTick()
    expect(view.find('.kpis').text()).not.toContain('42')
  })

  it('does not attach handlers if unmounted while connection starts; detaches only its own handlers', async () => {
    const start = deferred(), pending = deferred()
    api.start.mockReturnValueOnce(start.promise)
    api.summary.mockReturnValueOnce(pending.promise)
    render().view.unmount(); wrapper = undefined
    start.resolve(undefined); pending.reject(new Error('offline'))
    await flushPromises()
    expect(api.on).not.toHaveBeenCalled()
    const { view } = render()
    await flushPromises()
    const handler = event('NewOperLog')
    view.unmount(); wrapper = undefined
    expect(api.off).toHaveBeenCalledWith('NewOperLog', handler)
    const calls = api.summary.mock.calls.length
    handler()
    expect(api.summary).toHaveBeenCalledTimes(calls)
  })
})
