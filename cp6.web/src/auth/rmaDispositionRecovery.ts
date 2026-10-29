import { rmaDispositionApi as api, type DispositionPending } from '@/api/wms/rmaDispositions'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { identityStamp, requireIdentity } from './identity'
import { rmaPendingStore as store } from './rmaFormalPendingStore'

export const dispositionKey = (scope: RmaScope) => `disposition:${scope.tenantId}:${scope.actorId}`
export async function pendingDisposition(scope: RmaScope): Promise<DispositionPending | null> {
  const value = await store.read(dispositionKey(scope)) as DispositionPending | undefined
  if (value === undefined) return null
  if (!value || value.version !== 1 || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value.commandId)
    || !['PREPARE', 'COMMIT', 'SEAL'].includes(value.kind) || !value.body || !value.applicationId
    || !['RESALE_STOCK', 'REPAIR', 'SCRAP', 'SUPPLIER_RETURN', 'RETURN_TO_CUSTOMER'].includes(value.disposition) || value.kind !== 'PREPARE' && !value.workId) throw new Error('退货处置原请求记录异常，记录已保留。')
  return value
}
async function locked<T>(scope: RmaScope, check: () => void, action: (guard: () => void) => Promise<T>): Promise<T> {
  const stamp = identityStamp(), guard = () => { requireIdentity(stamp); check() }
  if (!navigator.locks) throw new Error('浏览器不支持安全的原请求恢复。')
  return navigator.locks.request(`cp6-rma-${dispositionKey(scope)}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理退货处置，请稍后查询原请求。')
    guard(); return action(guard)
  })
}
export async function writeDisposition(scope: RmaScope, command: DispositionPending, check: () => void) {
  const frozen = structuredClone(command)
  return locked(scope, check, async guard => {
    if (await pendingDisposition(scope)) throw new Error('请先恢复原退货处置请求。')
    guard(); await store.save(dispositionKey(scope), frozen); guard()
    let result
    try { result = await api.dispatch(frozen) }
    catch (error: any) {
      guard(); const problem = error?.response?.data
      if (problem?.commitKnowledge === 'NOT_ACCEPTED' && problem.bindingCreated === false && problem.code !== 'COMMAND_BINDING_CONFLICT') await store.remove(dispositionKey(scope))
      throw error
    }
    guard(); await store.remove(dispositionKey(scope)); guard(); return result
  })
}
export async function recoverDisposition(scope: RmaScope, retry: boolean, check: () => void) {
  return locked(scope, check, async guard => {
    const original = await pendingDisposition(scope); guard()
    if (!original) throw new Error('没有待恢复的退货处置请求。')
    const result = await api.query(original); guard()
    if (result.status === 'NOT_OBSERVED' && !retry) return null
    const value = result.status === 'FOUND' ? result.value : await api.dispatch(original); guard()
    await store.remove(dispositionKey(scope)); guard(); return value
  })
}
