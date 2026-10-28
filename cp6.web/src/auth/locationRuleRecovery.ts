import { locationRuleApi as api, type RuleScope, type RuleOperation, type RuleResult } from '@/api/wms/locationRule'
import { identityStamp, requireIdentity } from './identity'
import { transferResponse as value } from './transferEnvironmentRecovery'
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function locationRuleKey(s: RuleScope) {
  if (![s.tenantId, s.actorId, s.locationId].every(x => uuid.test(x))) throw new Error('无法确认当前身份和库位。')
  return `cp6_location_rule_v1:${s.tenantId}:${s.actorId}:${s.locationId}`.toLowerCase()
}
export function pendingLocationRule(s: RuleScope) {
  const id = localStorage.getItem(locationRuleKey(s)); if (id !== null && !uuid.test(id)) throw new Error('原操作记录异常，请保留记录。'); return id
}
async function exclusive<T>(s: RuleScope, guard: () => void, run: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp(), key = locationRuleKey(s)
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const current = value(await api.scope(s.locationId)); check(); if (locationRuleKey(current) !== key) throw new Error('当前身份或库位已变化。')
    return run(check)
  })
}
function accept(s: RuleScope, id: string, v: RuleOperation, check: () => void) {
  check(); const r = v?.result
  if (v?.operationId !== id || v.locationId !== s.locationId || !['COMMITTED', 'UNKNOWN', 'CLOSED'].includes(v.state)
    || (v.state === 'COMMITTED' ? !r || r.operationId !== id || r.locationId !== s.locationId || !uuid.test(r.selectorId)
      || r.reference?.owner !== 'Main.Wms.LocationRuleProposal.v1' || !uuid.test(r.reference.id) || !/^[1-9][0-9]*$/.test(r.reference.version)
      || !/^[a-f0-9]{64}$/.test(r.reference.digest) || !/^"[a-f0-9]{32}"$/.test(r.etag)
      || !['CREATED', 'UPDATED', 'NO_CHANGE', 'WITHDRAWN', 'RESOLVED'].includes(r.effect) || (r.effect === 'WITHDRAWN' ? r.state !== 'WITHDRAWN' : r.effect === 'NO_CHANGE' ? !['DRAFT', 'RETURNED'].includes(r.state) : r.state !== 'DRAFT') : r != null)
    || pendingLocationRule(s) !== id) throw new Error('原操作结果不一致，请保留记录。')
  if (v.state !== 'UNKNOWN') localStorage.removeItem(locationRuleKey(s)); return v
}
export function submitLocationRule(s: RuleScope, proposal: string | null, send: (id: string) => Promise<{ code: number; data: RuleResult }>, guard: () => void) {
  return exclusive(s, guard, async check => {
    if (pendingLocationRule(s)) throw new Error('请先查询或关闭原操作。')
    const id = crypto.randomUUID(); localStorage.setItem(locationRuleKey(s), id)
    if (pendingLocationRule(s) !== id) throw new Error('无法保存恢复记录。')
    check(); const r = value(await send(id)); check()
    if (proposal !== null && r.reference?.id !== proposal) throw new Error('候选身份不一致，请查询原操作。')
    return accept(s, id, { operationId: id, locationId: s.locationId, state: 'COMMITTED', result: r }, check)
  })
}
export function recoverLocationRule(s: RuleScope, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    const id = pendingLocationRule(s); if (!id) throw new Error('没有待确认的原操作。')
    return accept(s, id, value(await api.operation(s.locationId, id, close)), check)
  })
}
