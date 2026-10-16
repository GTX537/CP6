import { describe, expect, it } from 'vitest'
import { createDashboardLoader, parseDashboard, parseStockDwell, parseUnshipped } from './dashboardLoadState'

function deferred() {
  let resolve!: (value: unknown) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const summary = {
  todayOrders: 0, monthOrders: 0, activeWorkOrders: 0, monthCompleted: 0,
  pendingOutbound: 0, stockWarnings: 0, pendingApprovals: 0, totalProducts: 0,
}
const zeroDashboard = () => ({ summary: { ...summary }, recentOrders: [], workOrderStatus: [] })
const emptyDwell = () => ({ code: 0, data: {
  asOfDate: '2026-10-16', groupBy: 'product', totalQty: 0, totalValue: 0,
  over90Qty: 0, over90Value: 0, rows: [],
} })

describe('dashboard group state', () => {
  it('starts without business data; accepts real zero, then clears old data on HTTP 403', async () => {
    const group = createDashboardLoader(parseDashboard)
    expect(group.state.value).toEqual({ status: 'loading', data: null })
    await group.load(async () => zeroDashboard())
    expect(group.state.value.data?.summary.todayOrders).toBe(0)
    const request = deferred()
    const loading = group.load(() => request.promise)
    expect(group.state.value).toEqual({ status: 'loading', data: null })
    request.reject({ response: { status: 403 } })
    await loading
    expect(group.state.value).toEqual({ status: 'forbidden', data: null })
  })

  it.each(['old success/new failure', 'old failure/new success', 'old success/new success'])('%s keeps the newest result', async scenario => {
    const group = createDashboardLoader(parseDashboard)
    const old = deferred()
    const latest = deferred()
    const first = group.load(() => old.promise)
    const second = group.load(() => latest.promise)
    if (scenario.includes('new failure')) latest.reject({ response: { status: 403 } })
    else latest.resolve({ ...zeroDashboard(), summary: { ...summary, todayOrders: 7 } })
    await second
    const expected = group.state.value
    if (scenario.startsWith('old failure')) old.reject(new Error('offline'))
    else old.resolve(zeroDashboard())
    await first
    expect(group.state.value).toBe(expected)
  })

  it('does not end a newer pending load when an older response settles', async () => {
    const group = createDashboardLoader(parseDashboard)
    const old = deferred(), latest = deferred()
    const first = group.load(() => old.promise), second = group.load(() => latest.promise)
    old.resolve(zeroDashboard())
    await first
    expect(group.state.value.status).toBe('loading')
    latest.resolve(zeroDashboard())
    await second
    expect(group.state.value.status).toBe('ready')
  })

  it('invalidates pending requests on identity change and disposal, including failures', async () => {
    const group = createDashboardLoader(parseDashboard)
    const old = deferred()
    const pending = group.load(() => old.promise)
    group.invalidate()
    old.resolve(zeroDashboard())
    await pending
    expect(group.state.value).toEqual({ status: 'unavailable', data: null })
    const next = deferred()
    const loading = group.load(() => next.promise)
    group.dispose()
    const disposedState = group.state.value
    next.reject({ response: { status: 403 } })
    await loading
    await group.load(async () => zeroDashboard())
    expect(group.state.value).toBe(disposedState)
  })

  it.each([
    new Error('offline'), { response: { status: 500 } },
  ])('reports unavailable for transport failure without inventing an empty result', async error => {
    const group = createDashboardLoader(parseUnshipped)
    await group.load(async () => ({ code: 0, data: { items: [], total: 0 } }))
    await group.load(async () => { throw error })
    expect(group.state.value).toEqual({ status: 'unavailable', data: null })
  })
})

describe('existing dashboard response contracts', () => {
  it('accepts valid zero/empty, both pagination shapes and legal nullable recent-order values', () => {
    expect(parseDashboard(zeroDashboard()).summary.todayOrders).toBe(0)
    expect(parseDashboard({ ...zeroDashboard(), recentOrders: [
      { webOrderNo: 'A', shipStatus: 0, customerCd: null, quantity: null, orderDate: null },
    ] }).recentOrders).toHaveLength(1)
    for (const field of ['items', 'rows']) {
      expect(parseUnshipped({ code: 0, data: { [field]: [], total: 0 } })).toEqual({ rows: [], total: 0 })
    }
    expect(parseStockDwell(emptyDwell()).rows).toEqual([])
  })

  it('rejects incomplete or non-success payloads instead of manufacturing zero', async () => {
    const cases: [ (value: unknown) => unknown, unknown ][] = [
      [parseDashboard, { ...zeroDashboard(), summary: { todayOrders: 0 } }],
      [parseDashboard, { summary, workOrderStatus: [] }],
      [parseDashboard, { ...zeroDashboard(), code: 1 }],
      [parseDashboard, { ...zeroDashboard(), workOrderStatus: [{ status: 1 }] }],
      [parseUnshipped, { code: 0, data: { total: 0 } }],
      [parseUnshipped, { code: 0, data: { items: [] } }],
      [parseUnshipped, { code: 403, data: { items: [], total: 0 } }],
      [parseUnshipped, { code: 0, data: { items: [{ webOrderNo: 'A' }], total: 1 } }],
      [parseStockDwell, { code: 0, data: { ...emptyDwell().data, rows: undefined } }],
      [parseStockDwell, { code: 0, data: { ...emptyDwell().data, over90Qty: '0' } }],
      [parseStockDwell, { code: 1, data: emptyDwell().data }],
    ]
    for (const [parse, response] of cases) {
      const group = createDashboardLoader(parse)
      await group.load(async () => response)
      expect(group.state.value).toEqual({ status: 'unavailable', data: null })
    }
  })
})
