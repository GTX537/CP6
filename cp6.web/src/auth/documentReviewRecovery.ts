import { documentReviewApi, type ReviewOperation, type ReviewResult } from '@/api/plm/documentReview'
import { documentApi } from '@/api/plm/controlledDocument'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { documentResponse } from './documentRecovery'
import { identityStamp, requireIdentity } from './identity'

export interface PendingDocumentReview { operationId: string; objectId: string; kind: 'POLICY' | 'SUBMIT' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EngineeringScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_document_review_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingDocumentReview(scope: EngineeringScope): PendingDocumentReview | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingDocumentReview
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !['POLICY', 'SUBMIT'].includes(value.kind)) throw new Error('评审原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, kind: value.kind }
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
function accept(scope: EngineeringScope, original: PendingDocumentReview, value: ReviewOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId || receipt.documentId !== original.objectId
      || receipt.kind !== original.kind || !uuid.test(receipt.reference?.id) || receipt.reference.version !== '1'
      || !/^[0-9a-f]{64}$/.test(receipt.reference.digest)
      || (receipt.kind === 'POLICY' ? receipt.reference.owner !== 'Main.Plm.DocumentReviewPolicy.v1' || receipt.submission != null
        : receipt.reference.owner !== 'Main.Plm.DocumentReviewSubmission.v1' || receipt.submission?.submissionId !== receipt.reference.id
          || receipt.submission?.document.id !== original.objectId || receipt.submission?.isApproved !== false)
      : receipt != null)) throw new Error('返回结果与原评审操作不一致，请保留记录。')
  const pending = pendingDocumentReview(scope)
  if (JSON.stringify(pending) !== JSON.stringify(original)) throw new Error('原评审操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function writeDocumentReview(scope: EngineeringScope, objectId: string, kind: 'POLICY' | 'SUBMIT', guard: () => void,
  write: (id: string) => Promise<EngineeringEnvelope<ReviewResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId)) throw new Error('请先选择当前文档。')
    if (pendingDocumentReview(scope)) throw new Error('请先查询或结束原评审操作。')
    const original: PendingDocumentReview = { operationId: crypto.randomUUID(), objectId, kind }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingDocumentReview(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = documentResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveDocumentReview(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingDocumentReview(scope); if (!original) throw new Error('没有待确认的评审操作。')
    return accept(scope, original, documentResponse(await documentReviewApi.operation(original.operationId, close)), check)
  })
}
