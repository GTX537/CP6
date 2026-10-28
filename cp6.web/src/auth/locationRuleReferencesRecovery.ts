import { ruleReferencesApi as api, type RuleReferenceContext, type RuleReferenceCommand, type RuleReferenceOperation } from '@/api/wms/locationRuleReferences'
import { masterUuid } from '@/types/wms/warehouseOwner'
import { transferResponse as value } from './transferEnvironmentRecovery'
import { identityStamp, requireIdentity } from './identity'
export function ruleReferencesKey(s: RuleReferenceContext) {
  if (![s.tenantId, s.actorId, s.warehouseId].every(masterUuid) || s.qualification !== 'CONFIGURATION_ONLY' || s.admissionGrant !== false || !/^(absent|[1-9][0-9]*)$/.test(s.version)) throw new Error('无法确认规则字典的仓库和当前身份。')
  return `cp6_rule_references_v1:${s.tenantId}:${s.actorId}:${s.warehouseId}`.toLowerCase()
}
export function pendingRuleReferences(s: RuleReferenceContext) {
  const id = localStorage.getItem(ruleReferencesKey(s)); if (id !== null && !masterUuid(id)) throw new Error('原操作记录异常，请保留记录。'); return id
}
async function exclusive<T>(s: RuleReferenceContext, guard: () => void, work: (check: () => void, fresh: RuleReferenceContext) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const key = ruleReferencesKey(s), stamp = identityStamp()
  return navigator.locks.request(key, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check()
    const fresh = value(await api.context(s.warehouseId)); check()
    if (ruleReferencesKey(fresh) !== key) throw new Error('当前身份或仓库已变化。')
    return work(check, fresh)
  })
}
function accept(s: RuleReferenceContext, id: string, r: RuleReferenceOperation, check: () => void) {
  check(); const v = r?.result
  if (r?.operationId !== id || r.warehouseId !== s.warehouseId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(r.state)
    || (r.state === 'COMMITTED' ? !v || v.operationId !== id || v.warehouseId !== s.warehouseId || !masterUuid(v.reference?.id)
      || v.reference.owner !== 'Main.Wms.LocationRuleReferences.v1' || !/^[1-9][0-9]*$/.test(v.reference.version)
      || !/^[0-9a-f]{64}$/.test(v.reference.digest) || typeof v.changed !== 'boolean' : v != null)
    || pendingRuleReferences(s) !== id) throw new Error('回执与原操作不一致，请保留记录。')
  if (r.state !== 'UNKNOWN') localStorage.removeItem(ruleReferencesKey(s)); return r
}
export function saveRuleReferences(s: RuleReferenceContext, c: Pick<RuleReferenceCommand, 'definition' | 'reason'>, guard: () => void) {
  return exclusive(s, guard, async (check, fresh) => {
    if (pendingRuleReferences(s)) throw new Error('请先查询或关闭原操作。')
    if (fresh.version !== s.version || JSON.stringify(fresh.policy?.reference) !== JSON.stringify(s.policy?.reference)) throw new Error('字典已变化，请重新载入。')
    const id = crypto.randomUUID(); localStorage.setItem(ruleReferencesKey(s), id)
    if (pendingRuleReferences(s) !== id) throw new Error('无法保存恢复记录。')
    check(); const result = value(await api.save(s.warehouseId, { ...c, operationId: id, expectedVersion: s.version }))
    return accept(s, id, { operationId: id, warehouseId: s.warehouseId, state: 'COMMITTED', result }, check)
  })
}
export function recoverRuleReferences(s: RuleReferenceContext, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    const id = pendingRuleReferences(s); if (!id) throw new Error('没有待确认的原操作。')
    return accept(s, id, value(await api.operation(s.warehouseId, id, close)), check)
  })
}
