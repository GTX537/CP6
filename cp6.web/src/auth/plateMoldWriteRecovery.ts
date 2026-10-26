import { plateMoldWriterApi, type ToolWriterScope, type ToolWriteKind, type ToolWriteOperation } from '@/api/erp/plateMoldWriter'
import { identityStamp, requireIdentity } from './identity'
export interface PendingToolWrite { operationId: string; kind: ToolWriteKind }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const digest = /^[0-9a-f]{64}$/
const kinds = ['CREATE', 'REVISE', 'UPDATE', 'DELETE']
export function writerResponse<T>(r: { code: number; data?: T | null }): T {
  if (r?.code !== 0 || r.data == null) throw new Error('无法确认原操作结果。')
  return r.data
}
function channel(scope: ToolWriterScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前身份。')
  return `cp6_tool_write_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingToolWrite(scope: ToolWriterScope): PendingToolWrite | null {
  const raw = localStorage.getItem(channel(scope)); if (raw === null) return null
  const value = JSON.parse(raw) as PendingToolWrite
  if (!value || !uuid.test(value.operationId) || !kinds.includes(value.kind)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: value.operationId, kind: value.kind }
}
async function exclusive<T>(scope: ToolWriterScope, guard: () => void, body: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), key = channel(scope)
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = writerResponse(await plateMoldWriterApi.scope()); check()
    if (channel(current) !== key) throw new Error('当前身份已变化，请重新载入。')
    return body(check)
  })
}
function accept(scope: ToolWriterScope, original: PendingToolWrite, value: ToolWriteOperation, check: () => void) {
  check(); const r = value?.result, change = original.kind === 'UPDATE' || original.kind === 'DELETE'
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !r || r.operationId !== original.operationId || r.kind !== original.kind || !uuid.test(r.toolId)
      || !r.number || !Number.isSafeInteger(r.revision) || r.revision < 1 || !r.nativeRowVersion
      || r.source?.id !== r.toolId || r.source.owner !== 'Main.Erp.PlateMoldSource.v1' || !digest.test(r.source.digest) || !/^[1-9][0-9]*$/.test(r.source.version)
      || r.technicalControl?.id !== r.source.id
      || (r.technicalControl.knowledge === 'TECHNICAL_CONTROL_UNPROVEN'
        ? r.technicalControl.version !== r.source.version || r.technicalControl.digest !== r.source.digest || r.technicalControl.owner !== r.source.owner
        : r.technicalControl.knowledge !== 'TECHNICAL_SPECIFICATION_DRAFT' || r.technicalControl.owner !== 'Main.Plm.ToolSpecification.v1'
          || !/^[1-9][0-9]*$/.test(r.technicalControl.version) || !digest.test(r.technicalControl.digest))
      || !uuid.test(r.order?.orderId) || !uuid.test(r.order.lineId) || !r.order.number || !r.order.arrangeNumber || !Number.isSafeInteger(r.order.lineNumber) || r.order.lineNumber < 1 || !r.order.orderRowVersion || !r.order.lineRowVersion
      || r.peState !== 'NOT_EXECUTED' || r.orderSourceKind !== 'NATIVE_ORDER_LINK_ONLY'
      || (change ? r.commercialState !== 'REQUESTED' || r.intent?.operationId !== original.operationId || r.intent.toolId !== r.toolId || r.intent.state !== 'REQUESTED'
        || r.intent.kind !== (original.kind === 'UPDATE' ? 'CHANGE_REQUEST' : 'CANCEL_REQUEST') || !digest.test(r.intent.requestDigest)
        || r.intent.order.orderId !== r.order.orderId || r.intent.order.lineId !== r.order.lineId : r.commercialState !== 'OWNER_APPLIED' || r.intent != null) : r != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingToolWrite(scope)
  if (pending?.operationId !== original.operationId || pending.kind !== original.kind) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(channel(scope))
  return value
}
export function saveToolWrite(scope: ToolWriterScope, kind: ToolWriteKind, guard: () => void, write: (id: string) => Promise<{ code: number }>) {
  return exclusive(scope, guard, async check => {
    if (pendingToolWrite(scope)) throw new Error('请先查询或结束原操作。')
    const original = { operationId: crypto.randomUUID(), kind }
    localStorage.setItem(channel(scope), JSON.stringify(original))
    if (pendingToolWrite(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const response = await write(original.operationId); check()
    if (response?.code !== 0) throw new Error('结果暂未确认，请查询原操作。')
    return accept(scope, original, writerResponse(await plateMoldWriterApi.operation(original.operationId, false)), check)
  })
}
export function resolveToolWrite(scope: ToolWriterScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingToolWrite(scope); if (!original) throw new Error('没有待确认的原操作。')
    return accept(scope, original, writerResponse(await plateMoldWriterApi.operation(original.operationId, close)), check)
  })
}
