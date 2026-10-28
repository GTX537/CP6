import { transferEnvironmentApi as api, type TransferEnvironmentContext, type TransferEnvironmentOperation } from '@/api/wms/transferEnvironment'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export interface PendingTransferEnvironment { operationId: string; registrationId: string | null }
export function transferContextKey(s: TransferEnvironmentContext) {
  const d = s.deployment
  if (![s.tenantId, s.actorId, d?.environmentId].every(x => uuid.test(x)) || !['PRODUCTION', 'SIMULATION'].includes(d.mode)
    || (d.mode === 'PRODUCTION' ? d.sandboxScopeId !== null : !d.sandboxScopeId || !uuid.test(d.sandboxScopeId) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(d.sandboxScopeId))) throw new Error('无法确认当前身份或来源环境。')
  return `cp6_transfer_environment_v1:${d.environmentId}:${d.mode}:${d.sandboxScopeId || 'none'}:${s.tenantId}:${s.actorId}`.toLowerCase()
}
export function transferResponse<T>(r: EngineeringEnvelope<T>) { if (r?.code !== 0 || r.data == null) throw new Error('无法确认操作结果。'); return r.data }
export function pendingTransferEnvironment(s: TransferEnvironmentContext): PendingTransferEnvironment | null {
  const saved = localStorage.getItem(transferContextKey(s)); if (saved === null) return null
  const p = JSON.parse(saved) as PendingTransferEnvironment
  if (!p || !uuid.test(p.operationId) || (p.registrationId !== null && !uuid.test(p.registrationId))) throw new Error('原操作记录异常，请保留记录。')
  return { operationId: p.operationId, registrationId: p.registrationId }
}
async function exclusive<T>(s: TransferEnvironmentContext, guard: () => void, action: (check: () => void, current: TransferEnvironmentContext) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), key = transferContextKey(s)
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = transferResponse(await api.context()); check()
    if (transferContextKey(current) !== key) throw new Error('当前身份或环境已变化，请重新载入。')
    return action(check, current)
  })
}
function accept(s: TransferEnvironmentContext, p: PendingTransferEnvironment, v: TransferEnvironmentOperation, check: () => void) {
  check(); const r = v?.result
  if (v?.operationId !== p.operationId || !['COMMITTED', 'UNKNOWN', 'CLOSED'].includes(v.state)
    || (v.state === 'COMMITTED' ? !r || r.operationId !== p.operationId || r.reference?.owner !== 'Main.Wms.TransferEnvironment.v1'
      || !uuid.test(r.reference.id) || p.registrationId !== null && r.reference.id !== p.registrationId
      || !/^[1-9][0-9]*$/.test(r.reference.version) || !/^[0-9a-f]{64}$/.test(r.reference.digest) || typeof r.enabled !== 'boolean' || typeof r.changed !== 'boolean' : r != null)) throw new Error('返回结果与原操作不一致，请保留记录。')
  const saved = pendingTransferEnvironment(s)
  if (saved?.operationId !== p.operationId || saved.registrationId !== p.registrationId) throw new Error('原操作记录已变化。')
  if (v.state !== 'UNKNOWN') localStorage.removeItem(transferContextKey(s)); return v
}
export function saveTransferEnvironment(s: TransferEnvironmentContext, enabled: boolean, reason: string, guard: () => void) {
  return exclusive(s, guard, async (check, current) => {
    if (pendingTransferEnvironment(s)) throw new Error('请先查询或结束原操作。')
    if (current.version !== s.version || current.registration?.reference.id !== s.registration?.reference.id) throw new Error('注册版本已变化，请重新载入。')
    const p = { operationId: crypto.randomUUID(), registrationId: current.registration?.reference.id || null }
    localStorage.setItem(transferContextKey(s), JSON.stringify(p))
    if (pendingTransferEnvironment(s)?.operationId !== p.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = transferResponse(await api.save({ operationId: p.operationId, expectedVersion: s.version, enabled, reason }))
    return accept(s, p, { operationId: p.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveTransferEnvironment(s: TransferEnvironmentContext, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    const p = pendingTransferEnvironment(s); if (!p) throw new Error('没有待确认的原操作。')
    return accept(s, p, transferResponse(await api.operation(p.operationId, close)), check)
  })
}
