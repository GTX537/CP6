import { rmaQualityApi as api, type QualityPending } from '@/api/wms/rmaQuality'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { identityStamp, requireIdentity } from './identity'
import { rmaPendingStore as store } from './rmaFormalPendingStore'

export const qualityKey = (scope: RmaScope) => `quality:${scope.tenantId}:${scope.actorId}`
export async function pendingQuality(scope: RmaScope): Promise<QualityPending | null> {
  const value = await store.read(qualityKey(scope)) as QualityPending | undefined
  if (value === undefined) return null
  if (!value || value.version !== 1 || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.commandId)
    || !['START', 'DECIDE', 'WITHDRAW', 'OBSERVE'].includes(value.kind) || !value.body || !value.applicationId
    || value.kind !== 'START' && !value.inspectionId) throw new Error('退货质量原请求记录异常，记录已保留。')
  return value
}
async function locked<T>(scope: RmaScope, check: () => void, action: (guard: () => void) => Promise<T>): Promise<T> {
  const stamp = identityStamp(), guard = () => { requireIdentity(stamp); check() }
  if (!navigator.locks) throw new Error('浏览器不支持安全的原请求恢复。')
  return navigator.locks.request(`cp6-rma-${qualityKey(scope)}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理退货质量，请稍后查询原请求。')
    guard(); return action(guard)
  })
}
export async function writeQuality(scope: RmaScope, command: QualityPending, check: () => void) {
  const frozen = structuredClone(command)
  return locked(scope, check, async guard => {
    if (await pendingQuality(scope)) throw new Error('请先恢复原退货质量请求。')
    guard(); await store.save(qualityKey(scope), frozen); guard()
    let result
    try { result = await api.dispatch(frozen) }
    catch (error: any) {
      guard(); const problem = error?.response?.data
      if (problem?.commitKnowledge === 'NOT_ACCEPTED' && problem.bindingCreated === false && problem.code !== 'COMMAND_BINDING_CONFLICT') await store.remove(qualityKey(scope))
      throw error
    }
    guard(); await store.remove(qualityKey(scope)); guard(); return result
  })
}
export async function recoverQuality(scope: RmaScope, retry: boolean, check: () => void) {
  return locked(scope, check, async guard => {
    const original = await pendingQuality(scope); guard()
    if (!original) throw new Error('没有待恢复的退货质量请求。')
    const result = await api.query(original); guard()
    if (result.status === 'NOT_OBSERVED' && !retry) return null
    const value = result.status === 'FOUND' ? result.value : await api.dispatch(original); guard()
    await store.remove(qualityKey(scope)); guard(); return value
  })
}
