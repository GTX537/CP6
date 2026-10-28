import type { LocationRuleReviewScope } from '@/api/wms/locationRuleReview'
import { locationRuleReviewApi, type ApplicationOperation, type ApplicationResult, type NativeReviewResolution, type NativeReviewResult } from '@/api/wms/locationRuleReview'

import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { locationRuleResponse } from './locationRuleReviewResponse'
import { identityStamp, requireIdentity } from './identity'

export interface PendingLocationRuleApproval { operationId: string; objectId: string; submissionId: string; taskId: string | null; kind: 'DECISION' | 'APPLICATION' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: LocationRuleReviewScope) {
  if (!uuid.test(scope.environmentId) || !['REAL', 'DEMO'].includes(scope.evidenceMode) || !uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_locationRule_approval_operation_v1:${scope.environmentId.toLowerCase()}:${scope.evidenceMode}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingLocationRuleApproval(scope: LocationRuleReviewScope): PendingLocationRuleApproval | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingLocationRuleApproval
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !uuid.test(value.submissionId)
    || !['DECISION', 'APPLICATION'].includes(value.kind) || (value.kind === 'DECISION' ? !uuid.test(value.taskId ?? '') : value.taskId !== null)) throw new Error('批准原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, submissionId: value.submissionId, taskId: value.taskId, kind: value.kind }
}
async function exclusive<T>(scope: LocationRuleReviewScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = locationRuleResponse(await locationRuleReviewApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function finish(scope: LocationRuleReviewScope, original: PendingLocationRuleApproval, state: string, check: () => void) {
  check()
  if (JSON.stringify(pendingLocationRuleApproval(scope)) !== JSON.stringify(original)) throw new Error('批准原操作记录已变化。')
  if (state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return state
}
function acceptApplication(scope: LocationRuleReviewScope, original: PendingLocationRuleApproval, value: ApplicationOperation, check: () => void) {
  check(); const r = value?.result, receipt = r?.receipt
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.objectId !== original.objectId
      || !receipt || receipt.submissionId !== original.submissionId || receipt.subject?.subjectId !== original.objectId
      || receipt.producerOwner !== 'Main.Wms.LocationRuleProposal.v1' || !uuid.test(receipt.receiptId) || !uuid.test(receipt.decisionId)
      || (receipt.application === 'STALE' ? receipt.outcomeCode !== 'RULE_REVIEW_ROUND_NOT_CURRENT' || receipt.businessResultRef !== null
        : receipt.application !== 'APPLIED' || !['LOCATION_RULE_APPROVED', 'LOCATION_RULE_REJECTED'].includes(receipt.outcomeCode)
          || receipt.businessResultRef?.owner !== 'Main.Wms.LocationRuleApproval.v1' || receipt.businessResultRef.version !== '1' || !uuid.test(receipt.businessResultRef.id))
      : r != null)) throw new Error('返回结果与原批准应用不一致，请保留记录。')
  return finish(scope, original, value.state, check)
}
function acceptDecision(scope: LocationRuleReviewScope, original: PendingLocationRuleApproval, value: NativeReviewResolution, check: () => void) {
  check(); const r = value?.result
  if (value?.commandId !== original.operationId || !['COMPLETED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMPLETED' ? !r || r.commandId !== original.operationId || r.state !== 'COMPLETED'
      || r.result?.task?.taskId !== original.taskId || !uuid.test(r.result.voteId) || !uuid.test(r.result.task.instanceId)
      || r.result.decisionId !== null && !uuid.test(r.result.decisionId)
      || r.queryRef !== `/api/oa/tasks/decision-commands/${original.operationId}` : r != null)) throw new Error('返回结果与原专业评审不一致，请保留记录。')
  return finish(scope, original, value.state, check)
}
function save(scope: LocationRuleReviewScope, original: PendingLocationRuleApproval) {
  if (!uuid.test(original.objectId) || !uuid.test(original.submissionId) || original.kind === 'DECISION' && !uuid.test(original.taskId ?? '')) throw new Error('缺少准确评审对象。')
  if (pendingLocationRuleApproval(scope)) throw new Error('请先恢复原批准操作。')
  localStorage.setItem(key(scope), JSON.stringify(original))
  if (pendingLocationRuleApproval(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
}
export function writeLocationRuleDecision(scope: LocationRuleReviewScope, objectId: string, submissionId: string, taskId: string,
  guard: () => void, write: (id: string) => Promise<NativeReviewResult>) {
  return exclusive(scope, guard, async check => {
    const original: PendingLocationRuleApproval = { operationId: crypto.randomUUID(), objectId, submissionId, taskId, kind: 'DECISION' }
    save(scope, original); check(); const result = await write(original.operationId)
    return acceptDecision(scope, original, { commandId: original.operationId, state: 'COMPLETED', result }, check)
  })
}
export function writeLocationRuleApplication(scope: LocationRuleReviewScope, objectId: string, submissionId: string,
  guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<ApplicationResult>>) {
  return exclusive(scope, guard, async check => {
    const original: PendingLocationRuleApproval = { operationId: crypto.randomUUID(), objectId, submissionId, taskId: null, kind: 'APPLICATION' }
    save(scope, original); check(); const result = locationRuleResponse(await write(original.operationId))
    return acceptApplication(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveLocationRuleApproval(scope: LocationRuleReviewScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingLocationRuleApproval(scope); if (!original) throw new Error('没有待确认的批准操作。')
    return original.kind === 'APPLICATION'
      ? acceptApplication(scope, original, locationRuleResponse(await locationRuleReviewApi.applicationOperation(original.operationId, close)), check)
      : acceptDecision(scope, original, await locationRuleReviewApi.decisionOperation(original.operationId, original.taskId!, close), check)
  })
}
