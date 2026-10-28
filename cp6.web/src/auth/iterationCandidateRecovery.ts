import type { EngineeringIterationScope } from '@/api/plm/engineeringIteration'
import { engineeringIterationApi } from '@/api/plm/engineeringIteration'
import { candidateApi, type CandidateOperation, type CandidateResult } from '@/api/plm/iterationCandidate'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingCandidate { operationId: string; iterationId: string; candidateId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EngineeringIterationScope) {
  if (!uuid.test(scope.environmentId) || !['REAL', 'DEMO'].includes(scope.evidenceMode) || !uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_iterationCandidate_operation_v1:${scope.environmentId.toLowerCase()}:${scope.evidenceMode}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function candidateResponse<T>(response: EngineeringEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认候选方案原结果。')
  return response.data
}
export function pendingCandidate(scope: EngineeringIterationScope): PendingCandidate | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingCandidate
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.iterationId) || !uuid.test(value.candidateId)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: value.operationId, iterationId: value.iterationId, candidateId: value.candidateId }
}
async function exclusive<T>(scope: EngineeringIterationScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = candidateResponse(await engineeringIterationApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringIterationScope, original: PendingCandidate, value: CandidateOperation, check: () => void) {
  check(); const result = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !result || result.operationId !== original.operationId || result.iterationId !== original.iterationId || result.candidateId !== original.candidateId
      || !/^[1-9][0-9]*$/.test(result.version) || !/^[0-9a-f]{64}$/.test(result.digest) : result != null)) throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingCandidate(scope)
  if (pending?.operationId !== original.operationId || pending.iterationId !== original.iterationId || pending.candidateId !== original.candidateId) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveCandidate(scope: EngineeringIterationScope, iterationId: string, candidateId: string, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<CandidateResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(iterationId) || !uuid.test(candidateId)) throw new Error('请先选择当前迭代和方案。')
    if (pendingCandidate(scope)) throw new Error('请先查询或结束原操作。')
    const original: PendingCandidate = { operationId: crypto.randomUUID(), iterationId, candidateId }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingCandidate(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = candidateResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveCandidate(scope: EngineeringIterationScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingCandidate(scope); if (!original) throw new Error('没有待确认的原操作。')
    return accept(scope, original, candidateResponse(await candidateApi.operation(original.operationId, close)), check)
  })
}
