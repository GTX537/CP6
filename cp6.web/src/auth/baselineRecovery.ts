import { baselineApi, type BaselineScope, type BaselineOperation, type FreezeResult, type SuccessorResult } from '@/api/plm/engineeringBaselines'
import type { EngineeringEnvelope } from '@/api/plm/engineeringProducts'
import { identityStamp, requireIdentity } from './identity'
export interface PendingBaseline { operationId: string; action: 'FREEZE' | 'SUCCESSOR'; parentId: string; cancelOperationId?: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i, hash = /^[0-9a-f]{64}$/
function key(s: BaselineScope) {
  if (![s.environmentId, s.tenantId, s.actorId].every(x => uuid.test(x))) throw new Error('无法确认当前用户和租户。')
  return `cp6_engineering_baseline_operation_v1:${s.environmentId.toLowerCase()}:${s.tenantId.toLowerCase()}:${s.actorId.toLowerCase()}`
}
export function baselineResponse<T>(r: EngineeringEnvelope<T>) { if (r?.code !== 0 || r.data == null) throw new Error('无法确认原操作结果。'); return r.data }
export function pendingBaseline(s: BaselineScope): PendingBaseline | null {
  const raw = localStorage.getItem(key(s)); if (raw === null) return null
  const p = JSON.parse(raw) as PendingBaseline
  if (!p || !uuid.test(p.operationId) || !uuid.test(p.parentId) || !['FREEZE', 'SUCCESSOR'].includes(p.action) || p.cancelOperationId !== undefined && !uuid.test(p.cancelOperationId)) throw new Error('原操作记录异常，请保留记录。')
  return p
}
async function exclusive<T>(s: BaselineScope, guard: () => void, body: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器不能安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(s)
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一页面正在处理，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = baselineResponse(await baselineApi.scope()); check()
    if (key(current) !== channel) throw new Error('身份已改变，请重新载入。')
    return body(check)
  })
}
function native(v: any, owner: string) { return v?.owner === owner && uuid.test(v.id) && /^[1-9][0-9]*$/.test(v.version) && hash.test(v.digest) }
function accept(s: BaselineScope, p: PendingBaseline, v: BaselineOperation, check: () => void) {
  check(); const r = v?.result
  if (v?.operationId !== p.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(v.state) || (v.state === 'COMMITTED' ? !r || r.operationId !== p.operationId : r != null)) throw new Error('返回结果与原操作不一致。')
  if (v.state === 'COMMITTED') {
    if (p.action === 'FREEZE') { const f = r as FreezeResult; if (f.state !== 'ACCEPTED' || !native(f.baseline, 'Main.Plm.EngineeringBaseline.v1') || !uuid.test(f.originalOperationId) || !uuid.test(f.resultIntentId) || !hash.test(f.technicalManifestDigest)) throw new Error('冻结原结果不完整。') }
    else { const f = r as SuccessorResult; if (!native(f.parentBaseline, 'Main.Plm.EngineeringBaseline.v1') || f.parentBaseline.id !== p.parentId || !native(f.revision, 'Main.Plm.EngineeringRevision.v1') || !native(f.candidate, 'Main.Plm.EngineeringCandidateDraft.v1') || !uuid.test(f.etag)) throw new Error('后继原结果不完整。') }
  }
  const saved = pendingBaseline(s)
  if (saved?.operationId !== p.operationId || saved.action !== p.action || saved.parentId !== p.parentId) throw new Error('原操作记录已变化。')
  if (v.state !== 'UNKNOWN') localStorage.removeItem(key(s))
  return v
}
export function saveBaseline(s: BaselineScope, action: PendingBaseline['action'], parentId: string, guard: () => void, write: (operationId: string) => Promise<EngineeringEnvelope<FreezeResult | SuccessorResult>>) {
  return exclusive(s, guard, async check => {
    if (pendingBaseline(s)) throw new Error('请先处理原操作。')
    if (!uuid.test(parentId)) throw new Error('请选择准确候选或基线。')
    const p: PendingBaseline = { operationId: crypto.randomUUID(), action, parentId }
    localStorage.setItem(key(s), JSON.stringify(p)); if (pendingBaseline(s)?.operationId !== p.operationId) throw new Error('无法保存恢复记录。')
    check(); const r = baselineResponse(await write(p.operationId)); return accept(s, p, { operationId: p.operationId, state: 'COMMITTED', result: r }, check)
  })
}
export function resolveBaseline(s: BaselineScope, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    let p = pendingBaseline(s); if (!p) throw new Error('没有待确认原操作。')
    if (close && p.action === 'FREEZE') {
      p = { ...p, cancelOperationId: p.cancelOperationId ?? crypto.randomUUID() }; localStorage.setItem(key(s), JSON.stringify(p)); check()
      const r = baselineResponse(await baselineApi.cancel(p.cancelOperationId!, p.operationId)); check()
      if (r.operationId !== p.cancelOperationId || r.targetOperationId !== p.operationId) throw new Error('取消回执与原操作不一致。')
      return accept(s, p, r.outcome, check)
    }
    return accept(s, p, baselineResponse(await baselineApi.operation(p.operationId, p.action, close)), check)
  })
}
