import { identityStamp, requireIdentity } from './identity'
import type { PriceCorrectionOperationScope } from '@/types/erp/order'

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: PriceCorrectionOperationScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('現在のユーザーを確認できません。')
  return `cp6_price_operation_v1:${scope.tenantId}:${scope.actorId}`
}
export function pendingPriceOperation(scope: PriceCorrectionOperationScope): string | null {
  const id = localStorage.getItem(key(scope))
  if (id !== null && !uuid.test(id)) throw new Error('元の操作記録を確認してください。')
  return id
}
export async function withPriceOperation<T>(scope: PriceCorrectionOperationScope, create: boolean,
  action: (id: string, stamp: string) => Promise<T>): Promise<T> {
  if (!navigator.locks?.request) throw new Error('このブラウザーでは安全な保存ロックを利用できません。')
  const stamp = identityStamp()
  return navigator.locks.request(key(scope), { mode: 'exclusive' }, async () => {
    requireIdentity(stamp)
    let id = pendingPriceOperation(scope)
    if (create) {
      if (id) throw new Error('先に元の操作結果を確認してください。')
      id = crypto.randomUUID()
      // Public reference only. No prices, customer identifiers or draft survive in browser storage.
      localStorage.setItem(key(scope), id)
    }
    if (!id) throw new Error('確認対象の操作がありません。')
    const result = await action(id, stamp); requireIdentity(stamp); return result
  })
}
export function completePriceOperation(scope: PriceCorrectionOperationScope, id: string, stamp: string) {
  requireIdentity(stamp)
  if (pendingPriceOperation(scope) !== id) throw new Error('元の操作記録が変わりました。')
  localStorage.removeItem(key(scope))
}
