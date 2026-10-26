import { configurationApi, type ConfigurationEnvelope, type ConfigurationOperation, type ConfigurationOperationKind, type ConfigurationResult, type ConfigurationScope } from '@/api/plm/configurationRules'
import { identityStamp, requireIdentity } from './identity'

export interface PendingConfigurationOperation { operationId: string; kind: ConfigurationOperationKind; objectId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: ConfigurationScope, kind: ConfigurationOperationKind) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId) || !['CONFIGURATION_RULE'].includes(kind)) throw new Error('无法确认当前用户和租户。')
  return `cp6_configuration_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}:${kind}`
}
export function configurationResponse<T>(response: ConfigurationEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认操作结果。')
  return response.data
}
export function pendingConfigurationOperation(scope: ConfigurationScope, kind: ConfigurationOperationKind): PendingConfigurationOperation | null {
  const text = localStorage.getItem(key(scope, kind)); if (text === null) return null
  const value = JSON.parse(text) as PendingConfigurationOperation
  if (!value || !uuid.test(value.operationId) || value.kind !== kind || !uuid.test(value.objectId)) throw new Error('原操作记录异常，请保留记录并联系管理员。')
  return { operationId: value.operationId, kind, objectId: value.objectId }
}
async function exclusive<T>(scope: ConfigurationScope, kind: ConfigurationOperationKind, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope, kind), api = configurationApi
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理该操作，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = configurationResponse(await api.scope()); check()
    if (key(current, kind) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: ConfigurationScope, original: PendingConfigurationOperation, value: ConfigurationOperation, check: () => void) {
  check()
  const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId || receipt.kind !== original.kind
      || receipt.objectId !== original.objectId || !/^[1-9][0-9]*$/.test(receipt.version) || !/^[0-9a-f]{64}$/.test(receipt.digest) : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingConfigurationOperation(scope, original.kind)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope, original.kind))
  return value
}
export function saveConfigurationOperation(scope: ConfigurationScope, kind: ConfigurationOperationKind, objectId: string, guard: () => void,
  write: (id: string) => Promise<ConfigurationEnvelope<ConfigurationResult>>) {
  return exclusive(scope, kind, guard, async check => {
    if (!uuid.test(objectId)) throw new Error('请先选择当前对象。')
    if (pendingConfigurationOperation(scope, kind)) throw new Error('请先查询或结束原操作。')
    const original: PendingConfigurationOperation = { operationId: crypto.randomUUID(), kind, objectId }
    localStorage.setItem(key(scope, kind), JSON.stringify(original))
    if (pendingConfigurationOperation(scope, kind)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = configurationResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveConfigurationOperation(scope: ConfigurationScope, kind: ConfigurationOperationKind, close: boolean, guard: () => void) {
  return exclusive(scope, kind, guard, async check => {
    const original = pendingConfigurationOperation(scope, kind); if (!original) throw new Error('没有待确认的原操作。')
    const api = configurationApi
    return accept(scope, original, configurationResponse(await api.operation(original.operationId, close)), check)
  })
}
