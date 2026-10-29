import { rmaCorrectionApi as api, type CorrectionPending } from '@/api/wms/rmaCorrections'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { identityStamp, requireIdentity } from './identity'
import { rmaPendingStore as store } from './rmaFormalPendingStore'

export const correctionKey = (scope: RmaScope) => `correction:${scope.tenantId}:${scope.actorId}`
export async function pendingCorrection(scope: RmaScope): Promise<CorrectionPending | null> {
  const value = await store.read(correctionKey(scope)) as CorrectionPending | undefined
  if (value === undefined) return null
  if (!value || value.version !== 1 || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.commandId)
    || !['draft', 'edit', 'submit', 'bind-approval', 'prepare', 'apply', 'withdraw'].includes(value.kind) || !value.body || !value.applicationId
    || value.kind !== 'draft' && !value.proposalId) throw new Error('退货冲正原请求记录异常，记录已保留。')
  return value
}
async function locked<T>(scope: RmaScope, check: () => void, action: (guard: () => void) => Promise<T>): Promise<T> {
  const stamp = identityStamp(), guard = () => { requireIdentity(stamp); check() }
  if (!navigator.locks) throw new Error('浏览器不支持安全的原请求恢复。')
  return navigator.locks.request(`cp6-rma-${correctionKey(scope)}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理退货冲正，请稍后查询原请求。')
    guard(); return action(guard)
  })
}
export async function writeCorrection(scope: RmaScope, command: CorrectionPending, check: () => void) {
  const frozen = structuredClone(command)
  return locked(scope, check, async guard => {
    if (await pendingCorrection(scope)) throw new Error('请先恢复原退货冲正请求。')
    guard(); await store.save(correctionKey(scope), frozen); guard()
    let result
    try { result = await api.dispatch(frozen) }
    catch (error: any) {
      guard(); const problem = error?.response?.data
      if (problem?.commitKnowledge === 'NOT_ACCEPTED' && problem.bindingCreated === false && problem.code !== 'COMMAND_BINDING_CONFLICT') await store.remove(correctionKey(scope))
      throw error
    }
    guard(); await store.remove(correctionKey(scope)); guard(); return result
  })
}
export async function recoverCorrection(scope: RmaScope, retry: boolean, check: () => void) {
  return locked(scope, check, async guard => {
    const original = await pendingCorrection(scope); guard()
    if (!original) throw new Error('没有待恢复的退货冲正请求。')
    const result = await api.query(original); guard()
    if (result.status === 'NOT_OBSERVED' && !retry) return null
    const value = result.status === 'FOUND' ? result.value : await api.dispatch(original); guard()
    await store.remove(correctionKey(scope)); guard(); return value
  })
}
