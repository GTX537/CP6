import { rmaCommercialApi as api, type CommercialPending } from '@/api/wms/rmaCommercial'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { identityStamp, requireIdentity } from './identity'
import { rmaPendingStore as store } from './rmaFormalPendingStore'

export const commercialKey = (scope: RmaScope) => `commercial:${scope.tenantId}:${scope.actorId}`
export async function pendingCommercial(scope: RmaScope): Promise<CommercialPending | null> {
  const value = await store.read(commercialKey(scope)) as CommercialPending | undefined
  if (value === undefined) return null
  if (!value || value.version !== 1 || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.commandId)
    || value.kind !== 'request' || !value.body || !value.applicationId) throw new Error('商业申请原请求记录异常，记录已保留。')
  return value
}
async function locked<T>(scope: RmaScope, check: () => void, action: (guard: () => void) => Promise<T>): Promise<T> {
  const stamp = identityStamp(), guard = () => { requireIdentity(stamp); check() }
  if (!navigator.locks) throw new Error('浏览器不支持安全的原请求恢复。')
  return navigator.locks.request(`cp6-rma-${commercialKey(scope)}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理商业申请，请稍后查询原请求。')
    guard(); return action(guard)
  })
}
export async function writeCommercial(scope: RmaScope, command: CommercialPending, check: () => void) {
  const frozen = structuredClone(command)
  return locked(scope, check, async guard => {
    if (await pendingCommercial(scope)) throw new Error('请先恢复原商业申请请求。')
    guard(); await store.save(commercialKey(scope), frozen); guard()
    let result
    try { result = await api.dispatch(frozen) }
    catch (error: any) {
      guard(); const problem = error?.response?.data
      if (problem?.commitKnowledge === 'NOT_ACCEPTED' && problem.bindingCreated === false && problem.code !== 'COMMAND_BINDING_CONFLICT') await store.remove(commercialKey(scope))
      throw error
    }
    guard(); await store.remove(commercialKey(scope)); guard(); return result
  })
}
export async function recoverCommercial(scope: RmaScope, retry: boolean, check: () => void) {
  return locked(scope, check, async guard => {
    const original = await pendingCommercial(scope); guard()
    if (!original) throw new Error('没有待恢复的商业申请请求。')
    const result = await api.query(original); guard()
    if (result.status === 'NOT_OBSERVED' && !retry) return null
    const value = result.status === 'FOUND' ? result.value : await api.dispatch(original); guard()
    await store.remove(commercialKey(scope)); guard(); return value
  })
}
