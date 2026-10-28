import { qualityApi, type QualityOperation, type QualityResult, type QualityAction, type QualityScope } from '@/api/mes/qualitySpecification'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingQuality { operationId: string; objectId: string; action: QualityAction }
const actions = ['CREATE', 'REVISE', 'POLICY', 'SUBMIT', 'APPROVE', 'RETURN', 'WITHDRAW']
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: QualityScope) {
  if (!uuid.test(scope.environmentId) || !uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_quality_operation_v1:${scope.environmentId.toLowerCase()}:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function qualityResponse<T>(response: EngineeringEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认质量规格原结果。')
  return response.data
}
export function pendingQuality(scope: QualityScope): PendingQuality | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingQuality
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId) || !actions.includes(value.action)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId, action: value.action }
}
async function exclusive<T>(scope: QualityScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = qualityResponse(await qualityApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function exact(value: any, id: string, owner: string) { return value?.owner === owner && value.id === id && /^[1-9][0-9]*$/.test(value.version) && /^[0-9a-f]{64}$/.test(value.digest) }
function accept(scope: QualityScope, original: PendingQuality, value: QualityOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId
      || receipt.objectId !== original.objectId || receipt.action !== original.action
      || !exact(receipt.specification, original.objectId, 'Main.Quality.Specification.v1') || !exact(receipt.control, original.objectId, 'Main.Quality.SpecificationControl.v1') : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingQuality(scope)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId || pending.action !== original.action) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveQuality(scope: QualityScope, objectId: string, action: QualityAction, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<QualityResult>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId) || !actions.includes(action)) throw new Error('请先选择当前质量规格。')
    if (pendingQuality(scope)) throw new Error('请先查询或结束原操作。')
    const original: PendingQuality = { operationId: crypto.randomUUID(), objectId, action }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingQuality(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = qualityResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveQuality(scope: QualityScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingQuality(scope); if (!original) throw new Error('没有待确认的原操作。')
    return accept(scope, original, qualityResponse(await qualityApi.operation(original.operationId, close)), check)
  })
}
