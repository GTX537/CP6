import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { transferRequestsApi as api, type TransferRequestContext, type TransferRequestResult } from '@/api/wms/transferRequests'
import { identityStamp, requireIdentity } from './identity'

export function transferRequestData<T>(r: EngineeringEnvelope<T>): T {
  if (r.code !== 0 || r.data == null) throw new Error('未取得操作结果。')
  return r.data
}
export function transferRequestKey(c: TransferRequestContext) {
  return `cp6_transfer_requests_v1:${c.deployment.environmentId}:${c.deployment.mode}:${c.deployment.sandboxScopeId || 'none'}:${c.tenantId}:${c.actorId}`
}
export function pendingTransferRequest(c: TransferRequestContext): string | null {
  const id = localStorage.getItem(transferRequestKey(c))
  if (id !== null && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) throw new Error('恢复记录损坏，请保留记录并联系管理员。')
  return id
}
async function locked<T>(c: TransferRequestContext, guard: () => void, work: (check: () => void, key: string) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('浏览器不支持多页面提交协调。')
  const stamp = identityStamp(), key = transferRequestKey(c)
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在提交，请稍后查询。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    return work(check, key)
  })
}
export function sendTransferRequest(c: TransferRequestContext, send: (operation: string) => Promise<EngineeringEnvelope<TransferRequestResult>>, guard: () => void) {
  return locked(c, guard, async (check, key) => {
    if (pendingTransferRequest(c)) throw new Error('请先确认上一笔操作结果。')
    const id = crypto.randomUUID(); localStorage.setItem(key, id); check()
    const result = transferRequestData(await send(id)); check()
    if (result.operationId !== id) throw new Error('操作编号不符，请保留恢复记录。')
    localStorage.removeItem(key); return result
  })
}
export function recoverTransferRequest(c: TransferRequestContext, close: boolean, guard: () => void) {
  return locked(c, guard, async (check, key) => {
    const id = pendingTransferRequest(c); if (!id) throw new Error('没有待确认操作。')
    const result = transferRequestData(await api.operation(id, close)); check()
    if (result.operationId !== id) throw new Error('原操作编号不符。')
    if (result.state !== 'UNKNOWN') localStorage.removeItem(key)
    return result
  })
}
export function transferQuantity(input: string) {
  if (!/^(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,8})?$/.test(input)) throw new Error('数量需为正数，最多 8 位小数。')
  const [whole, fraction = ''] = input.split('.'), value = `${whole}.${fraction.padEnd(8, '0')}`
  if (value === '0.00000000') throw new Error('数量必须大于零。')
  return value
}
