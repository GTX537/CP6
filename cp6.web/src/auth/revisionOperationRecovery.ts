import { revisionApi, type RevisionEnvelope, type RevisionOperation, type RevisionOperationKind, type RevisionResult, type RevisionScope } from '@/api/plm/engineeringRevisions'
import { identityStamp, requireIdentity } from './identity'

export interface PendingRevisionOperation { operationId: string; kind: RevisionOperationKind; objectId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function key(scope: RevisionScope, kind: RevisionOperationKind) {
  if (!uuid.test(scope.tenantId) || !uuid.test(scope.actorId) || !['ENGINEERING_REVISION'].includes(kind)) throw new Error('无法确认当前用户和租户。')
  return `cp6_revision_operation_v1:${scope.tenantId.toLowerCase()}:${scope.actorId.toLowerCase()}:${kind}`
}
export function revisionResponse<T>(response: RevisionEnvelope<T>): T {
  if (response?.code !== 0 || response.data == null) throw new Error('无法确认操作结果。')
  return response.data
}
export function pendingRevisionOperation(scope: RevisionScope, kind: RevisionOperationKind): PendingRevisionOperation | null {
  const text = localStorage.getItem(key(scope, kind)); if (text === null) return null
  const value = JSON.parse(text) as PendingRevisionOperation
  if (!value || !uuid.test(value.operationId) || value.kind !== kind || !uuid.test(value.objectId)) throw new Error('原操作记录异常，请保留记录并联系管理员。')
  return { operationId: value.operationId, kind, objectId: value.objectId }
}
async function exclusive<T>(scope: RevisionScope, kind: RevisionOperationKind, guard: () => void, action: (check: () => void) => Promise<T>) {
  if (!navigator.locks?.request) throw new Error('此浏览器无法安全协调多个页面，暂不能提交。')
  const stamp = identityStamp(), channel = key(scope, kind), api = revisionApi
  return navigator.locks.request(channel, { mode: 'exclusive', ifAvailable: true }, async lock => {
    if (!lock) throw new Error('另一个页面正在处理该操作，请稍后查询原操作。')
    const check = () => { requireIdentity(stamp); guard() }
    check(); const current = revisionResponse(await api.scope()); check()
    if (key(current, kind) !== channel) throw new Error('当前身份已变化，请重新载入。')
    return action(check)
  })
}
function accept(scope: RevisionScope, original: PendingRevisionOperation, value: RevisionOperation, check: () => void) {
  check()
  const receipt = value?.result
  if (value?.operationId !== original.operationId || !['COMMITTED', 'CLOSED', 'UNKNOWN'].includes(value.state)
    || (value.state === 'COMMITTED' ? !receipt || receipt.operationId !== original.operationId || receipt.kind !== original.kind
      || receipt.objectId !== original.objectId || !/^[1-9][0-9]*$/.test(receipt.version) || !/^[0-9a-f]{64}$/.test(receipt.digest) : receipt != null))
    throw new Error('返回结果与原操作不一致，请保留记录。')
  const pending = pendingRevisionOperation(scope, original.kind)
  if (pending?.operationId !== original.operationId || pending.objectId !== original.objectId) throw new Error('原操作记录已变化。')
  if (value.state !== 'UNKNOWN') localStorage.removeItem(key(scope, original.kind))
  return value
}
export function saveRevisionOperation(scope: RevisionScope, kind: RevisionOperationKind, objectId: string, guard: () => void,
  write: (id: string) => Promise<RevisionEnvelope<RevisionResult>>) {
  return exclusive(scope, kind, guard, async check => {
    if (!uuid.test(objectId)) throw new Error('请先选择当前对象。')
    if (pendingRevisionOperation(scope, kind)) throw new Error('请先查询或结束原操作。')
    const original: PendingRevisionOperation = { operationId: crypto.randomUUID(), kind, objectId }
    localStorage.setItem(key(scope, kind), JSON.stringify(original))
    if (pendingRevisionOperation(scope, kind)?.operationId !== original.operationId) throw new Error('无法保存恢复记录。')
    check(); const result = revisionResponse(await write(original.operationId))
    return accept(scope, original, { operationId: original.operationId, state: 'COMMITTED', result }, check)
  })
}
export function resolveRevisionOperation(scope: RevisionScope, kind: RevisionOperationKind, close: boolean, guard: () => void) {
  return exclusive(scope, kind, guard, async check => {
    const original = pendingRevisionOperation(scope, kind); if (!original) throw new Error('没有待确认的原操作。')
    const api = revisionApi
    return accept(scope, original, revisionResponse(await api.operation(original.operationId, close)), check)
  })
}
