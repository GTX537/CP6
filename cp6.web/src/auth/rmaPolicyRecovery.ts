import { rmaPolicyApi as api, type RmaPolicyPending } from '@/api/wms/rmaPolicies'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { identityStamp, requireIdentity } from './identity'
import { rmaPendingStore as store } from './rmaFormalPendingStore'

export const rmaPolicyKey = (scope: RmaScope) => `policy:${scope.tenantId}:${scope.actorId}`
export async function pendingPolicy(scope: RmaScope): Promise<RmaPolicyPending | null> {
  const value = await store.read(rmaPolicyKey(scope)) as RmaPolicyPending | undefined
  if (value === undefined) return null
  if (!value || value.version !== 1 || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.commandId)
    || !value.body?.scopeOptionRef || !value.body.policy || typeof value.body.reason !== 'string')
    throw new Error('政策原请求记录异常，记录已保留。')
  return value
}
async function locked<T>(scope: RmaScope, check: () => void, action: (guard: () => void) => Promise<T>): Promise<T> {
  const stamp = identityStamp(), guard = () => { requireIdentity(stamp); check() }
  if (!navigator.locks) throw new Error('浏览器不支持安全的原请求恢复。')
  return navigator.locks.request(`cp6-rma-${rmaPolicyKey(scope)}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理政策，请稍后查询原请求。')
    guard(); return action(guard)
  })
}
export async function publishPolicy(scope: RmaScope, command: RmaPolicyPending, check: () => void) {
  const frozen = structuredClone(command)
  return locked(scope, check, async guard => {
    if (await pendingPolicy(scope)) throw new Error('请先恢复原政策请求。')
    guard(); await store.save(rmaPolicyKey(scope), frozen); guard()
    let result
    try { result = await api.publish(frozen) }
    catch (error: any) {
      guard(); const problem = error?.response?.data
      if (problem?.commitKnowledge === 'NOT_ACCEPTED' && problem.bindingCreated === false && problem.code !== 'COMMAND_BINDING_CONFLICT') await store.remove(rmaPolicyKey(scope))
      throw error
    }
    guard(); await store.remove(rmaPolicyKey(scope)); guard(); return result
  })
}
export async function recoverPolicy(scope: RmaScope, retry: boolean, check: () => void) {
  return locked(scope, check, async guard => {
    const original = await pendingPolicy(scope); guard()
    if (!original) throw new Error('没有待恢复的政策请求。')
    const result = await api.query(original); guard()
    if (result.status === 'NOT_OBSERVED' && !retry) return null
    const value = result.status === 'FOUND' ? result.value : await api.publish(original); guard()
    await store.remove(rmaPolicyKey(scope)); guard(); return value
  })
}
