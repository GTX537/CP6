<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
import { formDispositionApi as api, type DispositionDetail, type DispositionOperation } from '@/api/oa/formDisposition'

interface Pending { identity: string; operationId: string; requestId: string }
const cache = 'cp6-form-disposition-pending', permission = usePermissionStore()
const reference = ref(''), detail = ref<DispositionDetail | null>(null), reason = ref(''), basis = ref('')
const busy = ref(false), error = ref(''), notice = ref(''), pending = ref<Pending>(), storageUnavailable = ref(false)
let live = true, generation = 0, identity = identityStamp(), controller: AbortController | undefined
const locked = computed(() => busy.value || !!pending.value || storageUnavailable.value)
const labels: Record<string, string> = { REQUESTED: '等待独立审批', APPROVED: '已批准，等待留存', RETAINED: '原件已登记留存', REJECTED: '已驳回', REVOKED: '处置资格已撤销', CONSUMED: '已用于重基' }
const state = (value: string) => labels[value] ?? value
const can = (action: string) => !!detail.value?.losses && permission.has(`oa-form-dispositions:${action}`)
function clearPrivate() { detail.value = null; reason.value = ''; basis.value = '' }
function restore() {
  try {
    const raw = sessionStorage.getItem(cache); if (!raw) return
    const value = JSON.parse(raw) as Pending
    if (value.identity !== identity) { sessionStorage.removeItem(cache); return }
    const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
    if (!uuid.test(value.operationId) || !uuid.test(value.requestId)) throw new Error('恢复标识损坏')
    pending.value = value; reference.value = value.requestId
  } catch { storageUnavailable.value = true; error.value = '无法读取原操作恢复标识，已停止发送新操作。' }
}
restore()
function changed() {
  if (identityStamp() === identity) return
  generation++; controller?.abort(); identity = identityStamp(); clearPrivate(); reference.value = ''; pending.value = undefined
  busy.value = false; storageUnavailable.value = false; notice.value = ''; error.value = '登录身份已变化，请重新读取。'; restore()
}
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
onBeforeUnmount(() => { live = false; generation++; controller?.abort(); clearPrivate(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
async function run(action: (check: () => void, signal: AbortSignal) => Promise<void>) {
  if (busy.value) return
  const epoch = generation, stamp = identityStamp(); controller = new AbortController(); busy.value = true; error.value = ''
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation) throw new Error('STALE') }
  try { await action(check, controller.signal) }
  catch (e: any) {
    try {
      check(); clearPrivate()
      error.value = [401, 403, 404].includes(e?.response?.status) ? '当前无权读取或处理这份处置请求，明细已清除。'
        : e?.response?.status === 409 ? '处置版本、原件、政策或保留限制已变化，请恢复原结果并重新读取。'
          : e?.message || '操作结果尚未确认，请查询原结果。'
    } catch { /* A late response cannot publish data to a different identity. */ }
  } finally { if (live && epoch === generation) busy.value = false }
}
async function load() {
  if (locked.value || !reference.value.trim()) return
  clearPrivate(); notice.value = ''; const id = reference.value.trim()
  await run(async (check, signal) => { const value = await api.read(id, signal); check(); detail.value = value; reference.value = value.request.requestId })
}
async function accept(value: DispositionOperation, check: () => void, signal: AbortSignal) {
  if (value.state === 'UNKNOWN') { notice.value = '尚未观察到原操作结果，请继续查询或明确结束原键。'; return }
  sessionStorage.removeItem(cache); pending.value = undefined; clearPrivate()
  notice.value = value.state === 'CLOSED' ? '原键已关闭，服务端确认该操作未提交。请重新读取。' : '原操作已确认提交。'
  if (value.request) { const current = await api.read(value.request.requestId, signal); check(); detail.value = current; reference.value = current.request.requestId }
}
async function write(action: string, active?: boolean) {
  const current = detail.value; if (locked.value || !current || !reason.value.trim() || !can(action === 'reject' ? 'approve' : action)) return
  if (action === 'hold' && !basis.value.trim()) return
  const intent: Pending = { identity: identityStamp(), operationId: crypto.randomUUID(), requestId: current.request.requestId }
  const input = action === 'hold' ? { operationId: intent.operationId, expectedVersion: current.request.hold?.version ?? null, active, basis: basis.value, reason: reason.value }
    : { operationId: intent.operationId, expectedVersion: current.request.version, reason: reason.value }
  try { sessionStorage.setItem(cache, JSON.stringify(intent)) }
  catch { storageUnavailable.value = true; error.value = '无法保存恢复标识，未发送操作。'; return }
  pending.value = intent; notice.value = ''
  await run(async (check, signal) => { const result = await api.write(intent.requestId, action, input, signal); check(); await accept(result, check, signal) })
}
async function recover(close: boolean) {
  const intent = pending.value; if (!intent || storageUnavailable.value) return
  await run(async (check, signal) => { const result = await api.original(intent.operationId, close, signal); check(); await accept(result, check, signal) })
}
</script>
<template>
  <main class="dispositions" aria-label="表单字段处置" :aria-busy="busy">
    <h1>表单字段处置</h1>
    <p>批准及登记留存表示同意下列准确原版本到目标版本的字段移除或类型改变。原件按政策保留，草稿本人随后可采用此处置结果重基；提交时仍核对当前资格与保留限制。</p>
    <form @submit.prevent="load"><label>处置请求引用 <input v-model="reference" aria-label="处置请求引用" :disabled="locked" required /></label><button :disabled="locked">读取请求</button></form>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending" aria-label="原操作恢复"><p>上次操作尚待确认，恢复前不能发起新处理。</p><button :disabled="busy" @click="recover(false)">查询原结果</button><button :disabled="busy" @click="recover(true)">结束未提交原键</button></section>
    <section v-if="detail" aria-label="当前处置请求">
      <h2>{{ state(detail.request.state) }}</h2>
      <dl><dt>草稿</dt><dd>{{ detail.request.draftId }}</dd><dt>原版本</dt><dd>{{ detail.request.sourceVersionId }}</dd><dt>目标版本</dt><dd>{{ detail.request.targetVersionId }}</dd></dl>
      <p v-if="detail.request.hold?.active" role="status">存在保留限制，不能批准、执行或消费留存事实。</p>
      <p v-if="detail.request.dispositionRef">留存引用：{{ detail.request.dispositionRef.requestId }} / {{ detail.request.dispositionRef.receiptVersion }}。留存至 {{ detail.request.retainUntilUtc }}。</p>
      <p v-if="detail.request.consumption">已由 {{ detail.request.consumption.actualActorId }} 在 {{ detail.request.consumption.recordedAtUtc }} 采用处置人 {{ detail.request.consumption.processorId }} 的留存结果完成重基。原命令：{{ detail.request.consumption.commandId }}。</p>
      <template v-if="detail.losses">
        <h3>本次损失明细</h3><p>保留依据：{{ detail.retentionPolicyRef }}</p>
        <table><thead><tr><th>字段位置</th><th>变化</th><th>原内容</th></tr></thead><tbody><tr v-for="loss in detail.losses" :key="loss.path"><td>{{ loss.path }}</td><td>{{ loss.kind === 'TYPE_CHANGED' ? '类型改变' : '移除' }}</td><td><pre>{{ loss.valueJson }}</pre></td></tr></tbody></table>
        <fieldset :disabled="locked"><label>处理说明 <textarea v-model="reason" aria-label="处理说明" maxlength="1000" /></label>
          <button v-if="can('approve') && detail.request.state === 'REQUESTED'" :disabled="!reason.trim() || detail.request.hold?.active" @click="write('approve')">批准处置</button>
          <button v-if="can('approve') && detail.request.state === 'REQUESTED'" :disabled="!reason.trim()" @click="write('reject')">驳回请求</button>
          <button v-if="can('retain') && detail.request.state === 'APPROVED'" :disabled="!reason.trim() || detail.request.hold?.active" @click="write('retain')">登记原件留存</button>
          <button v-if="can('revoke') && ['REQUESTED', 'APPROVED', 'RETAINED'].includes(detail.request.state)" :disabled="!reason.trim()" @click="write('revoke')">撤销处置资格</button>
          <template v-if="can('hold')"><label>保留限制依据 <input v-model="basis" aria-label="保留限制依据" maxlength="400" /></label><button :disabled="!reason.trim() || !basis.trim()" @click="write('hold', !detail.request.hold?.active)">{{ detail.request.hold?.active ? '解除保留限制' : '设置保留限制' }}</button></template>
        </fieldset>
        <h3>处理历史</h3><ol><li v-for="event in detail.history" :key="event.version">{{ state(event.state) }} · {{ event.actorId }} · {{ event.recordedAtUtc }}<p>{{ event.reason }}</p></li></ol>
      </template>
      <p v-else>你可以查看当前状态；损失明细由具备相应资格的处理人查看。</p>
    </section>
  </main>
</template>
<style scoped>
.dispositions{max-width:1100px;margin:auto;padding:24px}form,fieldset{display:flex;gap:12px;flex-wrap:wrap;align-items:end}fieldset{margin-top:20px;padding:16px}label{display:flex;flex-direction:column;gap:6px}input,textarea,button{font:inherit;padding:8px;border:1px solid #91a3b0;border-radius:4px}input{min-width:320px}textarea{min-width:420px;min-height:70px}button{cursor:pointer}button:disabled{opacity:.6;cursor:default}table{border-collapse:collapse;width:100%}th,td{padding:12px;border:1px solid #ccd5dc;text-align:left}pre{white-space:pre-wrap;overflow-wrap:anywhere;margin:0}dd,p,li{overflow-wrap:anywhere}[role=alert]{color:#a12727}dt{font-weight:600}input:focus,textarea:focus,button:focus-visible{outline:2px solid #006b8a;outline-offset:2px}@media(max-width:600px){input,textarea{min-width:0;width:100%}label{width:100%}.dispositions{padding:12px}}
</style>
