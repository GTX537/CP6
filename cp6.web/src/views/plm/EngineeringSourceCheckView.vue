<template>
  <section aria-label="工程完整性检查" :aria-busy="busy">
    <h2>工程完整性检查</h2>
    <p>检查当前已保存的候选、批准政策和准确来源。检查结果保留原记录；冻结时仍须重新核对当前来源、评审和权限。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <p v-if="!revision.draft.configuration?.input.ruleRef">请先在工作配置中选择并保存已批准的工程检查政策。</p>
    <label>检查原因<textarea v-model="reason" aria-label="工程检查原因" :disabled="locked" /></label>
    <OutputPicker v-if="permissions.has('plm-engineering-output-relations:query')" v-model="outputReference" kind="relations" label="准确输出关系" :revision-id="revision.reference.id" :disabled="locked" optional />
    <button :disabled="locked || !canEdit || !revision.draft.configuration?.input.ruleRef" @click="start">检查当前候选</button>
    <button :disabled="busy || !canRead" @click="history(1)">读取检查记录</button>
    <div v-if="pending"><p>有一次检查的提交结果尚未确认。请查询原操作。</p><button :disabled="busy" @click="recover(false)">查询原检查</button><button :disabled="busy" @click="recover(true)">结束未提交检查</button></div>
    <div v-if="rows.length"><button v-for="row in rows" :key="row.reference.id" :disabled="busy" @click="open(row.reference.id)">{{ row.original.checkedAt }} · {{ state(row.original.state) }}</button>
      <button :disabled="busy || page === 1" @click="history(page - 1)">较新记录</button><button :disabled="busy || rows.length < 20" @click="history(page + 1)">更早记录</button>
    </div>
    <div v-if="selected">
      <h3>原检查：{{ state(selected.original.state) }}</h3>
      <p>{{ selected.freshness === 'CURRENT' ? '来源水位与当前观察相符。' : '当前来源或候选已变化，此原检查已失效。' }} 尚未冻结，未取得用途许可。</p>
      <p>候选版本 {{ selected.original.command.candidate.version }} · 政策版本 {{ selected.original.command.policy.version }} · {{ selected.original.checkedAt }}</p>
      <p v-if="selected.original.manifest">技术内容摘要：{{ selected.original.manifest.digest }}</p>
      <table><thead><tr><th>检查位置</th><th>结果</th><th>依据或缺项</th></tr></thead><tbody><tr v-for="(item, index) in selected.original.items" :key="index"><td>{{ item.path }}</td><td>{{ state(item.state) }}</td><td>{{ item.reason || finding(item.code) }}<details><summary>检查代码</summary>{{ item.code }}</details></td></tr></tbody></table>
      <details><summary>准确来源与检查水位</summary><div v-for="source in selected.original.sources" :key="source.path"><p>{{ source.path }} · {{ source.reference.owner }} · {{ source.reference.id }} · v{{ source.reference.version }}</p><p>水位 {{ source.watermark }}</p></div></details>
      <details><summary>本次原始输入与政策</summary><pre>{{ JSON.stringify({ input: selected.original.input, policy: selected.original.policyBody }, null, 2) }}</pre></details>
    </div>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { RevisionSnapshot } from '@/api/plm/engineeringRevisions'
import type { EngineeringScope, EngineeringReference } from '@/api/plm/engineeringProducts'
import OutputPicker from './EngineeringOutputSourcePicker.vue'
import { sourceCheckApi, type CheckView } from '@/api/plm/engineeringSourceCheck'
import { pendingSourceCheck, resolveSourceCheck, saveSourceCheck, sourceCheckResponse, type PendingSourceCheck } from '@/auth/engineeringCheckRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ revision: RevisionSnapshot; blocked?: boolean }>()
const emit = defineEmits<{ pending: [value: boolean] }>()
const permissions = usePermissionStore(), canRead = computed(() => permissions.loaded && permissions.has('plm-engineering-revision:query'))
const canEdit = computed(() => canRead.value && permissions.has('plm-engineering-revision:edit'))
const busy = ref(false), error = ref(''), notice = ref(''), reason = ref(''), page = ref(1)
const scope = ref<EngineeringScope | null>(null), pending = ref<PendingSourceCheck | null>(null)
const rows = ref<CheckView[]>([]), selected = ref<CheckView | null>(null)
const outputReference = ref<EngineeringReference | null>(null)
const locked = computed(() => busy.value || !!props.blocked || !!pending.value)
let live = true, generation = 0
const state = (s: string) => ({ PASS: '通过', BLOCK: '阻塞', UNKNOWN: '未知', NOT_APPLICABLE: '不适用' }[s] || s)
const finding = (code: string) => ({ EXACT_NATIVE_SOURCE: '已读取准确原生来源', EXACT_CANDIDATE: '候选身份一致', SOURCE_ACCESS_DENIED: '当前无权读取来源', REQUIRED_OCCURRENCES_MISSING: '缺少政策要求的组成位置', OPTIONS_VALIDATED: '选项符合政策', CHECK_POLICY_CHANGED: '政策已失效或来源变化' }[code] || '请核对该位置的准确来源与资格。')
function message(e: any) { const c = e?.response?.data?.message || e?.message || ''; const known: Record<string, string> = { ENGINEERING_CHECK_POLICY_REQUIRED: '所选政策仅覆盖选项，请先明确工程完整性政策并批准。', ENGINEERING_CHECK_INPUT_CHANGED: '候选已变化，请刷新工程修订。', OBJECT_FORBIDDEN: '当前无权读取检查涉及的来源。', OPERATIONS_OUTCOME_UNKNOWN: '提交结果尚未确认，请查询原检查。' }; return (known[c] || (/^[A-Z][A-Z0-9_]+$/.test(c) ? '检查未完成，请核对当前来源或查询原操作。' : c)) || '暂时无法完成检查。' }
function sync() { pending.value = scope.value ? pendingSourceCheck(scope.value) : null; emit('pending', !!pending.value) }
function clear() { generation++; busy.value = false; scope.value = null; rows.value = []; selected.value = null; outputReference.value = null; pending.value = null; error.value = ''; notice.value = ''; reason.value = ''; emit('pending', false) }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const g = generation, stamp = identityStamp(), revision = props.revision.reference.digest
  const check = () => { requireIdentity(stamp); if (!live || g !== generation || props.revision.reference.digest !== revision || !authenticationCanRead() || !canRead.value) throw new Error('当前身份或修订已变化，请重新载入。') }
  busy.value = true; error.value = ''; notice.value = ''
  try { check(); await action(check); check() } catch (e) { if (live && g === generation) { error.value = message(e); selected.value = null; rows.value = [] } }
  finally { if (live && g === generation) { busy.value = false; try { sync() } catch (e) { error.value = message(e) } } }
}
async function loadScope(check: () => void) { const s = sourceCheckResponse(await sourceCheckApi.scope()); check(); scope.value = s; sync(); return s }
async function init() { await run(async check => { await loadScope(check) }) }
async function start() {
  if (locked.value || !canEdit.value) return
  const revision = JSON.parse(JSON.stringify(props.revision.reference)), candidate = JSON.parse(JSON.stringify(props.revision.candidateRef)), expectedEtag = props.revision.etag
  const policy = props.revision.draft.configuration?.input.ruleRef
  if (!policy || !reason.value.trim()) { error.value = '请选择准确政策并填写检查原因。'; return }
  const frozenPolicy = JSON.parse(JSON.stringify(policy)), why = reason.value
  const outputRelations = outputReference.value ? JSON.parse(JSON.stringify(outputReference.value)) : undefined
  await run(async check => {
    const s = await loadScope(check)
    const result = await saveSourceCheck(s, revision.id, () => { check(); if (!canEdit.value || props.blocked) throw new Error('当前不能提交检查。') }, operationId => sourceCheckApi.save({ operationId, revision, candidate, expectedEtag, policy: frozenPolicy, reason: why, ...(outputRelations ? { outputRelations } : {}) }))
    check(); if (result.result) { const value = sourceCheckResponse(await sourceCheckApi.read(result.result.checkRunId)); check(); selected.value = value }
  })
}
async function history(target: number) { await run(async check => { await loadScope(check); const values = sourceCheckResponse(await sourceCheckApi.history(props.revision.reference.id, target)); check(); rows.value = values; selected.value = null; page.value = target }) }
async function open(id: string) { await run(async check => { selected.value = null; const value = sourceCheckResponse(await sourceCheckApi.read(id)); check(); selected.value = value }) }
async function recover(close: boolean) { await run(async check => { const s = await loadScope(check); const value = await resolveSourceCheck(s, close, check); check(); notice.value = value.state === 'UNKNOWN' ? '仍未确认，请继续查询原操作。' : value.state === 'CLOSED' ? '未提交检查已结束。' : '已找到原检查结果。'; if (value.result) { const v = sourceCheckResponse(await sourceCheckApi.read(value.result.checkRunId)); check(); selected.value = v } }) }
function identityChanged() { clear(); if (authenticationCanRead() && canRead.value) void init() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_engineering_check_operation_v1:')) { try { sync() } catch (e) { error.value = message(e) } } }
watch(() => props.revision.reference.digest, () => { clear(); if (canRead.value) void init() })
watch(canRead, value => { if (!value) clear() })
onMounted(() => { window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', storage); void init() })
onBeforeUnmount(() => { live = false; clear(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', storage) })
</script>
<style scoped>
section { padding: 18px; border: 1px solid var(--el-border-color); border-radius: 8px; overflow: auto; }
label { display: grid; gap: 6px; } button { margin: 6px; padding: 7px 12px; } table { width: 100%; border-collapse: collapse; }
td, th { text-align: left; padding: 8px; border-bottom: 1px solid var(--el-border-color); } pre { white-space: pre-wrap; overflow-wrap: anywhere; } [role=alert] { color: var(--el-color-danger); }
</style>
