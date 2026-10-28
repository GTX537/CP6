import { locationDraftApi as api, type LocationDraftScope, type LocationDraftContext, type LocationDraftCommand, type LocationDraftOperation } from '@/api/wms/locationDraft'
import { identityStamp, requireIdentity } from './identity'
import { transferResponse as value } from './transferEnvironmentRecovery'
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function locationDraftKey(s: LocationDraftScope) {
  if (![s.tenantId, s.actorId, s.warehouseId].every(x => uuid.test(x))) throw new Error('无法确认当前身份和仓库。')
  return `cp6_location_draft_v1:${s.tenantId}:${s.actorId}:${s.warehouseId}`.toLowerCase()
}
export function pendingLocationDraft(s: LocationDraftScope): string | null {
  const saved = localStorage.getItem(locationDraftKey(s)); if (saved === null) return null
  if (!uuid.test(saved)) throw new Error('原操作记录异常，请保留记录。'); return saved
}
async function exclusive<T>(s: LocationDraftScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), key = locationDraftKey(s)
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = value(await api.scope(s.warehouseId)); check(); if (locationDraftKey(current) !== key) throw new Error('当前身份或仓库已变化。')
    return action(check)
  })
}
function accept(s: LocationDraftScope, id: string, v: LocationDraftOperation, check: () => void) {
  check(); const r = v?.result
  if (v?.operationId !== id || v.warehouseId !== s.warehouseId || !['COMMITTED', 'UNKNOWN', 'CLOSED'].includes(v.state)
    || (v.state === 'COMMITTED' ? !r || r.operationId !== id || r.warehouseId !== s.warehouseId || !uuid.test(r.locationId) || !uuid.test(r.siteId)
      || r.effect !== 'CREATED_QUARANTINED_DRAFT' || r.businessRevision !== '1' || !/^[1-9][0-9]*$/.test(r.hierarchyRevision) || !/^"[A-Za-z0-9+/]{11}="$/.test(r.etag) : r != null)
    || pendingLocationDraft(s) !== id) throw new Error('原操作结果不一致，请保留记录。')
  if (v.state !== 'UNKNOWN') localStorage.removeItem(locationDraftKey(s)); return v
}
export function createLocationDraft(context: LocationDraftContext, body: Pick<LocationDraftCommand, 'departmentId' | 'locationCode' | 'locationName' | 'parentLocationId' | 'locationLevel'>, guard: () => void) {
  const s = context.scope
  return exclusive(s, guard, async check => {
    if (pendingLocationDraft(s)) throw new Error('请先查询或关闭原操作。')
    const current = value(await api.context(s.warehouseId)); check()
    if (locationDraftKey(current.scope) !== locationDraftKey(s) || current.creation.authorityVersion !== context.creation.authorityVersion
      || current.creation.tenantId !== s.tenantId || current.creation.actorId !== s.actorId || current.policy.reference.version !== context.policy.reference.version
      || current.hierarchyRevision !== context.hierarchyRevision) throw new Error('权限或层级前驱已变化，请重新载入。')
    const id = crypto.randomUUID(); localStorage.setItem(locationDraftKey(s), id)
    if (pendingLocationDraft(s) !== id) throw new Error('无法保存恢复记录。')
    check(); const result = value(await api.create(s.warehouseId, { ...body, operationId: id, authorityVersion: context.creation.authorityVersion, expectedPolicyVersion: context.policy.reference.version, expectedHierarchyRevision: context.hierarchyRevision, barcode: null }))
    if (result.siteId !== context.siteRef.id) throw new Error('返回站点不一致，请查询原操作。')
    return accept(s, id, { operationId: id, warehouseId: s.warehouseId, state: 'COMMITTED', result }, check)
  })
}
export function recoverLocationDraft(s: LocationDraftScope, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    const id = pendingLocationDraft(s); if (!id) throw new Error('没有待确认的原操作。')
    return accept(s, id, value(await api.operation(s.warehouseId, id, close)), check)
  })
}
