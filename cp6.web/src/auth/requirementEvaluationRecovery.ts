import { evaluationApi, type EvaluationOperation, type EvaluationResult, type EvaluationAction, type EvaluationScope } from '@/api/plm/requirementEvaluation'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingEvaluation { operationId: string; objectId: string; action: EvaluationAction }
const actions = ['POLICY', 'SUBMIT', 'APPROVE', 'RETURN', 'WITHDRAW']
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EvaluationScope) {
  if (!uuid.test(scope.environmentId) || !uuid.test(scope.tenantId) || !uuid.test(scope.actorId) || !['REAL', 'DEMO'].includes(scope.evidenceMode)) throw new Error('无法确认当前用户和租户。')
  return `cp6_evaluation_operation_v1:${scope.environmentId.toLowerCase()}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}:${scope.evidenceMode}`
}
export function evaluationResponse<T>(response: EngineeringEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认需求专业评价原结果。')
  return response.data
}
export function pendingEvaluation(scope: EvaluationScope): PendingEvaluation | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingEvaluation
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !actions.includes(value.action)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, action: value.action }
}
async function exclusive<T>(scope: EvaluationScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = evaluationResponse(await evaluationApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function exact(value: any, id: string, owner: string) { return value?.owner === owner && value.id === id && /^[1-9][0-9]*$/.test(value.version) && /^[0-9a-f]{64}$/.test(value.digest) }
function accept(scope: EvaluationScope, original: PendingEvaluation, value: EvaluationOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId
      || receipt.objectId !== original.objectId || receipt.action !== original.action || receipt.environmentId !== scope.environmentId || receipt.evidenceMode !== scope.evidenceMode
      || !exact(receipt.requirement, original.objectId, 'Main.Plm.EngineeringRequirement.v1') || !exact(receipt.control, original.objectId, 'Main.Plm.RequirementEvaluationControl.v1') : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingEvaluation(scope)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId || pending.action !== original.action) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveEvaluation(scope: EvaluationScope, objectId: string, action: EvaluationAction, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<EvaluationResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId) || !actions.includes(action)) throw new Error('请先选择当前需求专业评价。')
    if (pendingEvaluation(scope)) throw new Error('请先查询或结束原操作。')
    const original: PendingEvaluation = { operationId: crypto.randomUUID(), objectId, action }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingEvaluation(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = evaluationResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveEvaluation(scope: EvaluationScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingEvaluation(scope); if (!original) throw new Error('没有待确认的原操作。')
    return accept(scope, original, evaluationResponse(await evaluationApi.operation(original.operationId, close)), check)
  })
}
