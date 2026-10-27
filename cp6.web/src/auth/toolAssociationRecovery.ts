import { toolAssociationApi, type ToolAssociationOperation, type ToolAssociationResult } from '@/api/wms/toolAssociations'
import type { EngineeringEnvelope, EngineeringScope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingToolAssociation { operationId: string; objectId: string; proposalId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: EngineeringScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_tool_association_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function associationResponse<T>(response: EngineeringEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认工程实物关联原结果。')
  return response.data
}
export function pendingToolAssociation(scope: EngineeringScope): PendingToolAssociation | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingToolAssociation
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !uuid.test(value.proposalId)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, proposalId: value.proposalId }
}
async function exclusive<T>(scope: EngineeringScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = associationResponse(await toolAssociationApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: EngineeringScope, original: PendingToolAssociation, value: ToolAssociationOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId
      || receipt.objectId !== original.objectId || receipt.proposalId !== original.proposalId || receipt.notExecutionPermit !== true || receipt.kind !== 'TOOL_ASSOCIATION'
      || !/^[1-9][0-9]*$/.test(receipt.version) || !/^[0-9a-f]{64}$/.test(receipt.digest) : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingToolAssociation(scope)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId || pending.proposalId !== original.proposalId) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveToolAssociation(scope: EngineeringScope, objectId: string, proposalId: string, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<ToolAssociationResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId) || !uuid.test(proposalId)) throw new Error('请先选择当前实物。')
    if (pendingToolAssociation(scope)) throw new Error('请先查询或结束原操作。')
    const original: PendingToolAssociation = { operationId: crypto.randomUUID(), objectId, proposalId }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingToolAssociation(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = associationResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveToolAssociation(scope: EngineeringScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingToolAssociation(scope); if (!original) throw new Error('没有待确认的原操作。')
    return accept(scope, original, associationResponse(await toolAssociationApi.operation(original.operationId, close)), check)
  })
}
