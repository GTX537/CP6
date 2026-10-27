<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import type { AuthoringView } from '@/api/oa/flowAuthoring'
import { flowPublicationApi as api, type LifecycleIntent, type LifecycleResult, type PublicationPreview, type PublishedFlow } from '@/api/oa/flowPublication'
const props = defineProps<{ current?: AuthoringView; dirty: boolean; disabled: boolean }>()
const emit = defineEmits<{ reload: [key: string]; locked: [value: boolean]; privateClear: [] }>()
const preview = ref<PublicationPreview>(), versions = ref<PublishedFlow[]>([]), selected = ref<PublishedFlow>()
const pending = ref<LifecycleIntent>(), busy = ref(false), error = ref(''), message = ref(''), comment = ref(''), page = ref(1)
const cache = 'cp6-workflow-publication-pending'
let generation = 0, live = true, controller: AbortController | undefined
const unavailable = computed(() => busy.value || !!pending.value || props.disabled || props.dirty || !props.current)
const matchingPreview = computed(() => preview.value?.expectedVersion === props.current?.version && preview.value?.draftVersionId === props.current?.draftId)
function remember() {
  if (pending.value) sessionStorage.setItem(cache, JSON.stringify({ identity: identityStamp(), pending: pending.value }))
  else sessionStorage.removeItem(cache)
}
function clear() {
  generation++; controller?.abort(); preview.value = undefined; versions.value = []; selected.value = undefined
  pending.value = undefined; busy.value = false; comment.value = ''; error.value = ''; message.value = ''; remember(); emit('locked', false)
}
watch(() => [props.current?.key, props.current?.version], () => {
  preview.value = undefined; versions.value = []; selected.value = undefined; page.value = 1
})
watch(() => props.dirty, dirty => { if (dirty) preview.value = undefined })
async function run(action: (check: () => void, signal: AbortSignal) => Promise<void>) {
  if (busy.value) return
  const identity = identityStamp(), request = generation; controller = new AbortController(); busy.value = true; error.value = ''; emit('locked', true)
  const check = () => { requireIdentity(identity); if (!live || generation !== request) throw new Error('STALE') }
  try { await action(check, controller.signal); check() }
  catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if (status === 401 || status === 403) { clear(); emit('privateClear'); error.value = '当前权限不允许此操作，已清除页面内容。' }
      else if ([400, 404, 409, 412, 422].includes(status)) {
        pending.value = undefined; preview.value = undefined; remember(); error.value = '操作未被接受。请重新读取当前版本并重新预览。'
      } else error.value = pending.value ? '结果尚未确认，请查询原结果或以原意图重试。' : '当前无法读取发布信息，请稍后重试。'
    } catch { /* Ignore responses belonging to another identity. */ }
  } finally { if (live && generation === request) { busy.value = false; emit('locked', !!pending.value) } }
}
function prepare() {
  if (unavailable.value || props.current?.published) return
  const current = props.current!
  return run(async (check, signal) => { const result = await api.preview(current, signal); check(); preview.value = result; message.value = '' })
}
function history() {
  if (unavailable.value) return
  const key = props.current!.key
  return run(async (check, signal) => { const result = await api.versions(key, page.value, signal); check(); versions.value = result })
}
async function accept(result: LifecycleResult, check: () => void) {
  const intent = pending.value
  if (!intent || result.commandId !== intent.command.commandId || result.outcome !== 'SUCCEEDED' || result.resourceId !== intent.definitionId || !result.resourceVersion || !result.result) throw new Error('原结果不匹配')
  const publication = result.result.publication, draft = result.result.draft
  if (intent.operation === 'FLOW_PUBLISH' && (!publication || publication.key !== intent.key || publication.definitionId !== intent.definitionId || publication.versionId !== intent.command.body.draftVersionId || publication.activationChanged !== false)) throw new Error('发布结果不匹配')
  if (intent.operation === 'FLOW_CREATE_DRAFT' && (!draft || draft.key !== intent.key || draft.definitionId !== intent.definitionId || draft.version !== result.resourceVersion)) throw new Error('草稿结果不匹配')
  check(); pending.value = undefined; remember(); preview.value = undefined
  message.value = publication ? '发布已确认。当前启停配置保持不变。' : '下一草稿已创建，原发布版本保持不变。'
  emit('locked', false); emit('reload', intent.key)
}
function retry() {
  if (!pending.value) return
  return run(async (check, signal) => { const result = await api.execute(JSON.parse(JSON.stringify(pending.value)), signal); check(); await accept(result, check) })
}
function publish() {
  if (unavailable.value || !matchingPreview.value || !preview.value?.canPublish || Date.parse(preview.value.expiresAtUtc) <= Date.now()) return
  const current = props.current!, p = preview.value
  pending.value = { key: current.key, definitionId: current.definitionId, operation: 'FLOW_PUBLISH', command: { commandId: crypto.randomUUID(), expectedVersion: current.version,
    body: { draftVersionId: current.draftId, previewId: p.id, previewDigest: p.digest, expectedVersion: current.version, comment: comment.value } } }; remember(); return retry()
}
function next(version: PublishedFlow) {
  if (unavailable.value || !props.current?.published || version.definitionId !== props.current.definitionId) return
  const current = props.current
  pending.value = { key: current.key, definitionId: current.definitionId, operation: 'FLOW_CREATE_DRAFT', command: { commandId: crypto.randomUUID(), expectedVersion: current.version, body: { sourceVersionId: version.versionId } } }
  remember(); return retry()
}
function recover() {
  if (!pending.value) return
  return run(async (check, signal) => {
    const result = await api.original(pending.value!, signal); check()
    if (result.outcome === 'SUCCEEDED') await accept(result, check)
    else message.value = '尚未观察到原结果，不能据此判断操作未提交。请继续查询或以原意图重试。'
  })
}
function identityChanged() { clear(); error.value = '登录身份已变化，请重新读取。' }
onMounted(() => {
  try {
    const saved = JSON.parse(sessionStorage.getItem(cache) ?? 'null')
    if (saved?.identity === identityStamp() && ['FLOW_PUBLISH', 'FLOW_CREATE_DRAFT'].includes(saved.pending?.operation)
      && typeof saved.pending?.key === 'string' && typeof saved.pending?.definitionId === 'string' && saved.pending.command?.commandId) {
      pending.value = saved.pending; emit('locked', true); message.value = '有一次发布或创建草稿尚待确认，请先查询原结果。'; return
    }
  } catch { /* Invalid cached intent cannot be submitted. */ }
  sessionStorage.removeItem(cache)
})
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; generation++; controller?.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<template>
  <section aria-label="发布与版本" class="publication">
    <h3>发布与版本</h3>
    <p v-if="dirty">有尚未保存的修改，请先保存草稿再预览发布。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <div class="actions">
      <button :disabled="unavailable || current?.published" @click="prepare">预览发布</button>
      <button :disabled="unavailable" @click="history">读取历史版本</button>
      <template v-if="pending"><button :disabled="busy" @click="recover">查询原发布或草稿结果</button><button :disabled="busy" @click="retry">以原意图重试</button></template>
    </div>
    <div v-if="preview && matchingPreview">
      <p>预览有效至 {{ new Date(preview.expiresAtUtc).toLocaleString() }}。确认发布将冻结当前已保存版本，当前启停配置保持不变。</p>
      <p v-if="!preview.canPublish">当前草稿或依赖尚不满足发布条件：{{ preview.diagnostics.join('、') }}</p>
      <ul v-if="preview.dependencies.length"><li v-for="d in preview.dependencies" :key="d.location + d.kind">{{ d.kind }} · {{ d.reference }} · {{ d.evidenceState === 'UNPROVEN' ? '来源待核定' : '已登记引用' }}</li></ul>
      <label>发布说明<textarea v-model="comment" maxlength="1000" :disabled="busy || !!pending" /></label>
      <button :disabled="unavailable || !preview.canPublish" @click="publish">确认发布此版本</button>
    </div>
    <ol v-if="versions.length"><li v-for="version in versions" :key="version.versionId">
      <button :disabled="busy || !!pending" @click="selected = version">版本 {{ version.version }} · {{ version.fields.Name ?? '名称不可见' }}</button>
      <span>发布于 {{ new Date(version.publishedAtUtc).toLocaleString() }}</span>
      <button :disabled="unavailable || !current?.published" @click="next(version)">从此版本创建下一草稿</button>
    </li></ol>
    <div v-if="versions.length || page > 1"><button :disabled="unavailable || page === 1" @click="page--; history()">上一页版本</button><span>{{ page }}</span><button :disabled="unavailable || versions.length < 100" @click="page++; history()">下一页版本</button></div>
    <div v-if="selected"><h4>版本 {{ selected.version }}（只读）</h4><pre v-if="selected.fields.SchemaJson">{{ selected.fields.SchemaJson }}</pre><p v-else>当前无权读取流程结构。</p></div>
  </section>
</template>
<style scoped>
.publication { margin: 16px 0; padding: 16px; border: 1px solid #dbe3ed; border-radius: 8px; background: white; } .actions { display: flex; flex-wrap: wrap; gap: 8px; } button { padding: 8px 12px; margin: 4px; border: 1px solid #ccd6e3; border-radius: 6px; background: white; cursor: pointer; } button:disabled { opacity: .5; cursor: default; } label { display: grid; gap: 6px; } textarea { min-height: 70px; max-width: 640px; } pre { max-height: 320px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; } [role=alert] { color: #a63727; } li { margin: 10px 0; }
</style>
