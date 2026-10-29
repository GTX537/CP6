import { rmaApplicationsApi as api, type RmaScope, type RmaResult } from '@/api/wms/rmaApplications'

export const rmaPendingKey = (scope: RmaScope) => `cp6_rma_application_v1:${scope.tenantId}:${scope.actorId}`.toLowerCase()
export function pendingRma(scope: RmaScope): string | null {
  const id = localStorage.getItem(rmaPendingKey(scope))
  // localStorage is an external persistence boundary; never erase malformed recovery evidence.
  if (id !== null && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) throw new Error('原操作记录异常，请保留记录。')
  return id
}
async function exclusive<T>(scope: RmaScope, check: () => void, action: () => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器不支持多页面协调，暂不能提交。')
  return navigator.locks.request(rmaPendingKey(scope), { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理申请，请稍后查询。')
    check()
    // One current server scope check binds durable recovery to the actual actor/tenant.
    const current = await api.scope(); check()
    if (rmaPendingKey(current) !== rmaPendingKey(scope)) throw new Error('当前身份已变化，请重新载入。')
    return action()
  })
}
export function writeRma(scope: RmaScope, send: (id: string) => Promise<RmaResult>, check: () => void) {
  return exclusive(scope, check, async () => {
    if (pendingRma(scope)) throw new Error('请先查询或关闭原操作。')
    const id = crypto.randomUUID()
    localStorage.setItem(rmaPendingKey(scope), id)
    const result = await send(id); check()
    localStorage.removeItem(rmaPendingKey(scope))
    return result
  })
}
export function recoverRma(scope: RmaScope, close: boolean, check: () => void) {
  return exclusive(scope, check, async () => {
    const id = pendingRma(scope)
    if (!id) throw new Error('没有待确认的原操作。')
    const operation = await api.operation(id, close); check()
    if (operation.state !== 'UNKNOWN') localStorage.removeItem(rmaPendingKey(scope))
    return operation
  })
}
