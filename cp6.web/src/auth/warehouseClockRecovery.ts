import { warehouseClockApi as api, type ClockContext, type ClockCommand, type ClockOperation } from '@/api/wms/warehouseClock'
import { masterUuid } from '@/types/wms/warehouseOwner'
import { transferResponse as value } from './transferEnvironmentRecovery'
import { identityStamp, requireIdentity } from './identity'
export function clockKey(s: ClockContext) {
  if (![s.tenantId, s.actorId, s.warehouseId].every(masterUuid) || s.qualification !== 'CLOCK_CONFIGURATION_ONLY' || s.admissionGrant !== false || !/^(absent|[1-9][0-9]*)$/.test(s.version)) throw new Error('无法确认时钟政策的仓库和当前身份。')
  return `cp6_warehouse_clock_v1:${s.tenantId}:${s.actorId}:${s.warehouseId}`.toLowerCase()
}
export function pendingClock(s: ClockContext) {
  const id = localStorage.getItem(clockKey(s)); if (id !== null && !masterUuid(id)) throw new Error('原操作记录异常，请保留记录。'); return id
}
async function exclusive<T>(s: ClockContext, guard: () => void, work: (check: () => void, fresh: ClockContext) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const key = clockKey(s), stamp = identityStamp()
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const fresh = value(await api.context(s.warehouseId)); check()
    if (clockKey(fresh) !== key) throw new Error('当前身份或仓库已变化。')
    return work(check, fresh)
  })
}
function accept(s: ClockContext, id: string, r: ClockOperation, check: () => void) {
  check(); const v = r?.result
  if (r?.operationId !== id || r.warehouseId !== s.warehouseId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(r.state)
    || (r.state === 'COMMITTED' ? !v || v.operationId !== id || v.warehouseId !== s.warehouseId || !masterUuid(v.reference?.id)
      || v.reference.owner !== 'Main.Wms.WarehouseClockPolicy.v1' || !/^[1-9][0-9]*$/.test(v.reference.version)
      || !/^[0-9a-f]{64}$/.test(v.reference.digest) || typeof v.changed !== 'boolean' : v != null)
    || pendingClock(s) !== id) throw new Error('回执与原操作不一致，请保留记录。')
  if (r.state !== 'UNKNOWN') localStorage.removeItem(clockKey(s)); return r
}
export function saveClock(s: ClockContext, c: Pick<ClockCommand, 'definition' | 'reason'>, guard: () => void) {
  return exclusive(s, guard, async (check, fresh) => {
    if (pendingClock(s)) throw new Error('请先查询或关闭原操作。')
    if (JSON.stringify(fresh.source?.reference) !== JSON.stringify(s.source?.reference)) throw new Error('时钟来源已变化，请重新载入。')
    if (fresh.version !== s.version || JSON.stringify(fresh.policy?.reference) !== JSON.stringify(s.policy?.reference)) throw new Error('政策已变化，请重新载入。')
    const id = crypto.randomUUID(); localStorage.setItem(clockKey(s), id)
    if (pendingClock(s) !== id) throw new Error('无法保存恢复记录。')
    check(); const result = value(await api.save(s.warehouseId, { ...c, operationId: id, expectedVersion: s.version }))
    return accept(s, id, { operationId: id, warehouseId: s.warehouseId, state: 'COMMITTED', result }, check)
  })
}
export function recoverClock(s: ClockContext, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    const id = pendingClock(s); if (!id) throw new Error('没有待确认的原操作。')
    return accept(s, id, value(await api.operation(s.warehouseId, id, close)), check)
  })
}
