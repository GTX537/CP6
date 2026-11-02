import { personalFormError } from './personalFormError'
import { onBeforeUnmount, ref } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { personalFormApi as api, type PersonalResult } from '@/api/oa/personalForm'

const cache = 'cp6-personal-form-pending'
export interface PersonalIntent { identity: string; commandId: string; operation: string; resource: string }
const routes: Record<string, { method: 'post' | 'put'; path: (resource: string) => string }> = {
  FORM_DRAFT_CREATE: { method: 'post', path: r => `forms/${encodeURIComponent(r)}/drafts` },
  FORM_DIRECT_SUBMIT: { method: 'post', path: r => `forms/${encodeURIComponent(r)}/submissions` },
  FORM_DRAFT_SAVE: { method: 'put', path: r => `drafts/${encodeURIComponent(r)}` },
  FORM_DRAFT_DELETE: { method: 'post', path: r => `drafts/${encodeURIComponent(r)}/delete` },
  FORM_DRAFT_RESTORE: { method: 'post', path: r => `drafts/${encodeURIComponent(r)}/restore` },
  FORM_DRAFT_REBASE: { method: 'post', path: r => `drafts/${encodeURIComponent(r)}/rebase` },
  FORM_DRAFT_SUBMIT: { method: 'post', path: r => `drafts/${encodeURIComponent(r)}/submit` },
}
// Sensitive request bodies survive SPA navigation only in memory, never browser storage.
const bodies = new Map<string, string>()
export function usePersonalCommand(clearPrivate: () => void, accepted: (result: PersonalResult, intent: PersonalIntent) => Promise<void> | void) {
  const pending = ref<PersonalIntent>(), busy = ref(false), error = ref(''), message = ref(''), storageUnavailable = ref(false)
  let live = true, generation = 0, controller: AbortController | undefined, stamp = identityStamp()
  function restore() {
    try {
      const stored = sessionStorage.getItem(cache)
      if (!stored) return
      const value = JSON.parse(stored) as PersonalIntent
      if (value.identity !== stamp) { sessionStorage.removeItem(cache); bodies.clear(); return }
      if (!routes[value.operation] || typeof value.resource !== 'string' || !/^[A-Za-z0-9_.-]{1,100}$/.test(value.commandId)) throw new Error('恢复标识损坏')
      pending.value = { identity: value.identity, commandId: value.commandId, operation: value.operation, resource: value.resource }
    } catch { storageUnavailable.value = true; error.value = '无法读取命令恢复标识，已停止发送新操作。' }
  }
  restore()
  function resetIdentity() {
    if (identityStamp() === stamp) return
    generation++; controller?.abort(); stamp = identityStamp(); pending.value = undefined; bodies.clear(); clearPrivate(); busy.value = false
    error.value = '身份已改变，请重新读取页面。'; message.value = ''; storageUnavailable.value = false; restore()
  }
  window.addEventListener(AUTH_EVENT, resetIdentity); window.addEventListener('storage', resetIdentity)
  onBeforeUnmount(() => { live = false; generation++; controller?.abort(); window.removeEventListener(AUTH_EVENT, resetIdentity); window.removeEventListener('storage', resetIdentity) })
  function forget(intent: PersonalIntent) {
    sessionStorage.removeItem(cache); bodies.delete(intent.commandId); pending.value = undefined
  }
  async function run(read: boolean, previouslyUnknown: boolean, close = false) {
    const intent = pending.value
    if (!intent || busy.value || storageUnavailable.value) return
    const epoch = generation; controller = new AbortController(); busy.value = true; error.value = ''; message.value = ''
    const check = () => { requireIdentity(intent.identity); if (!live || generation !== epoch) throw new Error('STALE') }
    try {
      const route = routes[intent.operation]!
      const raw = bodies.get(intent.commandId)
      if (!read && !close && !raw) throw new Error('页面刷新后填写内容已清除，只能查询原命令结果；请勿重复提交。')
      const result = close ? await api.close(intent.operation, intent.resource, intent.commandId, controller.signal)
        : read ? await api.original(intent.operation, intent.resource, intent.commandId, controller.signal)
        : await api.send('/oa/v2/' + route.path(intent.resource), route.method, raw!, controller.signal)
      check()
      if (result.outcome === 'NOT_OBSERVED') { message.value = '尚未观察到原命令结果。此状态不证明失败；可继续查询，或在本页保留原内容时重试原命令。'; return }
      // Keep metadata if storage cleanup fails: recovery remains the same immutable command.
      forget(intent)
      if (result.outcome === 'CLOSED') { message.value = '原命令已关闭，服务端确认没有业务效果。请重新读取后开始新操作。'; return }
      message.value = '原操作已完成。'
      await accepted(result, intent)
    } catch (e: any) {
      try {
        check()
        const status = e?.response?.status
        if ([401, 403, 404].includes(status)) { clearPrivate(); bodies.delete(intent.commandId) }
        if (!read && !previouslyUnknown && status >= 400 && status < 500 && status !== 408 && status !== 429) forget(intent)
        error.value = personalFormError(e)
      } catch { /* An obsolete identity may not disclose or erase the current identity's state. */ }
    } finally { if (live && generation === epoch) busy.value = false }
  }
  async function execute(operation: string, resource: string, expectedVersion: string | null, bodyJson: string) {
    if (pending.value || busy.value || storageUnavailable.value) return
    if (!routes[operation]) throw new Error('不支持的表单操作')
    const intent: PersonalIntent = { identity: identityStamp(), commandId: crypto.randomUUID(), operation, resource }
    const raw = `{"commandId":${JSON.stringify(intent.commandId)},"expectedVersion":${JSON.stringify(expectedVersion)},"body":${bodyJson}}`
    try { sessionStorage.setItem(cache, JSON.stringify(intent)) }
    catch { storageUnavailable.value = true; error.value = '无法保存命令恢复标识，未发送操作。'; return }
    bodies.set(intent.commandId, raw); pending.value = intent; await run(false, false)
  }
  return { pending, busy, error, message, storageUnavailable, execute, recover: () => run(true, true), retry: () => run(false, true), close: () => run(false, true, true),
    canRetry: () => !!pending.value && bodies.has(pending.value.commandId) }
}
