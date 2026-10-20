import { iamApi, type IamKind, type IamOperation, type IamScope } from '@/api/sys/iam'
import { identityStamp, requireIdentity } from './identity'

export interface PendingIamOperation { operationId: string; kind: IamKind; entityId?: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const kinds: IamKind[] = ['user-create', 'user-update', 'user-roles', 'user-org', 'dept-create', 'dept-update', 'dept-move', 'dept-leader', 'dept-delete']
function key(scope: IamScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_iam_operation_v1:${scope.tenantId}:${scope.actorId}`
}
export function pendingIamOperation(scope: IamScope): PendingIamOperation | null {
  const value = localStorage.getItem(key(scope)); if (value === null) return null
  const parsed = JSON.parse(value) as PendingIamOperation
  if (!uuid.test(parsed.operationId) || !kinds.includes(parsed.kind) || parsed.entityId !== undefined && !uuid.test(parsed.entityId)) throw new Error('原操作记录异常，请联系管理员核对。')
  return parsed
}
async function exclusive<T>(scope: IamScope, action: (stamp: string) => Promise<T>): Promise<T> {
  if (!navigator.locks?.request) throw new Error('浏览器不支持安全恢复锁，暂不能保存。')
  const stamp = identityStamp()
  return navigator.locks.request(key(scope), { mode: 'exclusive' }, async () => {
    requireIdentity(stamp); const result = await action(stamp); requireIdentity(stamp); return result
  })
}
function accept(scope: IamScope, original: PendingIamOperation, result: IamOperation, stamp: string) {
  requireIdentity(stamp)
  if (result.operationId !== original.operationId || result.kind !== original.kind || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(result.state)) throw new Error('原操作结果不匹配。')
  if (result.state !== 'UNKNOWN' && pendingIamOperation(scope)?.operationId === original.operationId) localStorage.removeItem(key(scope))
  return result
}
export function saveIamOperation(scope: IamScope, kind: IamKind, entityId: string | undefined, write: (id: string) => Promise<IamOperation>) {
  return exclusive(scope, async stamp => {
    if (pendingIamOperation(scope)) throw new Error('请先查询或封闭上一次操作。')
    const original = { operationId: crypto.randomUUID(), kind, entityId }
    // Public recovery reference only. No password, user profile, or draft is stored across tabs.
    localStorage.setItem(key(scope), JSON.stringify(original))
    try { return accept(scope, original, await write(original.operationId), stamp) }
    catch (error: any) {
      // Even a definite validation failure keeps the original reference until it is accurately
      // closed. An interrupted close or another tab can never silently manufacture a new key.
      throw error
    }
  })
}
export function resolveIamOperation(scope: IamScope, close = false) {
  return exclusive(scope, async stamp => {
    const original = pendingIamOperation(scope); if (!original) throw new Error('没有待恢复操作。')
    return accept(scope, original, await iamApi.resolve(original.operationId, original.kind, close), stamp)
  })
}
