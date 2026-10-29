import { rmaAuthorizationApi as api, type AuthorizationPending } from '@/api/wms/rmaAuthorizations'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { identityStamp, requireIdentity } from './identity'
import { rmaPendingStore as store } from './rmaFormalPendingStore'

export const authorizationKey = (scope: RmaScope) => `authorization:${scope.tenantId}:${scope.actorId}`
export async function pendingAuthorization(scope: RmaScope): Promise<AuthorizationPending | null> {
  const value = await store.read(authorizationKey(scope)) as AuthorizationPending | undefined
  if (value === undefined) return null
  // IndexedDB is a persisted input boundary; do not erase an unrecognized original request.
  if (!value || value.version !== 1 || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.commandId)
    || !['DECIDE', 'REVOKE'].includes(value.kind) || !value.body?.authorityRef || !value.applicationId)
    throw new Error('退货授权原请求记录异常，记录已保留。')
  return value
}
async function locked<T>(scope: RmaScope, check: () => void, action: (guard: () => void) => Promise<T>): Promise<T> {
  const stamp = identityStamp(), guard = () => { requireIdentity(stamp); check() }
  if (!navigator.locks) throw new Error('浏览器不支持安全的原请求恢复。')
  return navigator.locks.request(`cp6-rma-${authorizationKey(scope)}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理退货授权，请稍后查询原请求。')
    guard(); return action(guard)
  })
}
export async function writeAuthorization(scope: RmaScope, command: AuthorizationPending, check: () => void) {
  const frozen = structuredClone(command)
  return locked(scope, check, async guard => {
    if (await pendingAuthorization(scope)) throw new Error('请先恢复原退货授权请求。')
    guard(); await store.save(authorizationKey(scope), frozen); guard()
    let result
    try { result = await api.dispatch(frozen) }
    catch (error: any) {
      guard(); const problem = error?.response?.data
      if (problem?.commitKnowledge === 'NOT_ACCEPTED' && problem.bindingCreated === false && problem.code !== 'COMMAND_BINDING_CONFLICT') await store.remove(authorizationKey(scope))
      throw error
    }
    guard(); await store.remove(authorizationKey(scope)); guard(); return result
  })
}
export async function recoverAuthorization(scope: RmaScope, retry: boolean, check: () => void) {
  return locked(scope, check, async guard => {
    const original = await pendingAuthorization(scope); guard()
    if (!original) throw new Error('没有待恢复的退货授权请求。')
    const result = await api.query(original); guard()
    if (result.status === 'NOT_OBSERVED' && !retry) return null
    const value = result.status === 'FOUND' ? result.value : await api.dispatch(original); guard()
    await store.remove(authorizationKey(scope)); guard(); return value
  })
}
