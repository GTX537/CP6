import { computed, onBeforeUnmount, ref } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { getActingAs } from '@/stores/oaActingAs'
import { nativeTaskOperationsApi as api, type OperationIntent, type OperationResult, type TaskOperation } from '@/api/oa/nativeTaskOperations'
const operations: TaskOperation[] = ['OA_TASK_TRANSFER', 'OA_TASK_ADDSIGN', 'OA_TASK_SENDBACK', 'OA_APPROVER_RESOLVE', 'SFS_RETURNED_EDIT', 'SFS_RETURNED_RESUBMIT']
export function useNativeOperation(cache: string, clear: () => void, completed: (r: OperationResult) => void, denied: () => void) {
  const pending = ref<OperationIntent>(), busy = ref(false), blocked = ref(false), error = ref(''), message = ref(''), canRetry = ref(false)
  const stamp = () => JSON.stringify([identityStamp(), getActingAs()?.userId ?? null])
  let live = true, epoch = 0, identity = stamp(), raw: string | undefined
  const locked = computed(() => busy.value || blocked.value || !!pending.value)
  try {
    const saved = sessionStorage.getItem(cache)
    if (saved) {
      const value = JSON.parse(saved) as OperationIntent
      if (value.identity !== identity) sessionStorage.removeItem(cache)
      else if (!operations.includes(value.operation) || !/^[0-9a-f-]{36}$/i.test(value.resource) || !/^[A-Za-z0-9_.-]{1,100}$/.test(value.command)) throw new Error('INVALID_RECOVERY')
      else pending.value = value
    }
  } catch { blocked.value = true; error.value = '无法读取恢复标识，已停止新操作。' }
  function changed() {
    if (identity === stamp()) return
    identity = stamp(); epoch++; clear(); pending.value = undefined; raw = undefined; canRetry.value = false; busy.value = false; message.value = ''
    error.value = '身份已改变，请重新读取当前条件。'
    try { sessionStorage.removeItem(cache); blocked.value = false } catch { blocked.value = true }
  }
  function capture() {
    changed(); const current = epoch, actor = stamp()
    return () => { if (!live || current !== epoch || actor !== stamp()) throw new Error('STALE') }
  }
  function failed(e: any) {
    if ([401, 403, 404].includes(e?.response?.status)) { clear(); raw = undefined; canRetry.value = false; denied() }
    error.value = [401, 403, 404].includes(e?.response?.status) ? '当前权限已不能办理，先前内容已清除。'
      : [409, 412].includes(e?.response?.status) ? '任务或表单已改变，请确认原命令结果后重新读取。'
        : e?.response?.status === 422 ? '当前填写不符合要求，请检查必填内容、原因和目标人员。'
          : pending.value ? '操作结果尚未确认，请查询原结果；不要另发新命令。' : '暂时无法读取当前条件，请重试。'
  }
  async function recover(action: 'send' | 'original' | 'close', first = false) {
    if (!pending.value || busy.value || blocked.value) return
    const intent = { ...pending.value }, check = capture(); busy.value = true; error.value = ''; message.value = ''
    try {
      if (intent.identity !== stamp()) throw new Error('STALE')
      if (action === 'send' && !raw) { message.value = '填写内容未保存在浏览器，请查询或关闭原命令。'; return }
      const value = await (action === 'send' ? api.send(intent, raw!) : action === 'original' ? api.original(intent) : api.close(intent)); check()
      if (!value || value.outcome === 'NOT_OBSERVED') { message.value = '尚未观察到原结果，这不表示未执行；请继续查询或关闭原命令。'; return }
      if (value.commandId !== intent.command || !['SUCCEEDED', 'CLOSED'].includes(value.outcome)) throw new Error('RESULT_MISMATCH')
      sessionStorage.removeItem(cache); pending.value = undefined; raw = undefined; canRetry.value = false; clear()
      message.value = value.outcome === 'CLOSED' ? '原命令已关闭，没有产生操作效果。' : '原操作已确认，请读取最新状态。'; completed(value)
    } catch (e: any) {
      try {
        check(); failed(e); const status = e?.response?.status
        if (first && status >= 400 && status < 500 && ![408, 429].includes(status)) {
          sessionStorage.removeItem(cache); pending.value = undefined; raw = undefined; canRetry.value = false
        }
      } catch { /* An obsolete identity cannot display or discard the current receipt. */ }
    } finally { try { check(); busy.value = false } catch { /* stale */ } }
  }
  async function begin(operation: TaskOperation, resource: string, expectedVersion: string, bodyJson: string) {
    if (locked.value) return
    const command = crypto.randomUUID(), intent = { identity: stamp(), operation, resource, command }
    try { sessionStorage.setItem(cache, JSON.stringify(intent)) }
    catch { blocked.value = true; error.value = '无法保存恢复标识，本次请求未发送。'; return }
    raw = `{"commandId":${JSON.stringify(command)},"expectedVersion":${JSON.stringify(expectedVersion)},"body":${bodyJson}}`
    pending.value = intent; canRetry.value = true; await recover('send', true)
  }
  window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
  onBeforeUnmount(() => { live = false; epoch++; raw = undefined; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
  return { pending, busy, blocked, locked, error, message, canRetry, capture, failed, begin, recover }
}
