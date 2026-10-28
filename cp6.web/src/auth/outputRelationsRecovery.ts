import { outputRelationsApi, type OutputOperation, type OutputResult, type OutputAction, type OutputScope } from '@/api/plm/engineeringOutputRelations'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingOutput { operationId: string; objectId: string; action: OutputAction }
const actions = ['CREATE', 'REVISE', 'POLICY', 'QUALIFY', 'WITHDRAW']
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: OutputScope) {
  if (!uuid.test(scope.environmentId) || !uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_output_relations_operation_v1:${scope.environmentId.toLowerCase()}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function outputResponse<T>(response: EngineeringEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认工程输出关系原结果。')
  return response.data
}
export function pendingOutput(scope: OutputScope): PendingOutput | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingOutput
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !actions.includes(value.action)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, action: value.action }
}
async function exclusive<T>(scope: OutputScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = outputResponse(await outputRelationsApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function exact(value: any, id: string, owner: string) { return value?.owner === owner && value.id === id && /^[1-9][0-9]*$/.test(value.version) && /^[0-9a-f]{64}$/.test(value.digest) }
function accept(scope: OutputScope, original: PendingOutput, value: OutputOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId
      || receipt.objectId !== original.objectId || receipt.action !== original.action
      || !exact(receipt.relation, original.objectId, 'Main.Plm.EngineeringOutputRelations.v1') || !exact(receipt.control, original.objectId, 'Main.Plm.EngineeringOutputRelationsControl.v1') : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingOutput(scope)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId || pending.action !== original.action) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveOutput(scope: OutputScope, objectId: string, action: OutputAction, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<OutputResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId) || !actions.includes(action)) throw new Error('请先选择当前工程输出关系。')
    if (pendingOutput(scope)) throw new Error('请先查询或结束原操作。')
    const original: PendingOutput = { operationId: crypto.randomUUID(), objectId, action }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingOutput(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = outputResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveOutput(scope: OutputScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingOutput(scope); if (!original) throw new Error('没有待确认的原操作。')
    return accept(scope, original, outputResponse(await outputRelationsApi.operation(original.operationId, close)), check)
  })
}
