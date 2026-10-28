import type { RequirementSetScope } from '@/api/plm/requirementSet'
import { requirementSetReviewApi, type ApplicationOperation, type ApplicationResult, type NativeReviewResolution, type NativeReviewResult } from '@/api/plm/requirementSetReview'
import { requirementSetApi } from '@/api/plm/requirementSet'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { requirementSetResponse } from './requirementSetRecovery'
import { identityStamp, requireIdentity } from './identity'

export interface PendingRequirementSetApproval { operationId: string; objectId: string; submissionId: string; taskId: string | null; kind: 'DECISION' | 'APPLICATION' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: RequirementSetScope) {
  if (!uuid.test(scope.environmentId) || !['REAL', 'DEMO'].includes(scope.evidenceMode) || !uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_requirementSet_approval_operation_v1:${scope.environmentId.toLowerCase()}:${scope.evidenceMode}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingRequirementSetApproval(scope: RequirementSetScope): PendingRequirementSetApproval | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingRequirementSetApproval
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !uuid.test(value.submissionId)
    || !['DECISION', 'APPLICATION'].includes(value.kind) || (value.kind === 'DECISION' ? !uuid.test(value.taskId ?? '') : value.taskId !== null)) throw new Error('批准原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, submissionId: value.submissionId, taskId: value.taskId, kind: value.kind }
}
async function exclusive<T>(scope: RequirementSetScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = requirementSetResponse(await requirementSetApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function finish(scope: RequirementSetScope, original: PendingRequirementSetApproval, state: string, check: () => void) {
  check()
  if (JSON.stringify(pendingRequirementSetApproval(scope)) !== JSON.stringify(original)) throw new Error('批准原操作记录已变化。')
  if (state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return state
}
function acceptApplication(scope: RequirementSetScope, original: PendingRequirementSetApproval, value: ApplicationOperation, check: () => void) {
  check(); const r = value?.result, receipt = r?.receipt
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.objectId !== original.objectId
      || !receipt || receipt.submissionId !== original.submissionId || receipt.subject?.subjectId !== original.objectId
      || receipt.producerOwner !== 'Main.Plm.RequirementSet.v1' || !uuid.test(receipt.receiptId) || !uuid.test(receipt.decisionId)
      || receipt.application !== 'APPLIED' || !['REQUIREMENT_SET_APPROVED', 'REQUIREMENT_SET_REJECTED'].includes(receipt.outcomeCode)
      || receipt.businessResultRef?.owner !== 'Main.Plm.RequirementSetApproval.v1' || receipt.businessResultRef.version !== '1' || !uuid.test(receipt.businessResultRef.id)
      : r != null)) throw new Error('返回结果与原批准应用不一致，请保留记录。')
  return finish(scope, original, value.state, check)
}
function acceptDecision(scope: RequirementSetScope, original: PendingRequirementSetApproval, value: NativeReviewResolution, check: () => void) {
  check(); const r = value?.result
  if (value?.commandId !== original.operationId || !['COMPLETED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMPLETED' ? !r || r.commandId !== original.operationId || r.state !== 'COMPLETED'
      || r.result?.task?.taskId !== original.taskId || !uuid.test(r.result.voteId) || !uuid.test(r.result.task.instanceId)
      || r.result.decisionId !== null && !uuid.test(r.result.decisionId)
      || r.queryRef !== `/api/oa/tasks/decision-commands/${original.operationId}` : r != null)) throw new Error('返回结果与原专业评审不一致，请保留记录。')
  return finish(scope, original, value.state, check)
}
function save(scope: RequirementSetScope, original: PendingRequirementSetApproval) {
  if (!uuid.test(original.objectId) || !uuid.test(original.submissionId) || original.kind === 'DECISION' && !uuid.test(original.taskId ?? '')) throw new Error('缺少准确评审对象。')
  if (pendingRequirementSetApproval(scope)) throw new Error('请先恢复原批准操作。')
  localStorage.setItem(key(scope), JSON.stringify(original))
  if (pendingRequirementSetApproval(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
}
export function writeRequirementSetDecision(scope: RequirementSetScope, objectId: string, submissionId: string, taskId: string,
  guard: () => void, write: (id: string) => Promise<NativeReviewResult>) {
  return exclusive(scope, guard, async check => {
    const original: PendingRequirementSetApproval = { operationId: crypto.randomUUID(), objectId, submissionId, taskId, kind: 'DECISION' }
    save(scope, original); check(); const result = await write(original.operationId)
    return acceptDecision(scope, original, { commandId: original.operationId, state: 'COMPLETED', result }, check)
  })
}
export function writeRequirementSetApplication(scope: RequirementSetScope, objectId: string, submissionId: string,
  guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<ApplicationResult>>) {
  return exclusive(scope, guard, async check => {
    const original: PendingRequirementSetApproval = { operationId: crypto.randomUUID(), objectId, submissionId, taskId: null, kind: 'APPLICATION' }
    save(scope, original); check(); const result = requirementSetResponse(await write(original.operationId))
    return acceptApplication(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveRequirementSetApproval(scope: RequirementSetScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingRequirementSetApproval(scope); if (!original) throw new Error('没有待确认的批准操作。')
    return original.kind === 'APPLICATION'
      ? acceptApplication(scope, original, requirementSetResponse(await requirementSetReviewApi.applicationOperation(original.operationId, close)), check)
      : acceptDecision(scope, original, await requirementSetReviewApi.decisionOperation(original.operationId, original.taskId!, close), check)
  })
}
