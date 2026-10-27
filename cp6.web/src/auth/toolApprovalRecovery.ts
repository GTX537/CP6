import { toolApprovalApi as api, type ToolApprovalKind, type ToolApprovalOperation, type ToolApprovalResult } from '@/api/wms/toolApproval'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingToolApproval { operationId: string; objectId: string; kind: ToolApprovalKind }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const owners: Record<ToolApprovalKind, string> = { POLICY: 'Main.Wms.ToolDispositionPolicy.v1', SUBMIT: 'Main.Wms.ToolDispositionSubmission.v1' }
function key(scope: EngineeringScope) { if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前身份。'); return `cp6_tool_approval_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}` }
export function toolApprovalResponse<T>(value: EngineeringEnvelope<T>): T { if (value?.code !== 0 || value.data == null) throw new Error('无法确认审批提交原结果。'); return value.data }
export function pendingToolApproval(scope: EngineeringScope): PendingToolApproval | null {
  const raw = localStorage.getItem(key(scope)); if (raw === null) return null
  const v = JSON.parse(raw) as PendingToolApproval
  if (!v || !uuid.test(v.operationId) || !uuid.test(v.objectId) || !Object.hasOwn(owners, v.kind)) throw new Error('原审批提交操作记录异常，请保留记录。')
  return { operationId: v.operationId, objectId: v.objectId, kind: v.kind }
}
async function exclusive<T>(scope: EngineeringScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = toolApprovalResponse(await api.scope()); check(); if (key(current) !== channel) throw new Error('身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringScope, original: PendingToolApproval, value: ToolApprovalOperation, check: () => void) {
  check(); const r = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.objectId !== original.objectId || r.kind !== original.kind || r.isUsePermit !== false || r.isDiscarded !== false
      || (original.kind === 'SUBMIT' && (!r.submission || r.submission.submissionId !== r.fact?.id || r.submission.toolId !== original.objectId || r.submission.state !== 'DISPATCH_PENDING' || r.submission.isApproved !== false || r.submission.isDiscarded !== false)) || (original.kind === 'POLICY' && r.submission != null)
      || r.fact?.owner !== owners[original.kind] || !uuid.test(r.fact.id) || r.fact.version !== '1' || !/^[0-9a-f]{64}$/.test(r.fact.digest) : r != null)) throw new Error('返回结果与原审批提交操作不一致，请保留记录。')
  if (JSON.stringify(pendingToolApproval(scope)) !== JSON.stringify(original)) throw new Error('原审批提交操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope)); return value
}
export function saveToolApproval(scope: EngineeringScope, objectId: string, kind: ToolApprovalKind, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<ToolApprovalResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId) || !Object.hasOwn(owners, kind)) throw new Error('请先选择审批提交对象。')
    if (pendingToolApproval(scope)) throw new Error('请先查询或结束原审批提交操作。')
    const original: PendingToolApproval = { operationId: crypto.randomUUID(), objectId, kind }
    localStorage.setItem(key(scope), JSON.stringify(original)); if (pendingToolApproval(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = toolApprovalResponse(await write(original.operationId)); return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveToolApproval(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => { const original = pendingToolApproval(scope); if (!original) throw new Error('没有待确认的审批提交操作。'); return accept(scope, original, toolApprovalResponse(await api.operation(original.operationId, close)), check) })
}
