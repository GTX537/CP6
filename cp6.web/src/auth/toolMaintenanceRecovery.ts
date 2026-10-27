import { toolMaintenanceApi as api, type MaintenanceOperation, type MaintenanceResult, type MaintenanceKind } from '@/api/wms/toolMaintenance'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingToolMaintenance { operationId: string; objectId: string; kind: MaintenanceKind; maintenanceId: string | null }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const owners: Record<MaintenanceKind, string> = { POLICY: 'Main.Wms.ToolMaintenancePolicy.v1', REPORT: 'Main.Wms.ToolMaintenanceReport.v1', ASSESSMENT: 'Main.Master.ToolMaintenanceRelease.v1', START: 'Main.Wms.ToolMaintenance.v1', COMPLETE: 'Main.Wms.ToolMaintenance.v1', RESET: 'Main.Wms.ToolMaintenance.v1' }
function key(scope: EngineeringScope) { if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前身份。'); return `cp6_tool_maintenance_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}` }
export function maintenanceResponse<T>(value: EngineeringEnvelope<T>): T { if (value?.code !== 0 || value.data == null) throw new Error('无法确认维护原结果。'); return value.data }
export function pendingToolMaintenance(scope: EngineeringScope): PendingToolMaintenance | null {
  const raw = localStorage.getItem(key(scope)); if (raw === null) return null
  const v = JSON.parse(raw) as PendingToolMaintenance
  if (!v || !uuid.test(v.operationId) || !uuid.test(v.objectId) || !Object.hasOwn(owners, v.kind) || (['START', 'POLICY'].includes(v.kind) ? v.maintenanceId !== null : !uuid.test(v.maintenanceId ?? ''))) throw new Error('原维护操作记录异常，请保留记录。')
  return { operationId: v.operationId, objectId: v.objectId, kind: v.kind, maintenanceId: v.maintenanceId }
}
async function exclusive<T>(scope: EngineeringScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = maintenanceResponse(await api.scope()); check(); if (key(current) !== channel) throw new Error('身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringScope, original: PendingToolMaintenance, value: MaintenanceOperation, check: () => void) {
  check(); const r = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.objectId !== original.objectId || r.kind !== original.kind || r.notExecutionPermit !== true
      || r.fact?.owner !== owners[original.kind] || !uuid.test(r.fact.id) || r.fact.version !== '1' || !/^[0-9a-f]{64}$/.test(r.fact.digest)
      || (original.kind === 'START' ? !uuid.test(r.maintenanceId ?? '') : r.maintenanceId !== original.maintenanceId) : r != null)) throw new Error('返回结果与原维护操作不一致，请保留记录。')
  if (JSON.stringify(pendingToolMaintenance(scope)) !== JSON.stringify(original)) throw new Error('原维护操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope)); return value
}
export function saveToolMaintenance(scope: EngineeringScope, objectId: string, kind: MaintenanceKind, maintenanceId: string | null, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<MaintenanceResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId) || (['START', 'POLICY'].includes(kind) ? maintenanceId !== null : !uuid.test(maintenanceId ?? ''))) throw new Error('请先选择维护对象。')
    if (pendingToolMaintenance(scope)) throw new Error('请先查询或结束原维护操作。')
    const original: PendingToolMaintenance = { operationId: crypto.randomUUID(), objectId, kind, maintenanceId }
    localStorage.setItem(key(scope), JSON.stringify(original)); if (pendingToolMaintenance(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = maintenanceResponse(await write(original.operationId)); return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveToolMaintenance(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => { const original = pendingToolMaintenance(scope); if (!original) throw new Error('没有待确认的维护操作。'); return accept(scope, original, maintenanceResponse(await api.operation(original.operationId, close)), check) })
}
