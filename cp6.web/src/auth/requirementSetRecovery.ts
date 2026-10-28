import type { RequirementSetScope } from '@/api/plm/requirementSet'
import { requirementSetApi, type RequirementSetOperation, type RequirementSetResult } from '@/api/plm/requirementSet'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingRequirementSet { operationId: string; objectId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: RequirementSetScope) {
  if (!uuid.test(scope.environmentId) || !['REAL', 'DEMO'].includes(scope.evidenceMode) || !uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_requirementSet_operation_v1:${scope.environmentId.toLowerCase()}:${scope.evidenceMode}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function requirementSetResponse<T>(response: EngineeringEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认需求集原结果。')
  return response.data
}
export function pendingRequirementSet(scope: RequirementSetScope): PendingRequirementSet | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingRequirementSet
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId }
}
async function exclusive<T>(scope: RequirementSetScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = requirementSetResponse(await requirementSetApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: RequirementSetScope, original: PendingRequirementSet, value: RequirementSetOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId
      || receipt.objectId !== original.objectId || receipt.kind !== 'SET'
      || !/^[1-9][0-9]*$/.test(receipt.version) || !/^[0-9a-f]{64}$/.test(receipt.digest) : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingRequirementSet(scope)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveRequirementSet(scope: RequirementSetScope, objectId: string, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<RequirementSetResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId)) throw new Error('请先选择当前需求集。')
    if (pendingRequirementSet(scope)) throw new Error('请先查询或结束原操作。')
    const original: PendingRequirementSet = { operationId: crypto.randomUUID(), objectId }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingRequirementSet(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = requirementSetResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveRequirementSet(scope: RequirementSetScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingRequirementSet(scope); if (!original) throw new Error('没有待确认的原操作。')
    return accept(scope, original, requirementSetResponse(await requirementSetApi.operation(original.operationId, close)), check)
  })
}
