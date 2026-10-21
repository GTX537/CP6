import { onBeforeUnmount, ref } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'

/** UI isolation and duplicate submission control; service Owner remains authoritative. */
export function useOperationsIntent(clear: () => void) {
  const busy = ref(false), uncertain = ref(false), error = ref(''), epoch = ref(0)
  let identity = identityStamp(), alive = true
  const capture = () => ({ epoch: epoch.value, identity: identityStamp() })
  const current = (token: ReturnType<typeof capture>) => alive && token.epoch === epoch.value && token.identity === identityStamp()
  function invalidate() { epoch.value++; busy.value = false; uncertain.value = false; error.value = '' }
  function identityChanged() {
    if (identity === identityStamp()) return
    identity = identityStamp(); invalidate(); clear()
  }
  function begin() {
    identityChanged()
    if (busy.value || uncertain.value) return
    busy.value = true; error.value = ''; return capture()
  }
  function failed(token: ReturnType<typeof capture>, failure: any) {
    if (!current(token) || failure === 'cancel' || failure === 'close') return
    const status = failure?.response?.status
    uncertain.value = !status || status >= 500 || String(failure?.response?.data?.message).includes('OUTCOME_UNKNOWN')
    error.value = uncertain.value
      ? '処理結果を確認できません。再送せず、単票または一覧で結果を確認してください。'
      : '操作は完了しませんでした。権限と対象の最新状態を確認してください。'
  }
  function finish(token: ReturnType<typeof capture>) { if (current(token)) busy.value = false }
  window.addEventListener(AUTH_EVENT, identityChanged)
  window.addEventListener('storage', identityChanged)
  onBeforeUnmount(() => {
    alive = false; invalidate()
    window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged)
  })
  return { busy, uncertain, error, epoch, capture, current, invalidate, begin, failed, finish }
}
