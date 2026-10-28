import { capabilityApi as resourceApi } from '@/api/mes/resourceCapabilities'
import type { ResourceEnvelope, ResourceOperation, ResourceKind, ResourceResult, ResourceScope } from '@/api/mes/resourceSources'
import { identityStamp, requireIdentity } from './identity'

export interface PendingCapabilityOperation { operationId: string; kind: ResourceKind; objectId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: ResourceScope, kind: ResourceKind) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId) || !['MACHINE', 'WORK_CENTER'].includes(kind)) throw new Error('无法确认当前用户和租户。')
  return `cp6_resource_capability_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}:${kind}`
}
export function capabilityResponse<T>(response: ResourceEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认操作结果。')
  return response.data
}
export function pendingCapabilityOperation(scope: ResourceScope, kind: ResourceKind): PendingCapabilityOperation | null {
  const text = localStorage.getItem(key(scope, kind)); if (text === null) return null
  const value = JSON.parse(text) as PendingCapabilityOperation
  if (!value || !uuid.test(value.operationId) || value.kind !== kind || !uuid.test(value.objectId)) throw new Error('原操作记录异常，请保留记录并联系管理员。')
  return { operationId: value.operationId, kind, objectId: value.objectId }
}
async function exclusive<T>(scope: ResourceScope, kind: ResourceKind, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope, kind), api = resourceApi(kind)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理该操作，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = capabilityResponse(await api.scope()); check()
    if (key(current, kind) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: ResourceScope, original: PendingCapabilityOperation, value: ResourceOperation, check: () => void) {
  check()
  const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId || receipt.kind !== original.kind
      || receipt.objectId !== original.objectId || !/^[1-9][0-9]*$/.test(receipt.version) || !/^[0-9a-f]{64}$/.test(receipt.digest) : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingCapabilityOperation(scope, original.kind)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope, original.kind))
  return value
}
export function saveCapabilityOperation(scope: ResourceScope, kind: ResourceKind, objectId: string, guard: () => void,
  write: (id: string) => Promise<ResourceEnvelope<ResourceResult>>) {
  return exclusive(scope, kind, guard, async check => {
    if (!uuid.test(objectId)) throw new Error('请先选择当前对象。')
    if (pendingCapabilityOperation(scope, kind)) throw new Error('请先查询或结束原操作。')
    const original: PendingCapabilityOperation = { operationId: crypto.randomUUID(), kind, objectId }
    localStorage.setItem(key(scope, kind), JSON.stringify(original))
    if (pendingCapabilityOperation(scope, kind)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = capabilityResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveCapabilityOperation(scope: ResourceScope, kind: ResourceKind, close: boolean, guard: () => void) {
  return exclusive(scope, kind, guard, async check => {
    const original = pendingCapabilityOperation(scope, kind); if (!original) throw new Error('没有待确认的原操作。')
    const api = resourceApi(kind)
    return accept(scope, original, capabilityResponse(await api.operation(original.operationId, close)), check)
  })
}
