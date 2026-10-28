import type { EngineeringIterationScope } from '@/api/plm/engineeringIteration'
import { engineeringIterationApi } from '@/api/plm/engineeringIteration'
import { iterationReviewActions, type ActionKind, type ActionOperation, type ActionResult, type ApplicationResult, type IssueResult, type WithdrawalResult } from '@/api/plm/iterationReviewActions'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import type { NativeReviewResult, NativeReviewResolution } from '@/api/plm/requirementSetReview'
import { requirementSetResponse as response } from './requirementSetRecovery'
import { identityStamp, requireIdentity } from './identity'
export interface PendingIterationAction { operationId: string; objectId: string; submissionId: string; roundId: string; kind: ActionKind | 'DECISION'; targetId: string | null }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EngineeringIterationScope) {
  if (![scope.environmentId, scope.tenantId, scope.actorId].every(v => uuid.test(v)) || !['REAL', 'DEMO'].includes(scope.evidenceMode)) throw new Error('无法确认当前用户和租户。')
  return `cp6_iteration_review_action_v1:${scope.environmentId.toLowerCase()}:${scope.evidenceMode}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingIterationAction(scope: EngineeringIterationScope): PendingIterationAction | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const v = JSON.parse(text) as PendingIterationAction
  if (!v || ![v.operationId, v.objectId, v.submissionId, v.roundId].every(x => uuid.test(x)) || !['APPLICATION', 'ISSUE', 'WITHDRAWAL', 'DECISION'].includes(v.kind)
    || (v.kind === 'DECISION' || v.kind === 'ISSUE' ? !uuid.test(v.targetId ?? '') : v.targetId !== null)) throw new Error('评审处理恢复记录异常，请保留记录。')
  return { operationId: v.operationId, objectId: v.objectId, submissionId: v.submissionId, roundId: v.roundId, kind: v.kind, targetId: v.targetId }
}
async function exclusive<T>(scope: EngineeringIterationScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = response(await engineeringIterationApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function finish(scope: EngineeringIterationScope, p: PendingIterationAction, state: string, check: () => void) {
  check()
  if (JSON.stringify(pendingIterationAction(scope)) !== JSON.stringify(p)) throw new Error('原操作记录已变化。')
  if (state !== 'UNKNOWN') localStorage.removeItem(key(scope))
}
function accept(scope: EngineeringIterationScope, p: PendingIterationAction, op: ActionOperation, check: () => void) {
  check(); const r = op?.result
  let valid = op?.operationId === p.operationId && ['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(op.state)
  if (op?.state === 'COMMITTED') {
    valid = valid && !!r && r.operationId === p.operationId
    if (p.kind === 'APPLICATION') {
      const v = r as ApplicationResult
      valid = valid && v.objectId === p.objectId && v.receipt?.submissionId === p.submissionId && v.receipt?.subject?.subjectId === p.roundId
        && v.receipt?.producerOwner === 'Main.Plm.EngineeringIteration.v1' && v.receipt?.action === 'PLM_ITER_REVIEW_DECISION'
        && ['APPLIED', 'BLOCKED', 'STALE', 'CONFLICT'].includes(v.receipt?.application) && uuid.test(v.receipt?.receiptId)
    } else if (p.kind === 'ISSUE') {
      const v = r as IssueResult
      valid = valid && v.iterationId === p.objectId && v.issue?.iterationId === p.objectId && v.issue?.submissionId === p.submissionId && v.issue?.id === p.targetId
        && typeof v.issue?.revision === 'string' && /^[1-9][0-9]*$/.test(v.issue.revision) && BigInt(v.issue.revision) <= 9223372036854775807n
    } else {
      const v = r as WithdrawalResult
      valid = valid && v.iterationId === p.objectId && (v.state === 'CONFLICT'
        ? v.fact === null && v.transportCommandId === null && v.appliedReceipt?.submissionId === p.submissionId && v.appliedReceipt?.application === 'APPLIED'
        : v.state === 'WITHDRAWN' && v.fact?.ownerCommandId === p.operationId && v.fact?.submissionId === p.submissionId && v.fact?.subject?.subjectId === p.roundId
          && v.fact?.owner === 'Main.Plm.EngineeringIteration.v1' && v.fact?.actor?.actualHandlerId === scope.actorId
          && v.fact?.scope?.tenantId === scope.tenantId && v.fact?.scope?.environmentId === scope.environmentId && uuid.test(v.transportCommandId ?? ''))
    }
  } else valid = valid && r == null
  if (!valid) throw new Error('返回结果与原评审处理操作不一致，请保留记录。')
  finish(scope, p, op.state, check)
  return op
}
function save(scope: EngineeringIterationScope, target: Omit<PendingIterationAction, 'operationId'>) {
  if (![target.objectId, target.submissionId, target.roundId].every(v => uuid.test(v))) throw new Error('请选择准确评审轮次。')
  if (pendingIterationAction(scope)) throw new Error('请先查询或结束原评审处理操作。')
  const p: PendingIterationAction = { operationId: crypto.randomUUID(), objectId: target.objectId, submissionId: target.submissionId, roundId: target.roundId, kind: target.kind, targetId: target.targetId }
  localStorage.setItem(key(scope), JSON.stringify(p)); if (pendingIterationAction(scope)?.operationId !== p.operationId) throw new Error('无法保存恢复记录。')
  return p
}
export function writeIterationAction(scope: EngineeringIterationScope, target: Omit<PendingIterationAction, 'operationId'> & { kind: ActionKind }, guard: () => void,
  write: (id: string) => Promise<EngineeringEnvelope<ActionResult>>) {
  return exclusive(scope, guard, async check => {
    const pending = save(scope, target)
    check(); const result = response(await write(pending.operationId))
    return accept(scope, pending, { operationId: pending.operationId, state: 'COMMITTED', result }, check)
  })
}
function acceptDecision(scope: EngineeringIterationScope, p: PendingIterationAction, value: NativeReviewResolution, check: () => void) {
  check(); const r = value?.result
  if (value?.commandId !== p.operationId || !['COMPLETED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMPLETED' ? !r || r.commandId !== p.operationId || r.state !== 'COMPLETED'
      || r.result?.task?.taskId !== p.targetId || !uuid.test(r.result.voteId) || !uuid.test(r.result.task.instanceId)
      || r.result.decisionId !== null && !uuid.test(r.result.decisionId)
      || r.queryRef !== `/api/oa/tasks/decision-commands/${p.operationId}` : r != null)) throw new Error('返回结果与原专业评审不一致，请保留记录。')
  finish(scope, p, value.state, check); return value
}
export function writeIterationDecision(scope: EngineeringIterationScope, target: Omit<PendingIterationAction, 'operationId'> & { kind: 'DECISION' }, guard: () => void,
  write: (id: string) => Promise<NativeReviewResult>) {
  return exclusive(scope, guard, async check => {
    const pending = save(scope, target); check(); const result = await write(pending.operationId)
    return acceptDecision(scope, pending, { commandId: pending.operationId, state: 'COMPLETED', result }, check)
  })
}
export function resolveIterationAction(scope: EngineeringIterationScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const p = pendingIterationAction(scope); if (!p) throw new Error('没有待确认的评审处理操作。')
    return p.kind === 'DECISION'
      ? acceptDecision(scope, p, await iterationReviewActions.decisionOperation(p.operationId, p.targetId!, close), check)
      : accept(scope, p, response(await iterationReviewActions.operation(p.kind, p.operationId, close)), check)
  })
}
