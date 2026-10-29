import { rmaDeliveryEvidenceApi as api, type DeliveryEvidencePending } from '@/api/wms/rmaDeliveryEvidence'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { identityStamp, requireIdentity } from './identity'
import { rmaPendingStore as store } from './rmaFormalPendingStore'

export const deliveryEvidenceKey = (scope: RmaScope) => `delivery-evidence:${scope.tenantId}:${scope.actorId}`
export async function pendingDeliveryEvidence(scope: RmaScope): Promise<DeliveryEvidencePending | null> {
  const value = await store.read(deliveryEvidenceKey(scope)) as DeliveryEvidencePending | undefined
  if (value === undefined) return null
  // IndexedDB is a persisted input boundary; do not erase an unrecognized original request.
  if (!value || value.version !== 1 || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.commandId)
    || !['RECORD', 'WITHDRAW'].includes(value.kind) || !value.body?.scopeOptionRef)
    throw new Error('签收证据原请求记录异常，记录已保留。')
  return value
}
async function locked<T>(scope: RmaScope, check: () => void, action: (guard: () => void) => Promise<T>): Promise<T> {
  const stamp = identityStamp(), guard = () => { requireIdentity(stamp); check() }
  if (!navigator.locks) throw new Error('浏览器不支持安全的原请求恢复。')
  return navigator.locks.request(`cp6-rma-${deliveryEvidenceKey(scope)}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理签收证据，请稍后查询原请求。')
    guard(); return action(guard)
  })
}
export async function writeDeliveryEvidence(scope: RmaScope, command: DeliveryEvidencePending, check: () => void) {
  const frozen = structuredClone(command)
  return locked(scope, check, async guard => {
    if (await pendingDeliveryEvidence(scope)) throw new Error('请先恢复原签收证据请求。')
    guard(); await store.save(deliveryEvidenceKey(scope), frozen); guard()
    let result
    try { result = await api.dispatch(frozen) }
    catch (error: any) {
      guard(); const problem = error?.response?.data
      if (problem?.commitKnowledge === 'NOT_ACCEPTED' && problem.bindingCreated === false && problem.code !== 'COMMAND_BINDING_CONFLICT') await store.remove(deliveryEvidenceKey(scope))
      throw error
    }
    guard(); await store.remove(deliveryEvidenceKey(scope)); guard(); return result
  })
}
export async function recoverDeliveryEvidence(scope: RmaScope, retry: boolean, check: () => void) {
  return locked(scope, check, async guard => {
    const original = await pendingDeliveryEvidence(scope); guard()
    if (!original) throw new Error('没有待恢复的签收证据请求。')
    const result = await api.query(original); guard()
    if (result.status === 'NOT_OBSERVED' && !retry) return null
    const value = result.status === 'FOUND' ? result.value : await api.dispatch(original); guard()
    await store.remove(deliveryEvidenceKey(scope)); guard(); return value
  })
}
