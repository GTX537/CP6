import { locationControlApi as api, type ControlScope, type ControlView, type ControlOperation, type ControlResult } from '@/api/wms/locationControl'
import { identityStamp, requireIdentity } from './identity'
import { transferResponse as value } from './transferEnvironmentRecovery'
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function controlKey(s: ControlScope) {
  if (![s?.tenantId, s?.actorId, s?.locationId].every(x => uuid.test(x))) throw new Error('当前身份或库位不一致。')
  return `cp6_location_control_v1:${s.tenantId}:${s.actorId}:${s.locationId}`.toLowerCase()
}
export function pendingControl(s: ControlScope) {
  const id = localStorage.getItem(controlKey(s)); if (id !== null && !uuid.test(id)) throw new Error('原操作记录异常，请保留记录。'); return id
}
export function controlView(v: ControlView, s?: ControlScope) {
  const key = controlKey(v)
  if (s && key !== controlKey(s) || v.qualification !== 'NATIVE_LOCAL_CONTROL_ONLY' || v.admissionGrant !== false
    || !/^"lc1\.[A-Za-z0-9+/=]+\.[1-9][0-9]*"$/.test(v.etag) || !/^[1-9][0-9]*$/.test(v.controlRevision)
    || !Array.isArray(v.entries) || !Number.isInteger(v.openBlocks) || v.openBlocks < 0) throw new Error('库位控制响应不一致。')
  return v
}
async function exclusive<T>(s: ControlScope, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法协调多个页面，暂不能提交。')
  const stamp = identityStamp()
  return navigator.locks.request(controlKey(s), { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理，请查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }; check(); return action(check)
  })
}
function accept(s: ControlScope, id: string, v: ControlOperation, check: () => void) {
  check(); const r = v?.result
  if (v?.operationId !== id || v.locationId !== s.locationId || !['COMMITTED', 'UNKNOWN', 'CLOSED'].includes(v.state)
    || (v.state === 'COMMITTED' ? !r || r.operationId !== id || r.locationId !== s.locationId || !uuid.test(r.blockId)
      || r.reference?.owner !== 'Main.Wms.LocationOperationalControl.v1' || !uuid.test(r.reference.id) || r.reference.version !== '1'
      || !/^[0-9a-f]{64}$/.test(r.reference.digest) || !['BLOCK', 'RELEASE_DECISION', 'RELEASE'].includes(r.effect)
      || !/^"lc1\.[A-Za-z0-9+/=]+\.[1-9][0-9]*"$/.test(r.etag) || !/^[1-9][0-9]*$/.test(r.controlRevision) : r != null)
    || pendingControl(s) !== id) throw new Error('原操作结果不一致，请保留恢复记录。')
  if (v.state !== 'UNKNOWN') localStorage.removeItem(controlKey(s)); return v
}
export function sendControl(s: ControlView, route: 'blocks' | 'release-decisions' | 'releases', body: object, guard: () => void) {
  return exclusive(s, guard, async check => {
    if (pendingControl(s)) throw new Error('请先查询或关闭原操作。')
    const current = controlView(value(await api.read(s.locationId)), s); check()
    if (current.etag !== s.etag) throw new Error('库位控制已变化，请重新载入。')
    const id = crypto.randomUUID(); localStorage.setItem(controlKey(s), id)
    if (pendingControl(s) !== id) throw new Error('无法保存恢复记录。')
    check(); const result: ControlResult = value(await api.command(s.locationId, route, s.etag, { ...body, operationId: id }))
    const expected = route === 'blocks' ? 'BLOCK' : route === 'release-decisions' ? 'RELEASE_DECISION' : 'RELEASE'
    if (result.effect !== expected || route !== 'blocks' && result.blockId !== (body as { blockId: string }).blockId) throw new Error('操作结果类型或封锁身份不一致，请查询原操作。')
    return accept(s, id, { operationId: id, locationId: s.locationId, state: 'COMMITTED', result }, check)
  })
}
export function recoverControl(s: ControlScope, close: boolean, guard: () => void) {
  return exclusive(s, guard, async check => {
    controlView(value(await api.read(s.locationId)), s); check()
    const id = pendingControl(s); if (!id) throw new Error('没有待确认的原操作。')
    return accept(s, id, value(await api.operation(s.locationId, id, close)), check)
  })
}
