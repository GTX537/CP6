<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import type { AuthoringView } from '@/api/oa/flowAuthoring'
import { flowActivationApi as api, type ActivationHistory, type ActivationIntent, type ActivationPreview, type ActivationResult } from '@/api/oa/flowActivation'
const rows = ref<AuthoringView[]>([]), preview = ref<ActivationPreview>(), history = ref<ActivationHistory[]>([])
const pending = ref<ActivationIntent>(), busy = ref(false), error = ref(''), message = ref(''), reason = ref(''), selected = ref('')
const page = ref(1), versionPage = ref(1), historyPage = ref(1)
const locked = computed(() => busy.value || !!pending.value)
const cache = 'cp6-workflow-activation-pending'
let live = true, generation = 0, controller: AbortController | undefined
function remember() {
  if (pending.value) sessionStorage.setItem(cache, JSON.stringify({ identity: identityStamp(), pending: pending.value }))
  else sessionStorage.removeItem(cache)
}
function clear() {
  generation++; controller?.abort(); rows.value = []; preview.value = undefined; history.value = []; pending.value = undefined
  reason.value = ''; selected.value = ''; error.value = ''; message.value = ''; busy.value = false; page.value = versionPage.value = historyPage.value = 1; remember()
}
async function run(action: (check: () => void, signal: AbortSignal) => Promise<void>) {
  if (busy.value) return
  const identity = identityStamp(), request = generation; controller = new AbortController(); busy.value = true; error.value = ''
  const check = () => { requireIdentity(identity); if (!live || generation !== request) throw new Error('STALE') }
  try { await action(check, controller.signal); check() }
  catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if (status === 401 || status === 403) { clear(); error.value = '当前权限不允许操作，已清除页面内容。' }
      else if ([400, 404, 409, 412, 422].includes(status)) {
        pending.value = undefined; preview.value = undefined; history.value = []; selected.value = ''; remember(); error.value = '当前版本、权限或依赖不满足要求，请重新读取后选择。'
      } else error.value = pending.value ? '提交结果尚未确认，请查询原结果或以原意图重试。' : '暂时无法读取启停配置。'
    } catch { /* An obsolete identity cannot update this view. */ }
  } finally { if (live && generation === request) busy.value = false }
}
function list() {
  if (locked.value) return
  return run(async (check, signal) => { const result = await api.list(page.value, signal); check(); rows.value = result })
}
function open(key: string, nextPage = 1) {
  if (locked.value) return
  return run(async (check, signal) => {
    const result = await api.preview(key, nextPage, signal); check(); preview.value = result; versionPage.value = nextPage
    selected.value = ''; history.value = []; historyPage.value = 1; reason.value = ''; message.value = ''
  })
}
function readHistory(nextPage = 1) {
  if (locked.value || !preview.value) return
  const key = preview.value.current.key
  return run(async (check, signal) => { const result = await api.history(key, nextPage, signal); check(); history.value = result; historyPage.value = nextPage })
}
function accept(result: ActivationResult, check: () => void) {
  const intent = pending.value, value = result.result
  if (!intent || result.commandId !== intent.command.commandId || result.outcome !== 'SUCCEEDED' || result.resourceId !== intent.definitionId || !value
    || value.definitionId !== intent.definitionId || value.key !== intent.key || value.revision !== result.resourceVersion
    || value.enabled !== intent.command.body.enabled || value.publishedVersionId !== intent.command.body.publishedVersionId || value.inflightUnchanged !== true) throw new Error('原启停结果不匹配')
  check(); pending.value = undefined; remember(); preview.value = undefined; selected.value = ''; history.value = []; rows.value = []
  message.value = '原启停命令已确认，在途实例保持不变。请重新读取当前配置；后续命令可能已改变它。'
}
function retry() {
  if (!pending.value) return
  return run(async (check, signal) => { const result = await api.execute(JSON.parse(JSON.stringify(pending.value)), signal); check(); accept(result, check) })
}
function change(enabled: boolean) {
  if (locked.value || !preview.value || !reason.value.trim()) return
  const current = preview.value.current, version = enabled ? selected.value : current.publishedVersionId
  if (!version || enabled && !preview.value.versions.some(v => v.versionId === version)) return
  pending.value = { key: current.key, definitionId: current.definitionId, command: { commandId: crypto.randomUUID(), expectedVersion: current.revision,
    body: { enabled, expectedActivationRevision: current.revision, publishedVersionId: version, reason: reason.value } } }; remember(); return retry()
}
function recover() {
  if (!pending.value) return
  return run(async (check, signal) => {
    const result = await api.original(pending.value!, signal); check()
    if (result.outcome === 'SUCCEEDED') accept(result, check)
    else message.value = '尚未观察到原结果，不能据此判断未提交。请继续查询或以原意图重试。'
  })
}
function identityChanged() { clear(); error.value = '登录身份已变化，请重新读取。' }
onMounted(() => {
  try {
    const saved = JSON.parse(sessionStorage.getItem(cache) ?? 'null'), intent = saved?.pending
    if (saved?.identity === identityStamp() && typeof intent?.key === 'string' && typeof intent.definitionId === 'string' && typeof intent.command?.commandId === 'string'
      && typeof intent.command.body?.enabled === 'boolean' && intent.command.body.expectedActivationRevision === intent.command.expectedVersion && typeof intent.command.body.publishedVersionId === 'string') {
      pending.value = intent; message.value = '有一次启停命令待确认，请先查询原结果。'; return
    }
  } catch { /* Invalid cached input is never submitted. */ }
  sessionStorage.removeItem(cache)
})
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; generation++; controller?.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<template>
  <section aria-label="准确版本启停" class="activation">
    <h3>准确版本启停</h3>
    <p>管理新发布流程的启停配置。运行入口尚未接入，此处启用不代表已可发起审批；在途数量暂不可用。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <button :disabled="locked" @click="list">读取流程</button>
    <template v-if="pending"><button :disabled="busy" @click="recover">查询原启停结果</button><button :disabled="busy" @click="retry">以原意图重试</button></template>
    <ul><li v-for="row in rows" :key="row.definitionId"><button :disabled="locked" @click="open(row.key)">{{ row.fields.Name ?? '名称不可见' }} · {{ row.key }}</button></li></ul>
    <div v-if="rows.length || page > 1"><button :disabled="locked || page === 1" @click="page--; list()">上一页流程</button><span>{{ page }}</span><button :disabled="locked || rows.length < 100" @click="page++; list()">下一页流程</button></div>
    <div v-if="preview">
      <h4>{{ preview.current.key }}</h4><p>配置：{{ preview.current.enabled ? '已启用' : '已停用' }} · 所选发布版本 {{ preview.current.publishedVersionId ?? '尚未选择' }}</p>
      <label>发布版本<select v-model="selected" :disabled="locked"><option value="">请明确选择一个版本</option><option v-for="v in preview.versions" :key="v.versionId" :value="v.versionId">版本 {{ v.number }} · {{ v.name ?? '名称不可见' }}</option></select></label>
      <div v-if="preview.versions.length || versionPage > 1"><button :disabled="locked || versionPage === 1" @click="open(preview.current.key, versionPage - 1)">上一页版本</button><span>{{ versionPage }}</span><button :disabled="locked || preview.versions.length < 100" @click="open(preview.current.key, versionPage + 1)">下一页版本</button></div>
      <label>启停原因<textarea v-model="reason" maxlength="500" :disabled="locked" /></label>
      <button :disabled="locked || !selected || !reason.trim()" @click="change(true)">启用所选版本</button>
      <button :disabled="locked || !preview.current.enabled || !reason.trim()" @click="change(false)">停用当前配置</button>
      <button :disabled="locked" @click="readHistory()">读取启停历史</button>
      <ol><li v-for="h in history" :key="h.revision">{{ h.enabled ? '启用' : '停用' }} · {{ h.publishedVersionId }} · {{ new Date(h.changedAtUtc).toLocaleString() }} · {{ h.reason }}</li></ol>
      <div v-if="history.length || historyPage > 1"><button :disabled="locked || historyPage === 1" @click="readHistory(historyPage - 1)">上一页历史</button><span>{{ historyPage }}</span><button :disabled="locked || history.length < 100" @click="readHistory(historyPage + 1)">下一页历史</button></div>
    </div>
  </section>
</template>
<style scoped>
.activation { padding: 16px; border: 1px solid #dbe3ed; border-radius: 8px; background: white; } button { margin: 4px; padding: 8px 12px; } label { display: grid; max-width: 680px; gap: 6px; margin: 12px 0; } textarea { min-height: 70px; } [role=alert] { color: #a63727; } li { margin: 8px 0; }
</style>
