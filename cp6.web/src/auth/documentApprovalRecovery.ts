import { documentReviewApi, type ApplicationOperation, type ApplicationResult, type NativeReviewResolution, type NativeReviewResult } from '@/api/plm/documentReview'
import { documentApi } from '@/api/plm/controlledDocument'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { documentResponse } from './documentRecovery'
import { identityStamp, requireIdentity } from './identity'

export interface PendingDocumentApproval { operationId: string; objectId: string; submissionId: string; taskId: string | null; kind: 'DECISION' | 'APPLICATION' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EngineeringScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_document_approval_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingDocumentApproval(scope: EngineeringScope): PendingDocumentApproval | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingDocumentApproval
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !uuid.test(value.submissionId)
    || !['DECISION', 'APPLICATION'].includes(value.kind) || (value.kind === 'DECISION' ? !uuid.test(value.taskId ?? '') : value.taskId !== null)) throw new Error('批准原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, submissionId: value.submissionId, taskId: value.taskId, kind: value.kind }
}
async function exclusive<T>(scope: EngineeringScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = documentResponse(await documentApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function finish(scope: EngineeringScope, original: PendingDocumentApproval, state: string, check: () => void) {
  check()
  if (JSON.stringify(pendingDocumentApproval(scope)) !== JSON.stringify(original)) throw new Error('批准原操作记录已变化。')
  if (state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return state
}
function acceptApplication(scope: EngineeringScope, original: PendingDocumentApproval, value: ApplicationOperation, check: () => void) {
  check(); const r = value?.result, receipt = r?.receipt
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.objectId !== original.objectId
      || !receipt || receipt.submissionId !== original.submissionId || receipt.subject?.subjectId !== original.objectId
      || receipt.producerOwner !== 'Main.Plm.ControlledDocument.v1' || !uuid.test(receipt.receiptId) || !uuid.test(receipt.decisionId)
      || receipt.application !== 'APPLIED' || !['DOCUMENT_APPROVED', 'DOCUMENT_REJECTED'].includes(receipt.outcomeCode)
      || receipt.businessResultRef?.owner !== 'Main.Plm.DocumentApproval.v1' || receipt.businessResultRef.version !== '1' || !uuid.test(receipt.businessResultRef.id)
      : r != null)) throw new Error('返回结果与原批准应用不一致，请保留记录。')
  return finish(scope, original, value.state, check)
}
function acceptDecision(scope: EngineeringScope, original: PendingDocumentApproval, value: NativeReviewResolution, check: () => void) {
  check(); const r = value?.result
  if (value?.commandId !== original.operationId || !['COMPLETED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMPLETED' ? !r || r.commandId !== original.operationId || r.state !== 'COMPLETED'
      || r.result?.task?.taskId !== original.taskId || !uuid.test(r.result.voteId) || !uuid.test(r.result.task.instanceId)
      || r.result.decisionId !== null && !uuid.test(r.result.decisionId)
      || r.queryRef !== `/api/oa/tasks/decision-commands/${original.operationId}` : r != null)) throw new Error('返回结果与原专业评审不一致，请保留记录。')
  return finish(scope, original, value.state, check)
}
function save(scope: EngineeringScope, original: PendingDocumentApproval) {
  if (!uuid.test(original.objectId) || !uuid.test(original.submissionId) || original.kind === 'DECISION' && !uuid.test(original.taskId ?? '')) throw new Error('缺少准确评审对象。')
  if (pendingDocumentApproval(scope)) throw new Error('请先恢复原批准操作。')
  localStorage.setItem(key(scope), JSON.stringify(original))
  if (pendingDocumentApproval(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
}
export function writeDocumentDecision(scope: EngineeringScope, objectId: string, submissionId: string, taskId: string,
  guard: () => void, write: (id: string) => Promise<NativeReviewResult>) {
  return exclusive(scope, guard, async check => {
    const original: PendingDocumentApproval = { operationId: crypto.randomUUID(), objectId, submissionId, taskId, kind: 'DECISION' }
    save(scope, original); check(); const result = await write(original.operationId)
    return acceptDecision(scope, original, { commandId: original.operationId, state: 'COMPLETED', result }, check)
  })
}
export function writeDocumentApplication(scope: EngineeringScope, objectId: string, submissionId: string,
  guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<ApplicationResult>>) {
  return exclusive(scope, guard, async check => {
    const original: PendingDocumentApproval = { operationId: crypto.randomUUID(), objectId, submissionId, taskId: null, kind: 'APPLICATION' }
    save(scope, original); check(); const result = documentResponse(await write(original.operationId))
    return acceptApplication(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveDocumentApproval(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingDocumentApproval(scope); if (!original) throw new Error('没有待确认的批准操作。')
    return original.kind === 'APPLICATION'
      ? acceptApplication(scope, original, documentResponse(await documentReviewApi.applicationOperation(original.operationId, close)), check)
      : acceptDecision(scope, original, await documentReviewApi.decisionOperation(original.operationId, original.taskId!, close), check)
  })
}
