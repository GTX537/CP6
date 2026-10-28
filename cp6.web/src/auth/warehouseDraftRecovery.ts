import { warehouseDraftApi as api, type DraftScope, type DraftContext, type DraftCommand, type DraftOperation } from '@/api/wms/warehouseDraft'
import { identityStamp, requireIdentity } from './identity'
import { transferResponse as value } from './transferEnvironmentRecovery'
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function draftKey(s: DraftScope) {
  if (![s.tenantId, s.actorId].every(x => uuid.test(x))) throw new Error('无法确认当前身份。')
  return `cp6_warehouse_draft_v1:${s.tenantId}:${s.actorId}`.toLowerCase()
}
export function pendingDraft(s: DraftScope): string | null {
  const saved = localStorage.getItem(draftKey(s)); if (saved === null) return null
  if (!uuid.test(saved)) throw new Error('原操作记录异常，请保留记录。'); return saved
}
async function exclusive<T>(s: DraftScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), key = draftKey(s)
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = value(await api.scope()); check(); if (draftKey(current) !== key) throw new Error('当前身份已变化。')
    return action(check)
  })
}
function accept(s: DraftScope, id: string, v: DraftOperation, check: () => void) {
  check(); const r = v?.result
  if (v?.operationId !== id || !['COMMITTED', 'UNKNOWN', 'CLOSED'].includes(v.state)
    || (v.state === 'COMMITTED' ? !r || r.operationId !== id || !uuid.test(r.warehouseId) || !uuid.test(r.siteId)
      || r.effect !== 'CREATED_DRAFT' || r.businessRevision !== '1' || !/^"[A-Za-z0-9+/]{11}="$/.test(r.etag) : r != null)
    || pendingDraft(s) !== id) throw new Error('原操作结果不一致，请保留记录。')
  if (v.state !== 'UNKNOWN') localStorage.removeItem(draftKey(s)); return v
}
export function createDraft(context: DraftContext, body: Omit<DraftCommand, 'operationId' | 'authorityVersion'>, guard: () => void) {
  const s = context.creation
  return exclusive(s, guard, async check => {
    if (pendingDraft(s)) throw new Error('请先查询或关闭原操作。')
    const current = value(await api.context()); check()
    if (draftKey(current.creation) !== draftKey(s) || current.creation.authorityVersion !== s.authorityVersion) throw new Error('创建权限已变化，请重新载入。')
    const id = crypto.randomUUID(); localStorage.setItem(draftKey(s), id)
    if (pendingDraft(s) !== id) throw new Error('无法保存恢复记录。')
    check(); const result = value(await api.create({ ...body, operationId: id, authorityVersion: s.authorityVersion }))
    if (result.siteId !== body.siteId) throw new Error('返回仓库与选择站点不一致，请查询原操作。')
    return accept(s, id, { operationId: id, state: 'COMMITTED', result }, check)
  })
}
export function recoverDraft(s: DraftScope, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    const id = pendingDraft(s); if (!id) throw new Error('没有待确认的原操作。')
    return accept(s, id, value(await api.operation(id, close)), check)
  })
}
