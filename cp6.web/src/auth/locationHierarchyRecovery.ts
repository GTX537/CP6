import { locationHierarchyApi as api, type HierarchyContext, type HierarchyCommand, type HierarchyOperation } from '@/api/wms/locationHierarchy'
import { masterUuid } from '@/types/wms/warehouseOwner'
import { transferResponse as value } from './transferEnvironmentRecovery'
import { identityStamp, requireIdentity } from './identity'
export const hierarchyKey = (s: HierarchyContext) => {
  if (![s.tenantId, s.actorId, s.warehouseId].every(masterUuid) || s.creationGrant !== false || s.initializationFence !== 'UNAVAILABLE'
    || !/^(absent|[1-9][0-9]*)$/.test(s.version) || !/^(0|[1-9][0-9]*)$/.test(s.hierarchyRevision)) throw new Error('无法确认当前仓库或策略范围。')
  return `cp6_location_hierarchy_v1:${s.tenantId}:${s.actorId}:${s.warehouseId}`.toLowerCase()
}
export function pendingHierarchy(s: HierarchyContext) {
  const raw = localStorage.getItem(hierarchyKey(s)); if (raw === null) return null
  if (!masterUuid(raw)) throw new Error('原操作记录异常，请保留记录。'); return raw
}
async function exclusive<T>(s: HierarchyContext, guard: () => void, work: (check: () => void, fresh: HierarchyContext) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面。')
  const key = hierarchyKey(s), stamp = identityStamp()
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const fresh = value(await api.context(s.warehouseId)); check()
    if (hierarchyKey(fresh) !== key) throw new Error('当前身份或仓库已变化，请重新载入。')
    return work(check, fresh)
  })
}
function accept(s: HierarchyContext, id: string, r: HierarchyOperation, check: () => void) {
  check(); const v = r?.result
  if (r?.operationId !== id || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(r.state)
    || (r.state === 'COMMITTED' ? !v || v.operationId !== id || v.warehouseId !== s.warehouseId || !masterUuid(v.reference?.id)
      || v.reference.owner !== 'Main.Wms.LocationHierarchy.v1' || !/^[1-9][0-9]*$/.test(v.reference.version)
      || !/^[0-9a-f]{64}$/.test(v.reference.digest) || typeof v.changed !== 'boolean' : v != null)) throw new Error('回执与原操作不一致，请保留记录。')
  if (pendingHierarchy(s) !== id) throw new Error('原操作记录已变化。')
  if (r.state !== 'UNKNOWN') localStorage.removeItem(hierarchyKey(s)); return r
}
export function saveHierarchy(s: HierarchyContext, c: Pick<HierarchyCommand, 'enabled' | 'rootLevels' | 'edges' | 'reason'>, guard: () => void) {
  return exclusive(s, guard, async (check, fresh) => {
    if (pendingHierarchy(s)) throw new Error('请先查询或关闭原操作。')
    if (fresh.version !== s.version || fresh.hierarchyRevision !== s.hierarchyRevision) throw new Error('策略或库位结构已变化，请重新载入。')
    const id = crypto.randomUUID(); localStorage.setItem(hierarchyKey(s), id)
    if (pendingHierarchy(s) !== id) throw new Error('无法保存原操作记录。')
    check(); const result = value(await api.save(s.warehouseId, { ...c, operationId: id, expectedVersion: s.version, expectedHierarchyRevision: s.hierarchyRevision }))
    return accept(s, id, { operationId: id, state: 'COMMITTED', result }, check)
  })
}
export function recoverHierarchy(s: HierarchyContext, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    const id = pendingHierarchy(s); if (!id) throw new Error('没有待确认的原操作。')
    return accept(s, id, value(await api.operation(s.warehouseId, id, close)), check)
  })
}
