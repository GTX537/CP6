<script setup lang="ts">
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { getActingAs } from '@/stores/oaActingAs'
import { formFileApi as api, type FormFileReadContext, type FormFileView, type FormFileUpload, type FormFileCommand } from '@/api/oa/formFiles'
import type { FormValue } from './personalFormJson'
import { personalFormError } from './personalFormError'
const props = defineProps<{ context?: FormFileReadContext; field: string; modelValue?: FormValue; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: FormValue]; validity: [value: boolean]; forbidden: [] }>()
type Intent = { identity: string; operation: string; kind: 'UPLOAD' | 'STATE' }
const views = ref<Record<string, FormFileView>>({}), pending = ref<Intent>(), busy = ref(false), error = ref(''), message = ref(''), storageBlocked = ref(false)
const references = computed(() => Array.isArray(props.modelValue) ? props.modelValue.filter((v): v is string => typeof v === 'string') : typeof props.modelValue === 'string' && props.modelValue ? [props.modelValue] : [])
const editable = computed(() => !props.disabled && !!props.context?.scope && props.context.scope.kind !== 'HISTORY')
const key = computed(() => { const c = props.context; return `cp6-form-file-pending:${c?.scope?.kind ?? 'READ'}:${c?.scope?.id ?? c?.scope?.key ?? c?.instanceId ?? c?.queryDataId ?? ''}:${props.field}` })
let stamp = identityStamp(), generation = 0, live = true, controller: AbortController | undefined, body: FormFileUpload | FormFileCommand | undefined, bytes: File | undefined
const valid = computed(() => !busy.value && !pending.value && !storageBlocked.value)
watch(valid, value => emit('validity', value), { immediate: true })
function privacy() { views.value = {}; body = undefined; bytes = undefined; emit('forbidden') }
function restore() {
  try {
    const text = sessionStorage.getItem(key.value); if (!text) return
    const value = JSON.parse(text) as Intent
    if (value.identity !== identityStamp()) { sessionStorage.removeItem(key.value); return }
    if (!/^[0-9a-f-]{36}$/.test(value.operation) || !['UPLOAD', 'STATE'].includes(value.kind)) throw new Error('附件恢复标识损坏')
    pending.value = value
  } catch { storageBlocked.value = true; error.value = '无法读取附件恢复标识，已停止新操作。' }
}
function reset() { generation++; controller?.abort(); busy.value = false; views.value = {}; body = undefined; bytes = undefined; pending.value = undefined; storageBlocked.value = false; error.value = ''; message.value = ''; restore() }
function changed() { if (stamp !== identityStamp()) { stamp = identityStamp(); reset(); privacy() } }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
function capture() { const epoch = generation, identity = identityStamp(), behalf = getActingAs()?.userId; return () => { requireIdentity(identity); if (!live || epoch !== generation || behalf !== getActingAs()?.userId) throw new Error('STALE') } }
async function refresh() {
  if (!props.context || busy.value) return
  const check = capture(), epoch = generation; busy.value = true; views.value = {}; controller = new AbortController(); error.value = ''
  try { const result: Record<string, FormFileView> = {}; for (const reference of references.value) { result[reference] = await api.read(props.context, props.field, reference, controller.signal); check() } views.value = result }
  catch (e: any) { try { check(); if ([401, 403, 404].includes(e?.response?.status)) privacy(); error.value = personalFormError(e) } catch { /* stale */ } }
  finally { if (live && epoch === generation) busy.value = false }
}
function forget() { sessionStorage.removeItem(key.value); pending.value = undefined; body = undefined; bytes = undefined }
async function run(mode: 'send' | 'query' | 'close', wasUnknown = true) {
  const intent = pending.value; if (!intent || busy.value || storageBlocked.value) return
  const check = capture(), epoch = generation; let resolved = false; busy.value = true; controller = new AbortController(); error.value = ''; message.value = ''
  try {
    const result = mode === 'query' ? await api.original(intent.operation, controller.signal) : mode === 'close' ? await api.close(intent.operation, controller.signal)
      : body && intent.kind === 'UPLOAD' && bytes ? await api.upload(body as FormFileUpload, bytes, controller.signal)
        : body && intent.kind === 'STATE' ? await api.command(body as FormFileCommand, controller.signal) : (() => { throw new Error('原文件内容已清除，请查询或关闭原操作。') })()
    check()
    if (result.state === 'UNKNOWN') { message.value = '原附件操作结果尚待确认；请继续查询，或关闭未执行操作。'; return }
    if (result.state === 'COMMITTED') {
      const value = result.result!; views.value[value.reference] = value
      if (intent.kind === 'UPLOAD' && !references.value.includes(value.reference)) emit('update:modelValue', [...references.value, value.reference])
      message.value = '附件原操作已确认；可用状态以当前读取结果为准。'
    } else message.value = '原操作已关闭，未产生文件记录。'
    forget(); resolved = true
  } catch (e: any) {
    try {
      check(); const status = e?.response?.status
      if ([401, 403, 404].includes(status)) privacy()
      if (mode === 'send' && !wasUnknown && status >= 400 && status < 500 && ![408, 429].includes(status)) forget()
      error.value = personalFormError(e)
    } catch { /* preserve an unknown operation; obsolete responses cannot clear current state */ }
  } finally {
    if (live && epoch === generation) {
      busy.value = false
      if (resolved) { await nextTick(); if (live && epoch === generation) await refresh() }
    }
  }
}
async function begin(kind: Intent['kind'], value: FormFileUpload | FormFileCommand, file?: File) {
  if (!editable.value || busy.value || pending.value || storageBlocked.value) return
  const intent: Intent = { identity: identityStamp(), operation: value.operationId, kind }
  try { sessionStorage.setItem(key.value, JSON.stringify(intent)) } catch { storageBlocked.value = true; error.value = '无法保存附件恢复标识，未发送。'; return }
  body = value; bytes = file; pending.value = intent; await run('send', false)
}
async function select(event: Event) {
  const input = event.target as HTMLInputElement, file = input.files?.[0]; input.value = ''
  if (!file || !props.context?.scope || !editable.value || !valid.value) return
  if (!file.size || file.size > 20 * 1024 * 1024) { error.value = '请选择 1 字节至 20 MiB 的文件。'; return }
  const check = capture(), scope = { ...props.context.scope }; const field = props.field; busy.value = true
  try {
    const digest = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', await file.arrayBuffer()))).map(x => x.toString(16).padStart(2, '0')).join(''); check(); busy.value = false
    await begin('UPLOAD', { operationId: crypto.randomUUID(), fileId: crypto.randomUUID(), scope, field, fileName: file.name, byteLength: file.size, digest }, file)
  } catch (e: any) { try { check(); busy.value = false; error.value = personalFormError(e) } catch { /* stale */ } }
}
async function inspect(value: FormFileView) { if (props.context?.scope) await begin('STATE', { operationId: crypto.randomUUID(), scope: { ...props.context.scope }, field: props.field, reference: value.reference, expectedStateVersion: value.stateVersion, action: 'INSPECT', reason: '用户重新检查表单附件' }) }
async function download(reference: string) {
  if (!props.context || busy.value) return
  const check = capture(), epoch = generation; busy.value = true; error.value = ''
  try { const blob = await api.download(props.context, props.field, reference); check(); const url = URL.createObjectURL(blob), link = document.createElement('a'); link.href = url; link.download = views.value[reference]?.fileName ?? 'attachment'; link.click(); URL.revokeObjectURL(url) }
  catch (e: any) { try { check(); if ([401, 403, 404].includes(e?.response?.status)) privacy(); error.value = personalFormError(e) } catch { /* stale */ } }
  finally { if (live && epoch === generation) busy.value = false }
}
function remove(reference: string) { if (editable.value && valid.value) emit('update:modelValue', references.value.filter(x => x !== reference)) }
watch(() => JSON.stringify(props.context) + props.field, () => { reset(); void refresh() }, { immediate: true })
watch(() => JSON.stringify(references.value), () => { if (!busy.value) void refresh() })
onBeforeUnmount(() => { live = false; generation++; controller?.abort(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<template>
  <section class="form-files" aria-label="表单附件">
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <div v-if="pending"><p>附件原操作待确认：{{ pending.operation }}</p><button type="button" :disabled="busy" @click="run('query')">查询附件原结果</button><button v-if="body" type="button" :disabled="busy" @click="run('send')">重试原附件操作</button><button type="button" :disabled="busy" @click="run('close')">关闭未执行附件操作 / 取回已执行结果</button></div>
    <ul v-if="references.length"><li v-for="reference in references" :key="reference"><template v-if="views[reference]">{{ views[reference]!.fileName }} · {{ views[reference]!.contentRef.byteLength }} 字节 · {{ ({ AVAILABLE: '可用', PENDING: '待检查', QUARANTINED: '已隔离', HELD: '保留锁定' } as Record<string, string>)[views[reference]!.availability] }}<button type="button" :disabled="busy || views[reference]!.availability !== 'AVAILABLE'" @click="download(reference)">下载原件</button><button v-if="editable" type="button" :disabled="!valid" @click="inspect(views[reference]!)">重新检查</button></template><span v-else>附件信息尚未读取</span><button v-if="editable" type="button" :disabled="!valid" @click="remove(reference)">移除此引用</button></li></ul>
    <button v-if="references.length && context" type="button" :disabled="busy" @click="refresh">重新读取附件状态</button>
    <label v-if="editable">添加附件<input type="file" :disabled="!valid" @change="select"></label>
    <p v-if="editable">单个文件最多 20 MiB；检查通过后可保存或提交。移除引用不会删除原文件。</p>
  </section>
</template>
<style scoped>.form-files { padding: 8px; border: 1px solid var(--el-border-color); }button { margin: 4px; }li { overflow-wrap: anywhere; margin: 8px 0; }[role=alert] { color: var(--el-color-danger); }p { font-size: 13px; }</style>
