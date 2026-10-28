import { inventoryIdentityApi as api, type IdentityScope, type IdentityResult, type IdentityOperation } from '@/api/wms/inventoryIdentities'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingInventoryIdentity { operationId: string; objectId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(s: IdentityScope) {
  if (![s.tenantId, s.actorId, s.environmentId].every(x => uuid.test(x)) || !['REAL', 'DEMO'].includes(s.evidenceMode)) throw new Error('无法确认当前身份和环境。')
  return `cp6_inventory_identity_operation_v1:${s.environmentId}:${s.tenantId}:${s.actorId}:${s.evidenceMode}`.toLowerCase()
}
export function identityResponse<T>(r: EngineeringEnvelope<T>) { if (r?.code !== 0 || r.data == null) throw new Error('无法确认操作结果。'); return r.data }
export function pendingInventoryIdentity(s: IdentityScope): PendingInventoryIdentity | null {
  const text = localStorage.getItem(key(s)); if (text === null) return null
  const v = JSON.parse(text) as PendingInventoryIdentity
  if (!v || !uuid.test(v.operationId) || !uuid.test(v.objectId)) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: v.operationId, objectId: v.objectId }
}
async function exclusive<T>(s: IdentityScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(s)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check(); const current = identityResponse(await api.scope()); check()
    if (key(current) !== channel) throw new Error('当前身份或环境已变化，请重新载入。')
    return action(check)
  })
}
function accept(s: IdentityScope, pending: PendingInventoryIdentity, v: IdentityOperation, check: () => void) {
  check(); const r = v?.result
  if (v?.operationId !== pending.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(v.state)
    || (v.state === 'COMMITTED' ? !r || r.operationId !== pending.operationId || r.objectId !== pending.objectId
      || !/^[1-9][0-9]*$/.test(r.version) || !/^[0-9a-f]{64}$/.test(r.digest) || typeof r.changed !== 'boolean' : r != null)) throw new Error('返回结果与原操作不一致，请保留记录。')
  const original = pendingInventoryIdentity(s)
  if (original?.operationId !== pending.operationId || original.objectId !== pending.objectId) throw new Error('原操作记录已变化。')
  if (v.state !== 'UNKNOWN') localStorage.removeItem(key(s)); return v
}
export function saveInventoryIdentity(s: IdentityScope, objectId: string, guard: () => void, write: (id: string) => Promise<EngineeringEnvelope<IdentityResult>>) {
  return exclusive(s, guard, async check => {
    if (!uuid.test(objectId) || pendingInventoryIdentity(s)) throw new Error('请先查询或结束原操作。')
    const original = { operationId: crypto.randomUUID(), objectId }; localStorage.setItem(key(s), JSON.stringify(original))
    if (pendingInventoryIdentity(s)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = identityResponse(await write(original.operationId)); return accept(s, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveInventoryIdentity(s: IdentityScope, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => { const original = pendingInventoryIdentity(s); if (!original) throw new Error('没有待确认的原操作。'); return accept(s, original, identityResponse(await api.operation(original.operationId, close)), check) })
}
