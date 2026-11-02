<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { AUTH_EPOCH, AUTH_EVENT, AUTH_RECORD, IMPERSONATION, identityStamp, requireIdentity } from '@/auth/identity'
import { formAuthoringApi as api, type FormAuthoringCommand, type FormAuthoringEntry, type FormAuthoringField, type FormAuthoringResult, type FormAuthoringView } from '@/api/oa/formAuthoring'
import FormDesigner from '@/views/wf/designer/FormDesigner.vue'

const emit = defineEmits<{ close: [] }>()
const cache = 'cp6-form-authoring-pending', initial = '{"fields":[],"rules":[]}'
const allFields: FormAuthoringField[] = ['Name', 'SchemaJson', 'Category', 'SubCategory']
const labels = { Name: '表单名称', SchemaJson: '表单结构', Category: '分类', SubCategory: '子分类' }
const entry = ref<FormAuthoringEntry>(), items = ref<FormAuthoringView[]>([]), current = ref<FormAuthoringView>()
const key = ref(''), fields = ref<Partial<Record<FormAuthoringField, string | null>>>({})
const editorSource = ref<{ schemaJson: string; version: string }>()
const busy = ref(false), error = ref(''), message = ref(''), stale = ref(false), page = ref(1)
type Pending = { key: string; command: FormAuthoringCommand; unknown: boolean }
const pending = ref<Pending>()
const writable = computed(() => current.value?.writableFields ?? entry.value?.writableFields ?? [])
const readable = computed(() => allFields.every(f => Object.hasOwn(fields.value, f)))
const canSave = computed(() => !stale.value && readable.value && writable.value.length > 0 && (!!current.value || !!entry.value?.canCreate))
const dirty = computed(() => current.value && allFields.some(f => fields.value[f] !== current.value?.fields[f]))
let generation = 0, live = true, controller: AbortController | undefined
class CommandStorageError extends Error {}
function storageChanged(event: StorageEvent) {
  if (event.key === null || [AUTH_EPOCH, AUTH_RECORD, IMPERSONATION, 'cp6_authed', 'userName'].includes(event.key)) identityChanged()
}
function remember() {
  if (pending.value) sessionStorage.setItem(cache, JSON.stringify({ identity: identityStamp(), pending: pending.value }))
  else sessionStorage.removeItem(cache)
}
function clearFields() {
  entry.value = undefined; items.value = []; current.value = undefined; fields.value = {}; editorSource.value = undefined
}
function identityChanged() {
  generation++; controller?.abort(); clearFields(); key.value = ''; pending.value = undefined
  message.value = ''; stale.value = false; busy.value = false; error.value = '登录身份已变化，请重新读取。'
  try { sessionStorage.removeItem(cache) } catch { error.value += ' 浏览器未能清除保存缓存。' }
}
async function run(action: (check: () => void, signal: AbortSignal) => Promise<void>) {
  if (busy.value) return
  const epoch = identityStamp(), request = generation; controller = new AbortController(); busy.value = true; error.value = ''
  const check = () => { requireIdentity(epoch); if (!live || generation !== request) throw new Error('STALE') }
  try { await action(check, controller.signal); check() }
  catch (e: any) {
    try {
      check()
      const status = e?.response?.status
      if (e instanceof CommandStorageError) error.value = e.message
      else if ([401, 403].includes(status)) { clearFields(); error.value = '当前权限无法读取或保存，请重新核对权限。' }
      else error.value = pending.value ? '保存结果尚未确认，请查询原结果或以原意图重试。' : e instanceof Error ? e.message : '请求未被接受，当前草稿已保留。'
    } catch { /* Obsolete identity/generation responses cannot update this page. */ }
  } finally { if (live && generation === request) busy.value = false }
}
function apply(view: FormAuthoringView) {
  current.value = view; key.value = view.key; fields.value = { ...view.fields }; stale.value = false
  editorSource.value = typeof view.fields.SchemaJson === 'string' ? { schemaJson: view.fields.SchemaJson, version: view.version } : undefined
}
function schemaChanged(raw: string) {
  if (!busy.value && !pending.value && writable.value.includes('SchemaJson')) fields.value.SchemaJson = raw
}
function reload() {
  if (pending.value) return
  return run(async (check, signal) => {
    const list = await api.list(page.value, signal); check(); items.value = list
    entry.value = undefined
    try { const available = await api.entry(signal); check(); entry.value = available }
    catch (e: any) { check(); if (e?.response?.status === 401) throw e; message.value = '当前无法新建；仍可读取有权限查看的草稿。' }
  })
}
function load(value: string) {
  if (pending.value) return
  return run(async (check, signal) => { const loaded = await api.read(value, signal); check(); apply(loaded); message.value = '' })
}
function create() {
  if (busy.value || pending.value || !entry.value?.canCreate) return
  current.value = undefined; key.value = ''; fields.value = { Name: '', SchemaJson: initial, Category: null, SubCategory: null }
  editorSource.value = { schemaJson: initial, version: crypto.randomUUID() }; stale.value = false; message.value = ''; error.value = ''
}
async function accept(result: FormAuthoringResult, check: () => void, signal: AbortSignal) {
  const intent = pending.value
  // Correlation is a network boundary; body/schema contracts are owned by the server.
  if (!intent || result.commandId !== intent.command.commandId || result.outcome !== 'SUCCEEDED' || !result.result || result.result.key !== intent.key)
    throw new Error('原结果与当前保存不匹配')
  apply(result.result); pending.value = undefined; remember(); message.value = '保存已确认。表单草稿尚未发布或启用。'
  const latest = await api.read(intent.key, signal); check(); apply(latest)
  const list = await api.list(page.value, signal); check(); items.value = list
}
function save() {
  if (!pending.value && (!canSave.value || !key.value.trim() || !fields.value.Name?.trim())) return
  return run(async (check, signal) => {
    if (!pending.value) pending.value = { key: key.value, unknown: false, command: { commandId: crypto.randomUUID(), expectedVersion: current.value?.version ?? null,
      body: { name: fields.value.Name!, schemaJson: fields.value.SchemaJson!, category: fields.value.Category ?? null, subCategory: fields.value.SubCategory ?? null } } }
    const intent = pending.value, wasUnknown = intent.unknown
    // Persist before every send. A storage failure must not send an untrackable command.
    try { remember() } catch { throw new CommandStorageError('浏览器无法保存恢复记录，本次请求尚未发送。') }
    intent.unknown = true
    try { remember() } catch { intent.unknown = wasUnknown; throw new CommandStorageError('浏览器无法保存恢复记录，本次请求尚未发送。') }
    let result: FormAuthoringResult
    try { result = await api.save(intent.key, JSON.parse(JSON.stringify(intent.command)), signal) }
    catch (e: any) {
      check()
      if (!wasUnknown && [400, 401, 403, 409, 412, 422].includes(e?.response?.status)) {
        pending.value = undefined; remember(); stale.value = [409, 412].includes(e.response.status)
        if (stale.value) throw new Error('草稿版本已变化。修改已保留，请读取当前草稿后重新确认。')
      }
      throw e
    }
    check(); await accept(result, check, signal)
  })
}
function recover() {
  return run(async (check, signal) => {
    if (!pending.value) return
    const result = await api.original(pending.value.key, pending.value.command.commandId, signal); check()
    if (result.outcome === 'SUCCEEDED') await accept(result, check, signal)
    else message.value = '尚未观察到原结果；这不能证明未保存。请继续查询或以原意图重试。'
  })
}
function leave() { if (!busy.value && !pending.value) emit('close') }
onMounted(() => {
  window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storageChanged)
  try {
    const saved = JSON.parse(sessionStorage.getItem(cache) ?? 'null')
    if (saved?.identity === identityStamp() && saved.pending?.command?.commandId && typeof saved.pending.key === 'string') {
      pending.value = { ...saved.pending, unknown: true }; key.value = saved.pending.key; message.value = '有一次保存尚待确认，请先查询原结果。'; return
    }
    sessionStorage.removeItem(cache)
  } catch { error.value = '无法读取浏览器保存记录，请恢复浏览器存储后重试。'; return }
  void reload()
})
onBeforeUnmount(() => { live = false; controller?.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storageChanged) })
</script>

<template>
  <section class="form-authoring">
    <header><h2>表单草稿工作区</h2><button :disabled="busy || !!pending" @click="leave">返回</button></header>
    <p>保存后为未发布、未启用的草稿。发布和启用分别执行。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <nav>
      <button :disabled="busy || !!pending" @click="reload">刷新列表</button>
      <button :disabled="busy || !!pending || !entry?.canCreate" @click="create">新建表单</button>
      <button :disabled="busy || !!pending || page === 1" @click="page--; reload()">上一页</button><span>第 {{ page }} 页</span>
      <button :disabled="busy || !!pending || items.length < 100" @click="page++; reload()">下一页</button>
    </nav>
    <ul><li v-for="item in items" :key="item.definitionId"><button :disabled="busy || !!pending" @click="load(item.key)">{{ item.key }} · {{ item.fields.Name ?? '名称不可见' }}</button></li></ul>
    <div v-if="pending" class="pending"><span>待确认：{{ pending.key }}</span><button :disabled="busy" @click="recover">查询原保存结果</button><button :disabled="busy" @click="save">以原意图重试</button></div>
    <div v-if="Object.keys(fields).length">
      <label>表单标识 <input v-model="key" :disabled="!!current || busy || !!pending" maxlength="100" /></label>
      <label v-for="field in (['Name', 'Category', 'SubCategory'] as const)" v-show="Object.hasOwn(fields, field)" :key="field">{{ labels[field] }}
        <input v-model="fields[field]" :disabled="busy || !!pending || !writable.includes(field)" :maxlength="field === 'Name' ? 200 : 100" />
        <button v-if="field !== 'Name'" :disabled="busy || !!pending || !writable.includes(field)" @click="fields[field] = null">清除分类</button>
      </label>
      <span v-if="dirty">有未保存修改</span>
      <button :disabled="busy || !!pending || !canSave" @click="save">保存草稿</button>
      <button v-if="current" :disabled="busy || !!pending" @click="load(current.key)">重新读取当前草稿（放弃本地修改）</button>
      <FormDesigner v-if="editorSource" :managed-source="editorSource" :read-only="busy || !!pending || !writable.includes('SchemaJson')" @schema-change="schemaChanged" />
      <p v-else>表单结构当前不可见，不能保存整份草稿。</p>
    </div>
  </section>
</template>

<style scoped>
.form-authoring { padding: 16px; display: grid; gap: 12px; }
header, nav, .pending { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
h2 { margin: 0; } label { display: inline-flex; gap: 6px; margin: 8px; align-items: center; }
button { padding: 6px 12px; cursor: pointer; } button:disabled { cursor: default; }
[role=alert] { color: var(--el-color-danger); } ul { list-style: none; padding: 0; }
.form-designer { margin-top: 16px; min-height: 620px; }
</style>
