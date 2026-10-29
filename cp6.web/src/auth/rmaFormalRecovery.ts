import { rmaFormalApi as api, type RmaPending } from '@/api/wms/rmaFormalApplications'
import type { RmaScope } from '@/api/wms/rmaApplications'
import { identityStamp, requireIdentity } from './identity'
import { rmaPendingStore as store } from './rmaFormalPendingStore'

export const rmaFormalKey = (scope: RmaScope) => `${scope.tenantId}:${scope.actorId}`
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export async function pendingFormal(scope: RmaScope): Promise<RmaPending | null> {
  const value = await store.read(rmaFormalKey(scope)) as RmaPending | undefined
  if (value === undefined) return null
  // Browser storage is an external boundary. Malformed records remain available for investigation.
  if (!value || value.version !== 1 || !uuid.test(value.commandId) || !uuid.test(value.applicationId)
    || !['CREATE', 'EDIT', 'SUBMIT', 'WITHDRAW'].includes(value.kind) || !value.scopeOptionRef
    || typeof value.body !== 'object' || !value.body || (value.kind !== 'CREATE' && typeof value.etag !== 'string'))
    throw new Error('原请求记录异常，已保留记录；请勿新建替代操作。')
  return value
}
async function locked<T>(scope: RmaScope, check: () => void, action: (guard: () => void) => Promise<T>): Promise<T> {
  const stamp = identityStamp(), guard = () => { requireIdentity(stamp); check() }
  if (!navigator.locks) throw new Error('浏览器不支持安全的原请求恢复。')
  return navigator.locks.request(`cp6-rma-formal:${rmaFormalKey(scope)}`, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理退货申请，请稍后查询原请求。')
    guard(); return action(guard)
  })
}
export async function writeFormal(scope: RmaScope, command: RmaPending, check: () => void) {
  const frozen = structuredClone(command)
  return locked(scope, check, async guard => {
    if (await pendingFormal(scope)) throw new Error('请先恢复原请求。')
    guard(); await store.save(rmaFormalKey(scope), frozen); guard()
    let result
    try { result = await api.dispatch(frozen) }
    catch (error: any) {
      guard()
      const problem = error?.response?.data
      if (problem?.commitKnowledge === 'NOT_ACCEPTED' && problem.bindingCreated === false && problem.code !== 'COMMAND_BINDING_CONFLICT')
        await store.remove(rmaFormalKey(scope))
      throw error
    }
    guard(); await store.remove(rmaFormalKey(scope)); guard(); return result
  })
}
export async function recoverFormal(scope: RmaScope, retry: boolean, check: () => void) {
  return locked(scope, check, async guard => {
    const pending = await pendingFormal(scope); guard()
    if (!pending) throw new Error('没有待恢复的原请求。')
    const result = await api.query(pending); guard()
    if (result.status === 'FOUND') {
      const reference = result.resultRefs[0]!
      const application = await api.read(reference.id, Number(reference.version)); guard()
      await store.remove(rmaFormalKey(scope)); guard(); return application
    }
    if (!retry) return null
    // NOT_OBSERVED is not proof of no effect. Only resend the stored key, exact body and original If-Match.
    const application = await api.dispatch(pending); guard()
    await store.remove(rmaFormalKey(scope)); guard(); return application
  })
}
