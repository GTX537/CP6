import { toolWithdrawalApi as api, type ToolWithdrawalScope, type WithdrawalOperation, type WithdrawalResult, type BusinessGrantOperation, type BusinessGrantResult } from '@/api/wms/toolWithdrawal'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { toolApprovalResponse as response } from './toolApprovalRecovery'
import { identityStamp, requireIdentity } from './identity'

export interface PendingToolWithdrawal { operationId: string; objectId: string; submissionId: string; kind: 'WITHDRAWAL' | 'BUSINESS_GRANT' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: ToolWithdrawalScope) {
  if (![scope.environmentId, scope.tenantId, scope.actorId].every(x => uuid.test(x))) throw new Error('无法确认当前环境、用户和租户。')
  return `cp6_tool_withdrawal_operation_v1:${scope.environmentId.toLowerCase()}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingToolWithdrawal(scope: ToolWithdrawalScope): PendingToolWithdrawal | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingToolWithdrawal
  if (!value || ![value.operationId, value.objectId, value.submissionId].every(x => uuid.test(x)) || !['WITHDRAWAL', 'BUSINESS_GRANT'].includes(value.kind)) throw new Error('版模原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, submissionId: value.submissionId, kind: value.kind }
}
async function exclusive<T>(scope: ToolWithdrawalScope, guard: () => void, action: (check: () => void) => Promise<T>) {
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
function accept(scope: ToolWithdrawalScope, original: PendingToolWithdrawal, value: WithdrawalOperation | BusinessGrantOperation, check: () => void) {
  check()
  if (value.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)) throw new Error('返回结果与原版模操作不一致。')
  if (value.state === 'COMMITTED') {
    const r = value.result
    if (!r || r.operationId !== original.operationId) throw new Error('缺少原版模操作结果。')
    if (original.kind === 'WITHDRAWAL') {
      const w = r as WithdrawalResult
      if (w.toolId !== original.objectId || w.state !== 'WITHDRAWN' || w.fact?.submissionId !== original.submissionId
        || w.fact.owner !== 'Main.Wms.ToolDisposition.v1' || w.fact.scope.environmentId !== scope.environmentId || w.fact.scope.tenantId !== scope.tenantId
        || w.fact.actor.actualHandlerId !== scope.actorId || !uuid.test(w.transportCommandId)) throw new Error('撤回结果与原版模送审不一致。')
    } else {
      const g = (r as BusinessGrantResult).grant
      if (g?.toolId !== original.objectId || g.submissionId !== original.submissionId || g.grantorId !== scope.actorId) throw new Error('授权结果与本人原版模送审不一致。')
    }
  } else if (value.result != null) throw new Error('未提交操作返回了业务结果。')
  check()
  if (JSON.stringify(pendingToolWithdrawal(scope)) !== JSON.stringify(original)) throw new Error('版模原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value.state
}
export function writeToolOwnerAction(scope: ToolWithdrawalScope, objectId: string, submissionId: string, kind: PendingToolWithdrawal['kind'],
  guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<WithdrawalResult | BusinessGrantResult>>) {
  return exclusive(scope, guard, async check => {
    if (![objectId, submissionId].every(x => uuid.test(x))) throw new Error('缺少准确版模送审对象。')
    if (pendingToolWithdrawal(scope)) throw new Error('请先恢复原版模操作。')
    const original: PendingToolWithdrawal = { operationId: crypto.randomUUID(), objectId, submissionId, kind }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingToolWithdrawal(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = response(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result } as WithdrawalOperation | BusinessGrantOperation, check)
  })
}
export function resolveToolWithdrawal(scope: ToolWithdrawalScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingToolWithdrawal(scope); if (!original) throw new Error('没有待确认的版模操作。')
    return accept(scope, original, response<WithdrawalOperation | BusinessGrantOperation>(original.kind === 'WITHDRAWAL'
      ? await api.withdrawalOperation(original.operationId, close) : await api.businessGrantOperation(original.operationId, close)), check)
  })
}
