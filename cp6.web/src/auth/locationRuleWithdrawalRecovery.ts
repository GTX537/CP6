import { locationRuleWithdrawalApi as api, type LocationRuleReviewScope, type WithdrawalOperation, type WithdrawalResult, type BusinessGrantOperation, type BusinessGrantResult } from '@/api/wms/locationRuleWithdrawal'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { locationRuleResponse as response } from './locationRuleReviewResponse'
import { identityStamp, requireIdentity } from './identity'

export interface PendingLocationRuleWithdrawal { operationId: string; objectId: string; submissionId: string; kind: 'WITHDRAWAL' | 'BUSINESS_GRANT' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: LocationRuleReviewScope) {
  if (![scope.environmentId, scope.tenantId, scope.actorId].every(x => uuid.test(x)) || !['REAL', 'DEMO'].includes(scope.evidenceMode)) throw new Error('无法确认当前环境、用户和租户。')
  return `cp6_location_rule_withdrawal_operation_v1:${scope.environmentId.toLowerCase()}:${scope.evidenceMode}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingLocationRuleWithdrawal(scope: LocationRuleReviewScope): PendingLocationRuleWithdrawal | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingLocationRuleWithdrawal
  if (!value || ![value.operationId, value.objectId, value.submissionId].every(x => uuid.test(x)) || !['WITHDRAWAL', 'BUSINESS_GRANT'].includes(value.kind)) throw new Error('库位规则原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, submissionId: value.submissionId, kind: value.kind }
}
async function exclusive<T>(scope: LocationRuleReviewScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = response(await api.scope()); check()
    if (key(current) !== channel) throw new Error('当前环境或身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: LocationRuleReviewScope, original: PendingLocationRuleWithdrawal, value: WithdrawalOperation | BusinessGrantOperation, check: () => void) {
  check()
  if (value.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)) throw new Error('返回结果与原库位规则操作不一致。')
  if (value.state === 'COMMITTED') {
    const r = value.result
    if (!r || r.operationId !== original.operationId) throw new Error('缺少原库位规则操作结果。')
    if (original.kind === 'WITHDRAWAL') {
      const w = r as WithdrawalResult
      if (w.locationRuleId !== original.objectId || !['WITHDRAWN', 'CONFLICT'].includes(w.state)) throw new Error('撤回结果与原库位规则操作不一致。')
      if (w.state === 'WITHDRAWN') {
        if (w.fact?.submissionId !== original.submissionId || w.fact.owner !== 'Main.Wms.LocationRuleProposal.v1'
          || w.fact.scope.environmentId !== scope.environmentId || w.fact.scope.tenantId !== scope.tenantId
          || w.fact.actor.actualHandlerId !== scope.actorId || !uuid.test(w.transportCommandId ?? '') || w.appliedReceipt !== null) throw new Error('撤回结果与原库位规则送审不一致。')
      } else if (w.appliedReceipt?.submissionId !== original.submissionId || w.appliedReceipt.subject.subjectId !== original.objectId
        || w.appliedReceipt.producerOwner !== 'Main.Wms.LocationRuleProposal.v1' || w.appliedReceipt.application !== 'APPLIED'
        || w.fact !== null || w.transportCommandId !== null) throw new Error('审批已应用结果与原库位规则送审不一致。')
    } else {
      const g = (r as BusinessGrantResult).grant
      if (g?.locationRuleId !== original.objectId || g.submissionId !== original.submissionId || g.grantorId !== scope.actorId) throw new Error('授权结果与本人原库位规则送审不一致。')
    }
  } else if (value.result != null) throw new Error('未提交操作返回了业务结果。')
  check()
  if (JSON.stringify(pendingLocationRuleWithdrawal(scope)) !== JSON.stringify(original)) throw new Error('库位规则原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value.state
}
export function writeLocationRuleOwnerAction(scope: LocationRuleReviewScope, objectId: string, submissionId: string, kind: PendingLocationRuleWithdrawal['kind'],
  guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<WithdrawalResult | BusinessGrantResult>>) {
  return exclusive(scope, guard, async check => {
    if (![objectId, submissionId].every(x => uuid.test(x))) throw new Error('缺少准确库位规则送审对象。')
    if (pendingLocationRuleWithdrawal(scope)) throw new Error('请先恢复原库位规则操作。')
    const original: PendingLocationRuleWithdrawal = { operationId: crypto.randomUUID(), objectId, submissionId, kind }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingLocationRuleWithdrawal(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = response(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result } as WithdrawalOperation | BusinessGrantOperation, check)
  })
}
export function resolveLocationRuleWithdrawal(scope: LocationRuleReviewScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingLocationRuleWithdrawal(scope); if (!original) throw new Error('没有待确认的库位规则操作。')
    return accept(scope, original, response<WithdrawalOperation | BusinessGrantOperation>(original.kind === 'WITHDRAWAL'
      ? await api.withdrawalOperation(original.operationId, close) : await api.businessGrantOperation(original.operationId, close)), check)
  })
}
