import { stockApi } from '@/api/wms/stock'
import type { StockOperation, StockOperationResult, StockOperationScope, WmsApi } from '@/types/wms/wms'
import { identityStamp, requireIdentity } from './identity'

export interface PendingStockOperation { operationId: string; kind: 'APPLY' | 'MOVE' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: StockOperationScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('現在のユーザーを確認できません。')
  return `cp6_stock_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingStockOperation(scope: StockOperationScope): PendingStockOperation | null {
  const raw = localStorage.getItem(key(scope)); if (raw === null) return null
  const value = JSON.parse(raw) as PendingStockOperation
  if (!value || !uuid.test(value.operationId) || !['APPLY', 'MOVE'].includes(value.kind)) throw new Error('保存された元の操作記録を確認してください。')
  return { operationId: value.operationId, kind: value.kind }
}
export function stockResponse<T>(response: WmsApi<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('操作結果を確認できません。')
  return response.data
}
function resultMatches(original: PendingStockOperation, result: StockOperationResult | null | undefined) {
  const reference = (value: unknown) => typeof value === 'string' && value.trim().length > 0
  return result?.operationId === original.operationId && result.kind === original.kind &&
    (result.kind === 'APPLY' ? reference(result.txnNo) : reference(result.outTxnNo) && reference(result.inTxnNo) && result.outTxnNo !== result.inTxnNo)
}
async function exclusive<T>(scope: StockOperationScope, guard: () => void, run: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('このブラウザーでは安全な保存ロックを利用できません。')
  const stamp = identityStamp()
  // Never queue a second submit behind a successful first submit: that could create a new key.
  return navigator.locks.request(key(scope), { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('別のタブで操作中です。完了後に操作状態を更新してください。')
    const check = () => { requireIdentity(stamp); guard() }
    check()
    const current = stockResponse(await stockApi.operationScope()); check()
    if (key(current) !== key(scope)) throw new Error('現在のユーザーが変わりました。操作状態を更新してください。')
    return run(check)
  })
}
function accept(scope: StockOperationScope, original: PendingStockOperation, value: StockOperation, check: () => void) {
  check()
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state) ||
    (value.state === 'COMMITTED' ? !resultMatches(original, value.result) : value.result != null)) throw new Error('元の操作と結果が一致しません。')
  const saved = pendingStockOperation(scope)
  if (saved?.operationId !== original.operationId || saved.kind !== original.kind) throw new Error('元の操作記録が変わりました。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveStockOperation(scope: StockOperationScope, kind: PendingStockOperation['kind'], guard: () => void,
  write: (id: string) => Promise<WmsApi<StockOperationResult>>) {
  return exclusive(scope, guard, async check => {
    if (pendingStockOperation(scope)) throw new Error('先に前回の操作結果を確認してください。')
    const original = { operationId: crypto.randomUUID(), kind }
    // Only a public recovery reference survives refresh/logout. No inventory, price or draft is stored.
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingStockOperation(scope)?.operationId !== original.operationId) throw new Error('操作番号を保存できません。')
    check()
    const result = stockResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveStockOperation(scope: StockOperationScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingStockOperation(scope); if (!original) throw new Error('確認対象の操作がありません。')
    const result = stockResponse(await (close ? stockApi.closeOperation(original.operationId) : stockApi.readOperation(original.operationId)))
    return accept(scope, original, result, check)
  })
}

/** Native Stock decimal(21,8), with no binary floating point conversion or rounding. */
export function stockQuantity(input: string, positive: boolean): string {
  const text = input.trim()
  if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(text)) throw new Error('数量を十進数で入力してください。')
  const negative = text.startsWith('-'), parts = text.replace(/^[+-]/, '').split('.')
  const whole = (parts[0] || '0').replace(/^0+(?=\d)/, ''), fraction = (parts[1] || '').replace(/0+$/, '')
  if (whole.length > 13 || fraction.length > 8) throw new Error('数量は整数13桁・小数8桁以内で入力してください。')
  const zero = whole === '0' && !fraction
  if (positive && (negative || zero)) throw new Error('移動数量は0より大きい値を入力してください。')
  return (negative && !zero ? '-' : '') + whole + (fraction ? '.' + fraction : '')
}
