import { shallowRef } from 'vue'
import type { UnshippedOrderItemDto } from '@/types/erp/order'
import type { StockDwellSummary } from '@/types/wms/stockDwell'

export type LoadStatus = 'loading' | 'ready' | 'forbidden' | 'unavailable'
type LoadState<T> = { status: 'ready'; data: T } | { status: Exclude<LoadStatus, 'ready'>; data: null }

/** Each group owns its generation. Superseded requests still run, but cannot commit. */
export function createDashboardLoader<T>(parse: (value: unknown) => T) {
  const state = shallowRef<LoadState<T>>({ status: 'loading', data: null })
  let generation = 0
  let disposed = false

  function invalidate() {
    if (disposed) return
    generation++
    state.value = { status: 'unavailable', data: null }
  }

  async function load(request: () => Promise<unknown>, contextCurrent = () => true) {
    if (disposed) return
    const current = ++generation
    state.value = { status: 'loading', data: null }
    const canCommit = () => !disposed && current === generation && contextCurrent()
    try {
      const response = await request()
      if (!canCommit()) return
      state.value = { status: 'ready', data: parse(response) }
    } catch (error) {
      if (!canCommit()) return
      // Only an explicit HTTP 403 establishes denied access. Business codes are not HTTP status.
      const forbidden = record(error) && record(error.response) && error.response.status === 403
      state.value = { status: forbidden ? 'forbidden' : 'unavailable', data: null }
    }
  }

  function dispose() {
    invalidate()
    disposed = true
  }

  return { state, load, invalidate, dispose }
}

export interface DashboardSummary {
  todayOrders: number
  monthOrders: number
  activeWorkOrders: number
  monthCompleted: number
  pendingOutbound: number
  stockWarnings: number
  pendingApprovals: number
  totalProducts: number
}
export interface DashboardData {
  summary: DashboardSummary
  recentOrders: { webOrderNo: string; customerCd?: string | null; quantity?: number | null; orderDate?: string | null; shipStatus: number }[]
  workOrderStatus: { status: number; count: number }[]
}
export interface UnshippedData { rows: UnshippedOrderItemDto[]; total: number }

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
const numeric = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value)
const text = (value: unknown): value is string => typeof value === 'string'
const optionalText = (value: unknown) => value == null || text(value)
const optionalNumber = (value: unknown) => value == null || numeric(value)
const numbers = (value: Record<string, unknown>, keys: string[]) => keys.every(key => numeric(value[key]))
function requireValid(valid: boolean): asserts valid {
  if (!valid) throw new Error('Invalid dashboard response')
}

export function parseDashboard(value: unknown): DashboardData {
  requireValid(record(value) && (!('code' in value) || value.code === 0))
  requireValid(record(value.summary) && numbers(value.summary, [
    'todayOrders', 'monthOrders', 'activeWorkOrders', 'monthCompleted',
    'pendingOutbound', 'stockWarnings', 'pendingApprovals', 'totalProducts',
  ]))
  requireValid(Array.isArray(value.recentOrders) && value.recentOrders.every(row =>
    record(row) && text(row.webOrderNo) && numeric(row.shipStatus) && optionalText(row.customerCd)
    && optionalNumber(row.quantity) && optionalText(row.orderDate)))
  requireValid(Array.isArray(value.workOrderStatus) && value.workOrderStatus.every(row =>
    record(row) && numeric(row.status) && numeric(row.count)))
  return value as unknown as DashboardData
}

function envelope(value: unknown): Record<string, unknown> {
  requireValid(record(value) && value.code === 0 && record(value.data))
  return value.data
}

export function parseUnshipped(value: unknown): UnshippedData {
  const data = envelope(value)
  // Preserve both existing PagedResultDto.items and PagedResult.rows contracts.
  const rows = data.items ?? data.rows
  requireValid(numeric(data.total) && Number.isInteger(data.total) && data.total >= 0)
  requireValid(Array.isArray(rows) && rows.every(row => record(row)
    && text(row.webOrderNo) && text(row.customerCd) && text(row.orderStatus)
    && numbers(row, ['shipStatus', 'orderedQty', 'shippedQty', 'remainingQty'])
    && typeof row.isOverdue === 'boolean' && optionalNumber(row.daysUntilDue)
    && ['customerName', 'orderDate', 'customerDeliveryDate', 'mesStatusSummary', 'wmsStatusSummary']
      .every(key => optionalText(row[key]))))
  return { rows: rows as UnshippedOrderItemDto[], total: data.total }
}

export function parseStockDwell(value: unknown): StockDwellSummary {
  const data = envelope(value)
  requireValid(text(data.asOfDate) && (data.groupBy === 'product' || data.groupBy === 'customer')
    && numbers(data, ['totalQty', 'totalValue', 'over90Qty', 'over90Value']))
  requireValid(Array.isArray(data.rows) && data.rows.every(row => record(row)
    && text(row.groupKey) && text(row.groupLabel) && optionalText(row.oldestReceiveDate)
    && numbers(row, ['totalQty', 'totalValue', 'bucket0To30Qty', 'bucket31To60Qty',
      'bucket61To90Qty', 'bucketOver90Qty', 'oldestAgeDays'])))
  return data as unknown as StockDwellSummary
}
