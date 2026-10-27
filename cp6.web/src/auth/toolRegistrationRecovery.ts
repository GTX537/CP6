import { toolInstanceApi } from '@/api/wms/toolInstances'
import { toolRegistrationApi as api, type ToolRegistrationOperation, type ToolRegistrationResult } from '@/api/wms/toolRegistrations'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
import { instanceResponse } from './toolInstanceRecovery'
export interface PendingToolRegistration { operationId: string; objectId: string; kind: 'TOOL_CREATE' | 'TOOL_INITIALIZE' }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EngineeringScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_tool_registration_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function pendingToolRegistration(scope: EngineeringScope): PendingToolRegistration | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingToolRegistration
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !['TOOL_CREATE', 'TOOL_INITIALIZE'].includes(value.kind)) throw new Error('原登记记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, kind: value.kind }
}
async function exclusive<T>(scope: EngineeringScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原登记。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = instanceResponse(await toolInstanceApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringScope, original: PendingToolRegistration, value: ToolRegistrationOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId || receipt.objectId !== original.objectId
      || receipt.kind !== original.kind || receipt.version !== '1' || !/^[0-9a-f]{64}$/.test(receipt.digest) : receipt != null))
    throw new Error('返回结果与原登记不一致，请保留记录。')
  const current = pendingToolRegistration(scope)
  if (current?.operationId !== original.operationId || current.objectId !== original.objectId || current.kind !== original.kind) throw new Error('原登记记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveToolRegistration(scope: EngineeringScope, objectId: string, kind: PendingToolRegistration['kind'], guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<ToolRegistrationResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId)) throw new Error('无法确认实物身份。')
    if (pendingToolRegistration(scope)) throw new Error('请先查询或结束原登记。')
    const original: PendingToolRegistration = { operationId: crypto.randomUUID(), objectId, kind }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingToolRegistration(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = instanceResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveToolRegistration(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingToolRegistration(scope); if (!original) throw new Error('没有待确认的原登记。')
    return accept(scope, original, instanceResponse(await api.operation(original.operationId, close)), check)
  })
}
