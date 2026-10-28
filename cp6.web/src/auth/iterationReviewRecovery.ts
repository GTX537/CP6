import type { EngineeringIterationScope } from '@/api/plm/engineeringIteration'
import { iterationReviewApi, type ReviewOperation, type ReviewResult } from '@/api/plm/iterationReview'
import { engineeringIterationApi } from '@/api/plm/engineeringIteration'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { requirementSetResponse as iterationResponse } from './requirementSetRecovery'
import { identityStamp, requireIdentity } from './identity'

export interface PendingIterationReview { operationId: string; objectId: string; kind: 'POLICY' | 'SUBMIT' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EngineeringIterationScope) {
  if (!uuid.test(scope.environmentId) || !['REAL', 'DEMO'].includes(scope.evidenceMode) || !uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_iteration_review_operation_v1:${scope.environmentId.toLowerCase()}:${scope.evidenceMode}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingIterationReview(scope: EngineeringIterationScope): PendingIterationReview | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingIterationReview
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !['POLICY', 'SUBMIT'].includes(value.kind)) throw new Error('评审原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, kind: value.kind }
}
async function exclusive<T>(scope: EngineeringIterationScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = iterationResponse(await engineeringIterationApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringIterationScope, original: PendingIterationReview, value: ReviewOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId || receipt.iterationId !== original.objectId
      || receipt.kind !== original.kind || !uuid.test(receipt.reference?.id) || receipt.reference.version !== '1'
      || !/^[0-9a-f]{64}$/.test(receipt.reference.digest)
      || (receipt.kind === 'POLICY' ? receipt.reference.owner !== 'Main.Plm.IterationReviewPolicy.v1' || receipt.submission != null
        : receipt.reference.owner !== 'Main.Plm.IterationReviewSubmission.v1' || receipt.submission?.submissionId !== receipt.reference.id
          || receipt.submission?.iteration.id !== original.objectId || receipt.submission?.applicationState !== 'NOT_RECEIVED' || receipt.submission?.businessStage !== 'IN_REVIEW')
      : receipt != null)) throw new Error('返回结果与原评审操作不一致，请保留记录。')
  const pending = pendingIterationReview(scope)
  if (JSON.stringify(pending) !== JSON.stringify(original)) throw new Error('原评审操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function writeIterationReview(scope: EngineeringIterationScope, objectId: string, kind: 'POLICY' | 'SUBMIT', guard: () => void,
  write: (id: string) => Promise<EngineeringEnvelope<ReviewResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId)) throw new Error('请先选择当前迭代。')
    if (pendingIterationReview(scope)) throw new Error('请先查询或结束原评审操作。')
    const original: PendingIterationReview = { operationId: crypto.randomUUID(), objectId, kind }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingIterationReview(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = iterationResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveIterationReview(scope: EngineeringIterationScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingIterationReview(scope); if (!original) throw new Error('没有待确认的评审操作。')
    return accept(scope, original, iterationResponse(await iterationReviewApi.operation(original.operationId, close)), check)
  })
}
