import { toolActualUsageApi as api, type AnyActualCommandKind, type ActualOperation, type ActualResult } from '@/api/wms/toolActualUsage'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingActualUsage { operationId: string; objectId: string; kind: AnyActualCommandKind }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const owners: Record<AnyActualCommandKind, string> = { M_OCCURRENCE: 'Main.Wms.ToolMaintenanceOccurrence.v1', M_OBSERVATION: 'Main.Wms.ToolActualObservation.v1', M_POLICY: 'Main.Wms.ToolMaintenanceRecognitionPolicy.v1', M_RECOGNITION: 'Main.Wms.ToolActualRecognition.v1', M_APPLICATION: 'Main.Wms.ToolObservationApplication.v1', EVIDENCE: 'Main.Wms.ManualToolUseEvidence.v1', OCCURRENCE: 'Main.Wms.ToolUseOccurrence.v1', OBSERVATION: 'Main.Wms.ToolActualObservation.v1', POLICY: 'Main.Wms.ToolActualRecognitionPolicy.v1', RECOGNITION: 'Main.Wms.ToolActualRecognition.v1', APPLICATION: 'Main.Wms.ToolObservationApplication.v1' }
function key(scope: EngineeringScope) { if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前身份。'); return `cp6_tool_actual_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}` }
export function actualResponse<T>(value: EngineeringEnvelope<T>): T { if (value?.code !== 0 || value.data == null) throw new Error('无法确认实际使用原结果。'); return value.data }
export function pendingActualUsage(scope: EngineeringScope): PendingActualUsage | null {
  const raw = localStorage.getItem(key(scope)); if (raw === null) return null
  const v = JSON.parse(raw) as PendingActualUsage
  if (!v || !uuid.test(v.operationId) || !uuid.test(v.objectId) || !Object.hasOwn(owners, v.kind)) throw new Error('原实际使用操作记录异常，请保留记录。')
  return { operationId: v.operationId, objectId: v.objectId, kind: v.kind }
}
async function exclusive<T>(scope: EngineeringScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = actualResponse(await api.scope()); check(); if (key(current) !== channel) throw new Error('身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringScope, original: PendingActualUsage, value: ActualOperation, check: () => void) {
  check(); const r = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.objectId !== original.objectId || r.kind !== original.kind || r.isUsePermit !== false
      || r.fact?.owner !== owners[original.kind] || !uuid.test(r.fact.id) || !/^[1-9][0-9]{0,18}$/.test(r.fact.version) || !/^[0-9a-f]{64}$/.test(r.fact.digest) : r != null)) throw new Error('返回结果与原实际使用操作不一致，请保留记录。')
  if (JSON.stringify(pendingActualUsage(scope)) !== JSON.stringify(original)) throw new Error('原实际使用操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope)); return value
}
export function saveActualUsage(scope: EngineeringScope, objectId: string, kind: AnyActualCommandKind, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<ActualResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId) || !Object.hasOwn(owners, kind)) throw new Error('请先选择实际使用对象。')
    if (pendingActualUsage(scope)) throw new Error('请先查询或结束原实际使用操作。')
    const original: PendingActualUsage = { operationId: crypto.randomUUID(), objectId, kind }
    localStorage.setItem(key(scope), JSON.stringify(original)); if (pendingActualUsage(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = actualResponse(await write(original.operationId)); return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveActualUsage(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => { const original = pendingActualUsage(scope); if (!original) throw new Error('没有待确认的实际使用操作。'); return accept(scope, original, actualResponse(await api.operation(original.operationId, close)), check) })
}
