import { toolDispositionApi as api, type DispositionKind, type DispositionOperation, type DispositionResult } from '@/api/wms/toolDisposition'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingDisposition { operationId: string; objectId: string; kind: DispositionKind }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const owners: Record<DispositionKind, string> = { REQUEST: 'Main.Wms.ToolDisposition.v1', BLOCK: 'Main.Wms.ToolDisposition.v1' }
function key(scope: EngineeringScope) { if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前身份。'); return `cp6_tool_disposition_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}` }
export function dispositionResponse<T>(value: EngineeringEnvelope<T>): T { if (value?.code !== 0 || value.data == null) throw new Error('无法确认处置原结果。'); return value.data }
export function pendingDisposition(scope: EngineeringScope): PendingDisposition | null {
  const raw = localStorage.getItem(key(scope)); if (raw === null) return null
  const v = JSON.parse(raw) as PendingDisposition
  if (!v || !uuid.test(v.operationId) || !uuid.test(v.objectId) || !Object.hasOwn(owners, v.kind)) throw new Error('原处置操作记录异常，请保留记录。')
  return { operationId: v.operationId, objectId: v.objectId, kind: v.kind }
}
async function exclusive<T>(scope: EngineeringScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = dispositionResponse(await api.scope()); check(); if (key(current) !== channel) throw new Error('身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringScope, original: PendingDisposition, value: DispositionOperation, check: () => void) {
  check(); const r = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.objectId !== original.objectId || r.kind !== original.kind || r.isUsePermit !== false || r.isDiscarded !== false
      || r.fact?.owner !== owners[original.kind] || !uuid.test(r.fact.id) || r.fact.version !== '1' || !/^[0-9a-f]{64}$/.test(r.fact.digest) : r != null)) throw new Error('返回结果与原处置操作不一致，请保留记录。')
  if (JSON.stringify(pendingDisposition(scope)) !== JSON.stringify(original)) throw new Error('原处置操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope)); return value
}
export function saveDisposition(scope: EngineeringScope, objectId: string, kind: DispositionKind, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<DispositionResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId) || !Object.hasOwn(owners, kind)) throw new Error('请先选择处置对象。')
    if (pendingDisposition(scope)) throw new Error('请先查询或结束原处置操作。')
    const original: PendingDisposition = { operationId: crypto.randomUUID(), objectId, kind }
    localStorage.setItem(key(scope), JSON.stringify(original)); if (pendingDisposition(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = dispositionResponse(await write(original.operationId)); return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveDisposition(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => { const original = pendingDisposition(scope); if (!original) throw new Error('没有待确认的处置操作。'); return accept(scope, original, dispositionResponse(await api.operation(original.operationId, close)), check) })
}
