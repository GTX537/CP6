import { toolApplicationApi as api, type ToolApplicationOperation, type ToolApplicationReceipt, type ToolApplicationResult } from '@/api/wms/toolApplication'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingToolApplication { operationId: string; objectId: string; receiptStreamId: string; decisionId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EngineeringScope) { if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前身份。'); return `cp6_tool_application_v2:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}` }
export function applicationResponse<T>(value: EngineeringEnvelope<T>): T { if (value?.code !== 0 || value.data == null) throw new Error('无法确认应用原结果。'); return value.data }
export function validApplicationReceipt(r: ToolApplicationReceipt, tenant: string, stream: string, decision: string) {
  return !!r && r.schema === 'oa.app/2.0' && r.scope?.tenantId === tenant && !!r.scope.environmentId && r.receiptStreamId === stream && r.decisionId === decision
    && [r.receiptId, r.receiptEventId, r.submissionId, r.instanceId, r.actionActorId].every(x => uuid.test(x))
    && /^[1-9][0-9]*$/.test(r.receiptRevision) && BigInt(r.receiptRevision) <= 9223372036854775807n
    && (r.receiptRevision === '1' ? r.previousReceiptId === null : typeof r.previousReceiptId === 'string' && uuid.test(r.previousReceiptId))
    && r.producerOwner === 'Main.Wms.ToolDisposition.v1' && r.action === 'WMS_TOOL_DISCARD' && ['BLOCKED', 'STALE', 'CONFLICT'].includes(r.application)
    && r.appliedAt === null && r.businessResultRef === null && r.businessResultVersion === null && typeof r.outcomeCode === 'string' && r.outcomeCode.length > 0
    && Number.isFinite(Date.parse(r.recordedAt)) && r.payloadDigest?.algorithm === 'sha256' && r.payloadDigest.canonicalizationVersion === 'oa.app.canonical/2.0' && /^[0-9a-f]{64}$/.test(r.payloadDigest.value)
}
export function pendingToolApplication(scope: EngineeringScope): PendingToolApplication | null {
  const raw = localStorage.getItem(key(scope)); if (raw === null) return null
  const v = JSON.parse(raw) as PendingToolApplication
  if (!v || ![v.operationId, v.objectId, v.receiptStreamId, v.decisionId].every(x => uuid.test(x))) throw new Error('原应用操作记录异常，请保留记录。')
  return { operationId: v.operationId, objectId: v.objectId, receiptStreamId: v.receiptStreamId, decisionId: v.decisionId }
}
async function exclusive<T>(scope: EngineeringScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = applicationResponse(await api.scope()); check(); if (key(current) !== channel) throw new Error('身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringScope, original: PendingToolApplication, value: ToolApplicationOperation, check: () => void) {
  check(); const r = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.objectId !== original.objectId || !validApplicationReceipt(r.receipt, scope.tenantId, original.receiptStreamId, original.decisionId) : r != null)) throw new Error('返回结果与原应用操作不一致，请保留记录。')
  if (JSON.stringify(pendingToolApplication(scope)) !== JSON.stringify(original)) throw new Error('原应用操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope)); return value
}
export function saveToolApplication(scope: EngineeringScope, objectId: string, receiptStreamId: string, decisionId: string, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<ToolApplicationResult>>) {
  return exclusive(scope, guard, async check => {
    if (![objectId, receiptStreamId, decisionId].every(x => uuid.test(x))) throw new Error('请先读取准确审批决定。')
    if (pendingToolApplication(scope)) throw new Error('请先查询或结束原应用操作。')
    const original = { operationId: crypto.randomUUID(), objectId, receiptStreamId, decisionId }
    localStorage.setItem(key(scope), JSON.stringify(original)); if (pendingToolApplication(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = applicationResponse(await write(original.operationId)); return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveToolApplication(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => { const original = pendingToolApplication(scope); if (!original) throw new Error('没有待确认的应用操作。'); return accept(scope, original, applicationResponse(await api.operation(original.operationId, close)), check) })
}
