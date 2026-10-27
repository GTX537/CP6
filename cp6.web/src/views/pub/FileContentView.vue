<template>
  <main class="file-content" :aria-busy="busy">
    <header><div><h1>受控文件内容</h1><p>保存工程原件、查看安全检查与保留状态，按当前权限下载准确版本。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <section v-if="pending"><p>上次操作结果尚未确认，请先恢复。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交操作</button></section>
    <section><h2>工程上下文</h2><form @submit.prevent="findParents(1)"><label>修订编号<input v-model="search" aria-label="工程修订搜索" :disabled="locked" maxlength="256" /></label><button :disabled="locked || !canRead">查找工程修订</button><button type="button" :disabled="locked || !moreParents" @click="findParents(parentPage + 1)">更多工程修订</button></form>
      <label>工程修订<select v-model="choice" aria-label="工程修订选择" :disabled="locked"><option value="">请选择当前版本</option><option v-for="p in parents" :key="key(p.reference)" :value="key(p.reference)">{{ p.revisionCode }} · v{{ p.reference.version }} · {{ p.draft.goal }}</option></select></label><button :disabled="locked || !choice" @click="loadPage(1)">读取文件</button>
      <p>上传记录工程来源；正式文档绑定、用途批准和工程冻结仍由对应业务流程确认。</p>
    </section>
    <section v-if="parent && permissions.has('file-content:add')"><h2>上传原件</h2><form @submit.prevent="upload"><fieldset :disabled="locked || !canRead"><label>文件<input :key="inputEpoch" type="file" aria-label="原件文件" @change="pick" /></label><p>单个文件不超过 20 MiB。文件名仅作显示；未完成检查的文件暂不可下载。</p><label>上传原因<textarea v-model="uploadReason" aria-label="上传原因" maxlength="4000" required /></label><button :disabled="!selected">上传原件</button></fieldset></form></section>
    <section v-if="parent"><h2>文件目录</h2><p v-if="!rows.length">当前范围内没有文件。</p><article v-for="row in rows" :key="row.contentRef.id"><h3>{{ row.fileName }}</h3><p>内容 v{{ row.contentRef.version }} · {{ row.contentRef.byteLength }} 字节 · {{ state(row.availability) }}</p><p class="digest">SHA256：{{ row.contentRef.digest }}</p><button :disabled="busy" @click="open(row.contentRef.id)">查看文件</button><button :disabled="busy || row.availability !== 'AVAILABLE'" @click="download(row)">下载原件</button></article><nav><button :disabled="busy || page === 1" @click="loadPage(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="loadPage(page + 1)">下一页</button></nav></section>
    <section v-if="current"><h2>{{ current.fileName }}</h2><p>{{ state(current.availability) }} · 状态 v{{ current.stateVersion }}</p><p>检查：{{ inspection(current.inspection.status) }} · {{ current.inspection.checkedAt }} · {{ current.inspection.provider }}</p><p v-if="current.inspection.signatureVersion">检查签名版本：{{ current.inspection.signatureVersion }}</p>
      <fieldset v-if="permissions.has('file-content:edit')" :disabled="locked"><label>状态变更原因<textarea v-model="reason" aria-label="状态原因" maxlength="4000" /></label><button :disabled="!reason.trim()" @click="change('INSPECT')">重新检查</button><button v-if="!current.held" :disabled="!reason.trim()" @click="change('HOLD')">设置保留</button><button v-else :disabled="!reason.trim()" @click="change('RELEASE')">解除保留</button></fieldset>
      <form @submit.prevent="readHistory"><label>历史状态版本<input v-model="historyVersion" aria-label="历史状态版本" inputmode="numeric" pattern="[1-9][0-9]*" :disabled="busy" /></label><button :disabled="busy">读取历史状态</button></form>
      <article v-if="history" aria-label="历史文件状态"><h3>状态 v{{ history.stateVersion }}</h3><p>{{ state(history.availability) }} · {{ history.recordedAt }} · {{ history.reason }}</p><p>历史检查状态不代表当前可下载；新下载始终重新核对当前状态与权限。</p></article>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { fileContentApi, type FileSnapshot, type FileCommand } from '@/api/pub/fileContent'
import { revisionApi, type RevisionSnapshot } from '@/api/plm/engineeringRevisions'
import type { EngineeringReference, EngineeringScope } from '@/api/plm/engineeringProducts'
import { pendingFile, fileResponse as result, saveFile, resolveFile, type PendingFile } from '@/auth/fileContentRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), canRead = computed(() => permissions.loaded && permissions.has('file-content:query'))
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<EngineeringScope | null>(null), pending = ref<PendingFile | null>(null), locked = computed(() => busy.value || !!pending.value)
const parents = ref<RevisionSnapshot[]>([]), search = ref(''), choice = ref(''), parentPage = ref(1), moreParents = ref(false)
const parent = computed(() => parents.value.find(p => key(p.reference) === choice.value)), rows = ref<FileSnapshot[]>([]), page = ref(1)
const selected = ref<File | null>(null), inputEpoch = ref(0), uploadReason = ref(''), reason = ref(''), current = ref<FileSnapshot | null>(null), history = ref<FileSnapshot | null>(null), historyVersion = ref('1')
const urls = new Set<string>(); let generation = 0, live = true
const key = (r: EngineeringReference) => `${r.owner}:${r.id}:${r.version}:${r.digest}`
const state = (value: string) => ({ AVAILABLE: '检查通过，可申请下载', PENDING_INSPECTION: '等待安全检查', HELD: '保留中，暂停下载', QUARANTINED: '隔离中，暂停下载' }[value] ?? '状态待确认')
const inspection = (value: string) => ({ CLEAN: '已完成安全检查', PENDING: '检查尚未就绪', NOT_CLEAN: '检查或完整性未通过' }[value] ?? '待确认')
function clearFiles() { rows.value = []; current.value = history.value = null; selected.value = null; inputEpoch.value++; uploadReason.value = reason.value = ''; page.value = 1; for (const url of urls) URL.revokeObjectURL(url); urls.clear() }
function clearPrivate() { clearFiles(); parents.value = []; choice.value = search.value = ''; moreParents.value = false }
function syncPending() { pending.value = scope.value ? pendingFile(scope.value) : null }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return; const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || generation !== epoch || !authenticationCanRead() || !canRead.value) throw new Error('当前身份或权限已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); await action(check); check() }
  catch (e: any) { try { check(); const status = e?.response?.status; if ([401, 403].includes(status)) clearPrivate(); error.value = [401, 403].includes(status) ? '当前无权读取文件或工程来源。' : status === 412 ? '对象版本已变化，请查询原操作并刷新。' : status === 409 || status === 503 ? '文件检查、保留状态或存储暂不允许此操作，请刷新核对。' : e?.message || '操作结果尚未确认。' } catch { /* Changed identity cannot publish old data. */ } }
  finally { try { check(); syncPending(); busy.value = false } catch { if (live && epoch === generation) busy.value = false } }
}
async function reload() { if (!permissions.loaded) await permissions.loadMyActions(); await run(async check => { clearPrivate(); scope.value = null; const s = result(await fileContentApi.scope()); check(); scope.value = s }) }
async function findParents(target: number) { await run(async check => { if (target === 1) { clearFiles(); parents.value = []; choice.value = '' } const values = result(await revisionApi.list(search.value, target)); check(); parents.value.push(...values.filter(p => p.isCurrent)); parentPage.value = target; moreParents.value = values.length === 20 }) }
async function loadRows(target: number, check: () => void) { const p = parent.value; if (!p) return; rows.value = []; const values = result(await fileContentApi.list(p.reference.id, target)); check(); if (parent.value?.reference.id !== p.reference.id || values.some(v => v.tenantId !== scope.value?.tenantId || v.parent.id !== p.reference.id)) throw new Error('文件上下文已变化。'); rows.value = values; page.value = target }
async function loadPage(target: number) { await run(check => loadRows(target, check)) }
function pick(event: Event) { selected.value = (event.target as HTMLInputElement).files?.[0] ?? null }
async function hash(bytes: ArrayBuffer) { return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))).map(v => v.toString(16).padStart(2, '0')).join('') }
async function upload() {
  const s = scope.value, file = selected.value, p = parent.value; if (!s || !file || !p || locked.value || !permissions.has('file-content:add')) return
  await run(async check => {
    if (file.size < 1 || file.size > 20 * 1024 * 1024) throw new Error('请选择 1 字节至 20 MiB 的文件。')
    const bytes = await file.arrayBuffer(); check(); const digest = await hash(bytes); check(); const id = crypto.randomUUID()
    await saveFile(s, id, () => { check(); if (!permissions.has('file-content:add')) throw new Error('当前无上传权限。') }, operationId => fileContentApi.upload({ operationId, fileId: id, parent: p.reference, fileName: file.name, byteLength: file.size, digest, reason: uploadReason.value }, bytes)); check()
    selected.value = null; inputEpoch.value++; uploadReason.value = ''; await loadRows(1, check); notice.value = '原件已保存，请按当前检查状态操作。'
  })
}
async function open(id: string) { await run(async check => { current.value = history.value = null; reason.value = ''; const value = result(await fileContentApi.read(id)); check(); if (value.tenantId !== scope.value?.tenantId || value.parent.id !== parent.value?.reference.id) throw new Error('文件上下文已变化。'); current.value = value }) }
async function change(action: FileCommand['action']) {
  const s = scope.value, value = current.value; if (!s || !value || locked.value || !reason.value.trim() || !permissions.has('file-content:edit')) return
  await run(async check => { await saveFile(s, value.contentRef.id, () => { check(); if (!permissions.has('file-content:edit')) throw new Error('当前无变更权限。') }, operationId => fileContentApi.save({ operationId, fileId: value.contentRef.id, action, expectedVersion: value.stateVersion, expectedEtag: value.etag, reason: reason.value })); check(); const saved = result(await fileContentApi.read(value.contentRef.id)); check(); current.value = saved; history.value = null; reason.value = ''; await loadRows(page.value, check) })
}
async function download(value: FileSnapshot) { await run(async check => { const blob = await fileContentApi.download(value.contentRef); check(); const bytes = await blob.arrayBuffer(); check(); if (bytes.byteLength !== value.contentRef.byteLength || await hash(bytes) !== value.contentRef.digest) throw new Error('下载内容与原版本不一致。'); check(); const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' })); urls.add(url); const link = document.createElement('a'); link.href = url; link.download = value.fileName; link.click(); window.setTimeout(() => { URL.revokeObjectURL(url); urls.delete(url) }, 1000) }) }
async function recover(close: boolean) { const s = scope.value; if (!s) return; await run(async check => { const value = await resolveFile(s, close, check); check(); clearFiles(); notice.value = value.state === 'COMMITTED' ? '原操作已提交，请重新读取文件。' : value.state === 'CLOSED' ? '未提交操作已结束。' : '原结果仍未知，请继续查询。' }) }
async function readHistory() { const value = current.value; if (!value) return; await run(async check => { history.value = null; const saved = result(await fileContentApi.read(value.contentRef.id, historyVersion.value)); check(); history.value = saved }) }
function clear() { generation++; busy.value = false; clearPrivate(); scope.value = null; pending.value = null; error.value = notice.value = '' }
function changed() { clear(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_file_content_operation_v1:')) { try { syncPending() } catch { error.value = '原操作记录异常，请保留记录。' } } }
watch(choice, clearFiles); watch(canRead, allowed => { if (!allowed) clear() })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; generation++; clearPrivate(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.file-content{display:grid;gap:18px;padding:24px;color:var(--el-text-color-primary)}header,form,nav{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}section,article{padding:18px;border:1px solid var(--el-border-color);border-radius:8px;background:var(--el-bg-color)}article{margin:12px 0}label{display:grid;gap:6px;margin:8px 0}input,select,textarea,button{font:inherit;padding:8px;border:1px solid var(--el-border-color);border-radius:4px;color:inherit;background:var(--el-bg-color);max-width:100%;box-sizing:border-box}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}fieldset{border:0;padding:0}.digest{overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}@media(max-width:600px){.file-content{padding:12px}section,article{padding:12px}}
</style>
