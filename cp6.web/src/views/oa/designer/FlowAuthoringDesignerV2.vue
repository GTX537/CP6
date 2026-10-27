<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { flowAuthoringApi as api, type AuthoringCommand, type AuthoringEntry, type AuthoringField, type AuthoringResult, type AuthoringView } from '@/api/oa/flowAuthoring'
import DesignerCanvas from './DesignerCanvas.vue'
import NodePropertyPanel from './NodePropertyPanel.vue'
import EdgePropertyPanel from './EdgePropertyPanel.vue'
import { type FlowSchemaDto, type SchemaNode, type SchemaEdge } from './designerModel'
import { preserveSchema } from './preserveSchema'

const emit = defineEmits<{ close: [] }>()
const cache = 'cp6-workflow-authoring-pending'
const initial = '{"start":"start1","nodes":[{"id":"start1","type":"start","name":"发起","x":260,"y":60},{"id":"end1","type":"end","name":"结束","x":260,"y":330}],"edges":[]}'
const allFields: AuthoringField[] = ['Name', 'SchemaJson', 'FormKey', 'FunctionId', 'FlowCode']
const labels: Record<AuthoringField, string> = { Name: '流程名称', SchemaJson: '流程结构', FormKey: '表单标识', FunctionId: '功能 ID', FlowCode: '流程编号' }
const entry = ref<AuthoringEntry>(), items = ref<AuthoringView[]>([]), current = ref<AuthoringView>()
const key = ref(''), fields = ref<Partial<Record<AuthoringField, string | null>>>({}), schema = ref<FlowSchemaDto>(JSON.parse(initial)), originalSchema = ref(initial)
const busy = ref(false), error = ref(''), message = ref(''), stale = ref(false), page = ref(1)
const pending = ref<{ key: string; command: AuthoringCommand }>()
const selected = ref<{ kind: 'node' | 'edge' | null; id: string | null }>({ kind: null, id: null })
const selectedNode = computed(() => selected.value.kind === 'node' ? schema.value.nodes.find(n => n.id === selected.value.id) : undefined)
const edgeIndex = computed(() => selected.value.kind === 'edge' ? schema.value.edges.findIndex((e, i) => (e.id || `${e.from}__${e.to}__${i}`) === selected.value.id) : -1)
const writable = computed(() => current.value?.writableFields ?? entry.value?.writableFields ?? [])
const readable = computed(() => allFields.every(f => Object.hasOwn(fields.value, f)))
const canSave = computed(() => !stale.value && readable.value && writable.value.length > 0 && (current.value != null || entry.value?.canCreate))
let generation = 0, live = true, controller: AbortController | undefined
function remember() {
  if (pending.value) sessionStorage.setItem(cache, JSON.stringify({ identity: identityStamp(), pending: pending.value }))
  else sessionStorage.removeItem(cache)
}
function clearPrivate() {
  generation++; controller?.abort(); entry.value = undefined; items.value = []; current.value = undefined
  key.value = ''; fields.value = {}; originalSchema.value = initial; schema.value = JSON.parse(initial); pending.value = undefined
  selected.value = { kind: null, id: null }; message.value = ''; error.value = ''; stale.value = false; busy.value = false; remember()
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
      if ([401, 403].includes(status)) { clearPrivate(); error.value = '权限已变化，已清除页面内容。请重新读取。' }
      else if ([400, 409, 412, 422].includes(status)) {
        pending.value = undefined; remember(); stale.value = true; error.value = '保存未被接受。请重新读取当前草稿后确认修改。'
      } else error.value = pending.value ? '保存结果尚未确认，请查询原结果或以原意图重试。' : '当前无法读取草稿，请稍后重试。'
    } catch { /* Discard obsolete identity/generation responses. */ }
  } finally { if (live && generation === request) busy.value = false }
}
function apply(view: AuthoringView) {
  current.value = view; key.value = view.key; fields.value = { ...view.fields }; stale.value = false
  originalSchema.value = view.fields.SchemaJson ?? initial; schema.value = JSON.parse(originalSchema.value)
  selected.value = { kind: null, id: null }
}
async function reload() {
  if (pending.value) return
  return run(async (check, signal) => {
    const list = await api.list(page.value, signal); check(); items.value = list
    // Existing readable drafts remain accessible when creation permission or Scope registration is unavailable.
    try { const available = await api.entry(signal); check(); entry.value = available }
    catch (e: any) { check(); entry.value = undefined; if (e?.response?.status === 401) throw e }
    if (current.value) { const value = await api.read(key.value, signal); check(); apply(value) }
  })
}
function load(value: string) {
  if (pending.value) return
  return run(async (check, signal) => { const loaded = await api.read(value, signal); check(); apply(loaded); message.value = '' })
}
function create() {
  if (busy.value || pending.value || !entry.value?.canCreate) return
  current.value = undefined; key.value = ''; fields.value = { Name: '', SchemaJson: initial, FormKey: null, FunctionId: null, FlowCode: null }
  originalSchema.value = initial; schema.value = JSON.parse(initial); selected.value = { kind: null, id: null }; stale.value = false; message.value = ''; error.value = ''
}
async function accept(result: AuthoringResult, check: () => void, signal: AbortSignal) {
  const intent = pending.value
  if (!intent || result.commandId !== intent.command.commandId || result.outcome !== 'SUCCEEDED' || !result.result
    || result.result.key !== intent.key || result.resourceId !== result.result.definitionId || result.resourceVersion !== result.result.version
    || current.value && current.value.definitionId !== result.resourceId) throw new Error('原结果不匹配')
  apply(result.result); pending.value = undefined; remember(); message.value = '原保存已确认。草稿尚未发布或启用。'
  const latest = await api.read(intent.key, signal); check(); apply(latest)
  const list = await api.list(page.value, signal); check(); items.value = list
}
function save() {
  if (!pending.value && (!canSave.value || !key.value.trim() || !fields.value.Name?.trim())) return
  return run(async (check, signal) => {
    if (!pending.value) {
      const body = { name: fields.value.Name!, schemaJson: preserveSchema(originalSchema.value, schema.value),
        formKey: fields.value.FormKey ?? null, functionId: fields.value.FunctionId || null, flowCode: fields.value.FlowCode || null }
      pending.value = { key: key.value, command: { commandId: crypto.randomUUID(), expectedVersion: current.value?.version ?? null, body } }; remember()
    }
    const intent = pending.value
    const result = await api.save(intent.key, JSON.parse(JSON.stringify(intent.command)), signal); check(); await accept(result, check, signal)
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
function patchNode(patch: Partial<SchemaNode>) {
  if (!writable.value.includes('SchemaJson') || pending.value) return
  schema.value = { ...schema.value, nodes: schema.value.nodes.map(n => n.id === selected.value.id ? { ...n, ...patch, id: n.id } : n) }
}
function patchEdge(patch: Partial<SchemaEdge>) {
  if (!writable.value.includes('SchemaJson') || pending.value) return
  schema.value = { ...schema.value, edges: schema.value.edges.map((e, i) => i === edgeIndex.value ? { ...e, ...patch, id: e.id, from: e.from, to: e.to } : e) }
}
function identityChanged() { clearPrivate(); error.value = '登录身份已变化，请重新读取。' }
function leave() { if (!pending.value && !busy.value) emit('close') }
onMounted(() => {
  try {
    const saved = JSON.parse(sessionStorage.getItem(cache) ?? 'null')
    if (saved?.identity === identityStamp() && saved.pending?.command?.commandId && typeof saved.pending.key === 'string') {
      pending.value = saved.pending; key.value = saved.pending.key; message.value = '有一次保存尚待确认，请先查询原结果。'; return
    }
    sessionStorage.removeItem(cache)
  } catch { sessionStorage.removeItem(cache) }
  void reload()
})
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => { live = false; generation++; controller?.abort(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>

<template>
  <section class="authoring" aria-label="流程草稿工作区">
    <header><div><h2>流程草稿</h2><p>在这里编写并保存新流程。草稿保持停用，发布入口尚未开放。</p></div>
      <div class="actions"><button :disabled="busy || !!pending" @click="leave">返回流程库</button><button :disabled="busy || !!pending" @click="reload">重新读取</button>
        <button :disabled="busy || !!pending || !entry?.canCreate" @click="create">新建草稿</button>
        <button :disabled="busy || (!pending && !canSave)" @click="save">{{ pending ? '以原意图重试' : '保存草稿' }}</button>
        <button v-if="pending" :disabled="busy" @click="recover">查询原保存结果</button></div></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <p v-if="!entry?.canCreate && !pending">当前不能新建草稿。请确认流程设计权限及本租户的草稿登记策略。</p>
    <div class="workbench">
      <aside><h3>已保存草稿</h3><button v-for="item in items" :key="item.definitionId" :disabled="busy || !!pending" @click="load(item.key)">{{ item.fields.Name ?? item.key }}</button>
        <p v-if="!items.length">暂无可读取的草稿</p><div class="pages"><button :disabled="busy || !!pending || page === 1" @click="page--; reload()">上一页</button><span>{{ page }}</span><button :disabled="busy || !!pending || items.length < 100" @click="page++; reload()">下一页</button></div></aside>
      <main>
        <div class="identity" :inert="busy || !!pending || stale">
          <label>流程标识<input v-model="key" :disabled="!!current" maxlength="100" /></label>
          <label v-for="field in allFields.filter(f => f !== 'SchemaJson')" :key="field" v-show="Object.hasOwn(fields, field)">{{ labels[field] }}<input v-model="fields[field]" :disabled="!writable.includes(field)" :maxlength="field === 'Name' ? 200 : field === 'FormKey' ? 100 : 50" /></label>
        </div>
        <p v-if="!Object.hasOwn(fields, 'SchemaJson')">当前无权读取流程结构。</p>
        <div v-else class="canvas" :inert="busy || !!pending || stale || !writable.includes('SchemaJson')"><DesignerCanvas v-model="schema" @select="selected = $event" /></div>
        <p v-if="current?.diagnostics.length">草稿仍有待完善项目：{{ current.diagnostics.join('、') }}。保存不代表可发布。</p>
      </main>
      <aside :inert="busy || !!pending || stale || !writable.includes('SchemaJson')">
        <NodePropertyPanel v-if="selectedNode" :node="selectedNode" :current-flow-key="key" @update="patchNode" />
        <EdgePropertyPanel v-else-if="edgeIndex >= 0 && schema.edges[edgeIndex]" :edge="schema.edges[edgeIndex]!" @update="patchEdge" />
        <p v-else>选择节点或路径以编辑属性。</p>
      </aside>
    </div>
  </section>
</template>
<style scoped>
.authoring { padding: 18px; background: #f7f9fc; color: #26354a; min-height: 70vh; } header { display: flex; justify-content: space-between; gap: 20px; } h2, h3 { margin: 0 0 10px; } p { line-height: 1.6; } .actions { display: flex; flex-wrap: wrap; align-content: start; gap: 8px; } button { border: 1px solid #ccd6e3; border-radius: 6px; background: white; padding: 8px 12px; cursor: pointer; } button:disabled { opacity: .5; cursor: default; } .workbench { display: grid; grid-template-columns: 220px minmax(320px, 1fr) 300px; gap: 14px; } aside, main { padding: 12px; background: white; border: 1px solid #e1e7ef; border-radius: 8px; min-width: 0; } aside > button { display: block; width: 100%; text-align: left; margin-bottom: 6px; } .identity { display: flex; flex-wrap: wrap; gap: 12px; } label { display: grid; gap: 5px; font-size: 13px; } input { padding: 8px; border: 1px solid #ccd6e3; border-radius: 5px; } .canvas { height: 560px; margin-top: 15px; } [role=alert] { color: #a63727; } .pages { display: flex; gap: 8px; align-items: center; margin-top: 16px; } @media(max-width:1100px) { .workbench { grid-template-columns: 180px 1fr; } .workbench > aside:last-child { grid-column: 1/-1; } header { display: block; } }
</style>
