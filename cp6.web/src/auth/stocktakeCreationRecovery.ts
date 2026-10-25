import { stockTakeApi } from '@/api/wms/stockTake'
import type { StocktakeCreationScope, StocktakeCreationResult, StocktakeCreationOperation } from '@/types/wms/stocktakeCreation'
import { identityStamp, requireIdentity } from './identity'

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const nonzero = (value: unknown): value is string => typeof value === 'string' && uuid.test(value) && value !== '00000000-0000-0000-0000-000000000000'
function key(scope: StocktakeCreationScope) {
  if (!nonzero(scope.tenantId) || !nonzero(scope.actorId)) throw new Error('現在のユーザーを確認できません。')
  return `cp6_stocktake_creation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingStocktakeCreation(scope: StocktakeCreationScope): string | null {
  const value = localStorage.getItem(key(scope))
  if (value !== null && !nonzero(value)) throw new Error('保存された操作番号を確認してください。')
  return value
}
export function stocktakeResponse<T>(response: { code: number; data: T }): T {
  if (response?.code !== 0 || response.data == null) throw new Error('操作結果を確認できません。')
  return response.data
}
async function exclusive<T>(scope: StocktakeCreationScope, creating: boolean, guard: () => void, run: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('このブラウザーでは安全な保存ロックを利用できません。')
  const stamp = identityStamp()
  return navigator.locks.request(key(scope), { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('別のタブで操作中です。完了後に操作状態を更新してください。')
    const check = () => { requireIdentity(stamp); guard() }
    check()
    const current = stocktakeResponse(await (creating ? stockTakeApi.creationContext() : stockTakeApi.creationScope())); check()
    if (key(current) !== key(scope)) throw new Error('現在のユーザーが変わりました。操作状態を更新してください。')
    return run(check)
  })
}
function accept(scope: StocktakeCreationScope, operation: string, value: StocktakeCreationOperation, check: () => void) {
  check()
  const result = value?.result
  if (value?.operationId !== operation || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.status) ||
    (value.status === 'COMMITTED' ? result?.operationId !== operation || !nonzero(result?.stockTakeId) ||
      typeof result?.stockTakeNo !== 'string' || !result.stockTakeNo.trim() || (result.departmentId !== null && !nonzero(result.departmentId)) : result != null)) {
    throw new Error('元の操作と結果が一致しません。')
  }
  if (pendingStocktakeCreation(scope) !== operation) throw new Error('保存された操作番号が変わりました。')
  if (value.status !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveStocktakeCreation(scope: StocktakeCreationScope, guard: () => void,
  write: (id: string) => Promise<{ code: number; data: StocktakeCreationResult }>) {
  return exclusive(scope, true, guard, async check => {
    if (pendingStocktakeCreation(scope)) throw new Error('先に前回の操作結果を確認してください。')
    const operation = crypto.randomUUID()
    // Only the original reference survives reload/logout; never store stocktake data or department choices.
    localStorage.setItem(key(scope), operation)
    if (pendingStocktakeCreation(scope) !== operation) throw new Error('操作番号を保存できません。')
    check()
    const result = stocktakeResponse(await write(operation))
    return accept(scope, operation, { operationId: operation, status: 'COMMITTED', result }, check)
  })
}
export function resolveStocktakeCreation(scope: StocktakeCreationScope, close: boolean, guard: () => void) {
  return exclusive(scope, false, guard, async check => {
    const operation = pendingStocktakeCreation(scope)
    if (!operation) throw new Error('確認対象の操作がありません。')
    const value = stocktakeResponse(await (close ? stockTakeApi.closeCreation(operation) : stockTakeApi.readCreation(operation)))
    return accept(scope, operation, value, check)
  })
}
