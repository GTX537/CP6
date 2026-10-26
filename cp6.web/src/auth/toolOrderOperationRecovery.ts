import { toolLinkApi, type ToolLinkEnvelope, type ToolLinkOperation, type ToolLinkReceipt, type ToolLinkScope } from '@/api/erp/toolOrderLinks'
import { identityStamp, requireIdentity } from './identity'
export interface PendingToolOrder { operationId: string; objectId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: ToolLinkScope) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId)) throw new Error('无法确认当前用户和租户。')
  return `cp6_tool_order_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}`
}
export function toolResponse<T>(response: ToolLinkEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认原订单结果。')
  return response.data
}
export function pendingToolOrder(scope: ToolLinkScope): PendingToolOrder | null {
  const text = localStorage.getItem(key(scope)); if (text === null) return null
  const value = JSON.parse(text) as PendingToolOrder
  if (!value || !uuid.test(value.operationId) || !uuid.test(value.objectId)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: value.operationId, objectId: value.objectId }
}
async function exclusive<T>(scope: ToolLinkScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = toolResponse(await toolLinkApi.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: ToolLinkScope, original: PendingToolOrder, value: ToolLinkOperation, check: () => void) {
  check(); const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId || receipt.tenantId !== scope.tenantId
      || receipt.tool?.id !== original.objectId || receipt.tool.owner !== 'Main.Erp.PlateMoldSource.v1'
      || !/^[1-9][0-9]*$/.test(receipt.tool.version) || !/^[0-9a-f]{64}$/.test(receipt.tool.digest)
      || !uuid.test(receipt.order?.orderId) || !uuid.test(receipt.order?.lineId) || !receipt.order.number || !receipt.order.arrangeNumber
      || !receipt.order.orderRowVersion || !receipt.order.lineRowVersion || !receipt.nativeToolRowVersion
      || !Number.isSafeInteger(receipt.order.lineNumber) || receipt.order.lineNumber < 1
      || !['ORDER_CREATED', 'NATIVE_LINK_RECORDED', 'EXISTING_LINK'].includes(receipt.effect)
      || receipt.sourceKind !== 'NATIVE_ORDER_LINK_ONLY' || receipt.peState !== 'NOT_EXECUTED' : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingToolOrder(scope)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope))
  return value
}
export function saveToolOrder(scope: ToolLinkScope, objectId: string, guard: () => void, write: (id: string) => Promise<ToolLinkEnvelope<ToolLinkReceipt>>) {
  return exclusive(scope, guard, async check => {
    if (!uuid.test(objectId)) throw new Error('请先选择当前版型。')
    if (pendingToolOrder(scope)) throw new Error('请先查询或结束原操作。')
    const original: PendingToolOrder = { operationId: crypto.randomUUID(), objectId }
    localStorage.setItem(key(scope), JSON.stringify(original))
    if (pendingToolOrder(scope)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = toolResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveToolOrder(scope: ToolLinkScope, close: boolean, guard: () => void) {
  return exclusive(scope, guard, async check => {
    const original = pendingToolOrder(scope); if (!original) throw new Error('没有待确认的原操作。')
    return accept(scope, original, toolResponse(await toolLinkApi.operation(original.operationId, close)), check)
  })
}
