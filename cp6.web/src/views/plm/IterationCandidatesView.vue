<template>
  <main class="candidates">
    <header><div><a href="/plm/engineering-iteration">返回工程项目与迭代</a><h2>迭代候选方案</h2><p v-if="iteration">{{ iteration.body.name }} · 输入版本 {{ iteration.body.iteration?.inputVersion }}</p></div><button :disabled="busy" @click="reload">重新核对当前状态</button></header>
    <p role="alert" v-if="error">{{ error }}</p><p role="status" v-if="notice">{{ notice }}</p>
    <section v-if="pending"><h3>原操作待确认</h3><p>先查询原结果；结果未明时保留记录，避免重复建立方案。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy" @click="recover(true)">结束未提交原操作</button></section>
    <template v-if="scope && iteration && can('query')">
      <p v-if="!editable">当前迭代未处于可编辑的设计阶段，或候选维护权限不可用。</p>
      <p>候选保留准确技术版本。技术预检、方案评审和冻结分别记录；预检通过不代表评审批准。</p>
      <section><h3>方案列表</h3><label>查找方案<input v-model="search" aria-label="查找方案" /></label><button :disabled="busy" @click="loadList(1)">查找</button><button v-if="can('candidate-write')" :disabled="locked || !editable" @click="newCandidate()">新建方案</button>
        <table><thead><tr><th>方案</th><th>版本</th><th>来源</th><th>操作</th></tr></thead><tbody><tr v-for="r in rows" :key="r.reference.id"><td>{{ r.original.definition.title }}</td><td>{{ r.reference.version }}</td><td>{{ driftLabel(r.sourceDrift) }}</td><td><button :disabled="busy" @click="open(r.reference.id)">打开方案</button></td></tr></tbody></table>
        <button :disabled="busy || page <= 1" @click="loadList(page - 1)">上一页</button><span>第 {{ page }} 页</span><button :disabled="busy || rows.length < 20" @click="loadList(page + 1)">下一页</button>
      </section>
      <section v-if="current"><h3>{{ current.original.definition.title }} · 版本 {{ current.reference.version }}</h3><p>{{ driftLabel(current.sourceDrift) }} · {{ current.isCurrent ? '当前方案版本' : '历史版本' }}</p>
        <p>技术预检：{{ checkLabel(current.original.definition.technicalSource.state) }}；评审：尚未提交；冻结：尚未请求。</p>
        <p>差异说明：{{ current.original.definition.changeSummary || '尚未填写' }}</p><p>风险与假设：{{ current.original.definition.assumptions || '无已记录假设' }}</p>
        <p v-if="current.original.definition.parentCandidate">父方案 {{ current.original.definition.parentCandidate.id }} · 版本 {{ current.original.definition.parentCandidate.version }}</p>
        <p v-if="current.findings.length">有 {{ current.findings.length }} 项待核对内容。请在技术检查和输入记录中核对原因。</p>
        <button v-if="can('candidate-write') && current.isCurrent" :disabled="locked || !editable" @click="editCandidate">修订方案</button><button v-if="can('candidate-write')" :disabled="locked || !editable" @click="newCandidate(current)">从此版本派生方案</button>
        <h4>准确技术内容</h4><p>工程修订 {{ current.original.definition.engineeringRevision.id }} · {{ current.original.definition.engineeringRevision.version }}；ENG 候选 {{ current.original.definition.engineeringCandidate.id }} · {{ current.original.definition.engineeringCandidate.version }}</p>
        <table><thead><tr><th>类型</th><th>发生位置</th><th>准确来源版本</th><th>适用性</th></tr></thead><tbody><tr v-for="c in current.original.definition.technicalSource.input.components" :key="c.occurrenceId"><td>{{ c.kind }}</td><td>{{ c.path }} · {{ c.occurrenceId }}</td><td>{{ c.sourceRef ? `${c.sourceRef.id} / ${c.sourceRef.version}` : '尚未选择准确来源' }}</td><td>{{ c.applicability }}</td></tr></tbody></table>
        <details><summary>版本差异</summary><h4>相对前版</h4><pre>{{ JSON.stringify(current.original.previousChanges, null, 2) }}</pre><h4>相对父方案</h4><pre>{{ JSON.stringify(current.original.parentChanges, null, 2) }}</pre></details>
        <details><summary>历史与准确版本比较</summary><label>历史版本<input v-model="historyVersion" aria-label="历史版本" pattern="[1-9][0-9]*" /></label><button :disabled="busy" @click="history">读取历史版本</button><button :disabled="busy" @click="pickComparison">设为比较起点</button><p v-if="compareFrom">起点 {{ compareFrom.id }} · 版本 {{ compareFrom.version }}</p><button :disabled="busy || !compareFrom" @click="compare">与当前所选版本比较</button><pre v-if="comparison">{{ JSON.stringify(comparison.changes, null, 2) }}</pre></details>
        <details><summary>原始记录与技术清单摘要</summary><p>方案原件摘要：{{ current.reference.digest }}</p><p>技术清单摘要：{{ current.original.definition.technicalSource.manifest?.digest || '技术清单不完整' }}</p><pre>{{ current.originalJson }}</pre></details>
      </section>
      <form v-if="draft" @submit.prevent="save"><fieldset :disabled="locked || !editable"><h3>{{ editing ? '修订方案' : '新建方案' }}</h3>
        <label>方案名称<input v-model="draft.title" aria-label="方案名称" required /></label><label>差异说明<textarea v-model="draft.changeSummary" aria-label="差异说明" /></label><label>风险与假设<textarea v-model="draft.assumptions" aria-label="风险与假设" /></label>
        <p v-if="parent">固定父方案：{{ parent.id }} · 版本 {{ parent.version }}</p>
        <h4>选择准确工程技术记录</h4><label>查找工程修订<input v-model="revisionSearch" aria-label="查找工程修订" /></label><button type="button" @click="findRevisions(1)">查找工程修订</button>
        <label>工程修订<select v-model="revisionId" aria-label="工程修订"><option value="">请选择</option><option v-for="r in revisions" :key="r.reference.id" :value="r.reference.id">{{ r.revisionCode }} · {{ r.draft.goal }} · 版本 {{ r.reference.version }}</option></select></label>
        <button type="button" :disabled="revisionPage <= 1" @click="findRevisions(revisionPage - 1)">工程修订上一页</button><button type="button" :disabled="revisions.length < 20" @click="findRevisions(revisionPage + 1)">工程修订下一页</button><button type="button" :disabled="!revisionId" @click="findChecks(1)">读取技术检查记录</button>
        <label>准确检查记录<select v-model="checkId" aria-label="准确检查记录"><option value="">请选择</option><option v-for="c in checkChoices" :key="c.reference.id" :value="c.reference.id">{{ c.original.checkedAt }} · 工程版本 {{ c.original.command.revision.version }} · {{ checkLabel(c.original.state) }} · {{ c.freshness === 'CURRENT' ? '当前来源' : '来源有变化' }}</option></select></label>
        <button type="button" :disabled="checkPage <= 1" @click="findChecks(checkPage - 1)">检查记录上一页</button><button type="button" :disabled="checkChoices.length < 20" @click="findChecks(checkPage + 1)">检查记录下一页</button><button type="button" :disabled="!checkId" @click="chooseCheck">核对并选择检查原件</button>
        <p v-if="selectedCheck">已选择工程版本 {{ selectedCheck.original.command.revision.version }}，预检 {{ checkLabel(selectedCheck.original.state) }}。{{ selectedCheck.freshness === 'STALE' ? '来源已变化，需明确保留原因。' : '保存时再次核对当前来源。' }}</p>
        <label>来源处理<select v-model="draft.sourceDecision" aria-label="来源处理"><option value="ADOPT_CURRENT">采用当前检查原件</option><option value="KEEP_ORIGINAL">明确保留原版本供比较</option></select></label><label v-if="draft.sourceDecision === 'KEEP_ORIGINAL'">保留原版原因<textarea v-model="draft.driftReason" aria-label="保留原版原因" required /></label>
        <label>操作原因<textarea v-model="draft.reason" aria-label="操作原因" required /></label><button :disabled="!selectedCheck" type="submit">保存方案版本</button>
      </fieldset></form>
    </template>
  </main>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { usePermissionStore } from '@/stores/permission'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { engineeringIterationApi, type EngineeringIterationScope, type EngineeringIterationSnapshot } from '@/api/plm/engineeringIteration'
import { revisionApi, type RevisionSnapshot } from '@/api/plm/engineeringRevisions'
import { sourceCheckApi, type CheckView } from '@/api/plm/engineeringSourceCheck'
import type { EngineeringReference } from '@/api/plm/engineeringProducts'
import { candidateApi as api, type CandidateCommand, type CandidateSnapshot, type CandidateDiff } from '@/api/plm/iterationCandidate'
import { candidateResponse as response, pendingCandidate, resolveCandidate, saveCandidate, type PendingCandidate } from '@/auth/iterationCandidateRecovery'
const props = defineProps<{ iterationId: string }>(), permission = usePermissionStore(), can = (action: string) => permission.loaded && permission.has(`plm-engineering-iteration:${action}`)
const scope = ref<EngineeringIterationScope | null>(null), iteration = ref<EngineeringIterationSnapshot | null>(null), rows = ref<CandidateSnapshot[]>([]), current = ref<CandidateSnapshot | null>(null)
const busy = ref(false), pending = ref<PendingCandidate | null>(null), error = ref(''), notice = ref(''), search = ref(''), page = ref(1), locked = computed(() => busy.value || !!pending.value)
const editable = computed(() => can('candidate-write') && iteration.value?.body.iteration?.stage === 'IN_DESIGN' && iteration.value?.body.iteration?.holdState === 'NORMAL')
type Draft = Pick<CandidateCommand, 'title' | 'changeSummary' | 'assumptions' | 'sourceDecision' | 'driftReason' | 'reason'>
const draft = ref<Draft | null>(null), editing = ref<CandidateSnapshot | null>(null), candidateId = ref(''), parent = ref<EngineeringReference | null>(null), selectedCheck = ref<CheckView | null>(null)
const revisionSearch = ref(''), revisions = ref<RevisionSnapshot[]>([]), revisionId = ref(''), revisionPage = ref(1), checkChoices = ref<CheckView[]>([]), checkId = ref(''), checkPage = ref(1)
const historyVersion = ref('1'), compareFrom = ref<EngineeringReference | null>(null), comparison = ref<CandidateDiff | null>(null)
let generation = 0, live = true
const copy = <T,>(x: T): T => JSON.parse(JSON.stringify(x)) as T
const driftLabel = (x: string) => ({ UNCHANGED: '准确来源未变化', CHANGED: '来源已变化', UNKNOWN: '来源状态不可确认' }[x] || '需核对来源')
const checkLabel = (x: string) => ({ PASS: '通过', BLOCK: '有阻塞项', UNKNOWN: '不可确认' }[x] || '待检查')
function clearEditor() { draft.value = null; editing.value = null; candidateId.value = ''; parent.value = null; selectedCheck.value = null; revisions.value = []; revisionId.value = ''; checkChoices.value = []; checkId.value = '' }
function clearPrivate() { clearEditor(); scope.value = null; iteration.value = null; current.value = null; rows.value = []; pending.value = null; comparison.value = null; compareFrom.value = null; notice.value = '' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const stamp = identityStamp(), epoch = generation
  const check = () => { requireIdentity(stamp); if (!live || epoch !== generation || !authenticationCanRead()) throw new Error('身份已变化，请重新载入。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); await action(check); check() }
  catch (e: any) { try { check(); if (e?.response?.status === 403) clearPrivate(); const message = e?.response?.data?.message || e?.message || ''; error.value = /^[A-Z][A-Z0-9_]+$/.test(message) ? '无法确认当前来源、权限或版本，请重新核对。' : message || '暂时无法完成。' } catch { /* discard obsolete identity response */ } }
  finally { try { check(); pending.value = scope.value ? pendingCandidate(scope.value) : null; busy.value = false } catch { if (live && epoch === generation) busy.value = false } }
}
async function reload() { await run(async check => { clearPrivate(); if (!permission.loaded) await permission.loadMyActions(); check(); if (!can('query')) throw new Error('当前无权读取迭代候选方案。'); const s = response(await engineeringIterationApi.scope()); check(); scope.value = s; const i = response(await engineeringIterationApi.read(props.iterationId)); check(); if (!i.body.iteration) throw new Error('请选择工程迭代。'); iteration.value = i; const values = response(await api.list(props.iterationId, search.value, 1)); check(); rows.value = values; page.value = 1 }) }
async function loadList(p: number) { await run(async check => { rows.value = []; const values = response(await api.list(props.iterationId, search.value, p)); check(); rows.value = values; page.value = p }) }
async function open(id: string, version?: string) { await run(async check => { current.value = null; clearEditor(); const value = response(await api.read(props.iterationId, id, version)); check(); current.value = value }) }
function newCandidate(from?: CandidateSnapshot) { if (locked.value || !editable.value) return; clearEditor(); candidateId.value = crypto.randomUUID(); parent.value = from ? copy(from.reference) : null; draft.value = { title: '', changeSummary: '', assumptions: '', sourceDecision: 'ADOPT_CURRENT', driftReason: '', reason: '' } }
function editCandidate() { if (!current.value?.isCurrent || locked.value || !editable.value) return; const v = copy(current.value), d = v.original.definition; clearEditor(); editing.value = v; candidateId.value = v.reference.id; parent.value = d.parentCandidate; draft.value = { title: d.title, changeSummary: d.changeSummary, assumptions: d.assumptions, sourceDecision: 'ADOPT_CURRENT', driftReason: '', reason: '' }; selectedCheck.value = { reference: d.sourceCheck, original: d.technicalSource, originalJson: d.technicalSourceOriginal, freshness: v.sourceDrift === 'UNCHANGED' ? 'CURRENT' : 'STALE', changes: v.findings, freeze: 'NOT_REQUESTED' } }
async function findRevisions(p: number) { await run(async check => { revisions.value = []; revisionId.value = ''; checkChoices.value = []; checkId.value = ''; const values = response(await revisionApi.list(revisionSearch.value, p)); check(); revisions.value = values; revisionPage.value = p }) }
async function findChecks(p: number) { await run(async check => { checkChoices.value = []; checkId.value = ''; if (!revisions.value.some(x => x.reference.id === revisionId.value)) return; const values = response(await sourceCheckApi.history(revisionId.value, p)); check(); checkChoices.value = values; checkPage.value = p }) }
async function chooseCheck() { await run(async check => { const chosen = checkChoices.value.find(x => x.reference.id === checkId.value); if (!chosen) return; selectedCheck.value = null; const value = response(await sourceCheckApi.read(chosen.reference.id)); check(); if (JSON.stringify(value.reference) !== JSON.stringify(chosen.reference)) throw new Error('检查原件与所选准确引用不一致。'); selectedCheck.value = value }) }
async function save() { await run(async check => { if (!scope.value || !draft.value || !selectedCheck.value || !iteration.value || !editable.value || pending.value) return; const d = copy(draft.value); if (!d.reason.trim() || !d.title.trim() || d.sourceDecision === 'KEEP_ORIGINAL' && !d.driftReason.trim()) throw new Error('请填写方案名称和本次操作依据。'); if (d.sourceDecision === 'ADOPT_CURRENT' && selectedCheck.value.freshness !== 'CURRENT') throw new Error('来源已变化，请选择新的准确检查记录或明确保留原版原因。'); const command: Omit<CandidateCommand, 'operationId'> = { ...d, driftReason: d.sourceDecision === 'ADOPT_CURRENT' ? '' : d.driftReason, iterationId: props.iterationId, candidateId: candidateId.value, action: editing.value ? 'REVISE' : 'CREATE', expectedIteration: copy(iteration.value.reference), expectedIterationEtag: iteration.value.etag, expectedVersion: editing.value?.reference.version ?? null, expectedEtag: editing.value?.original.etag ?? null, parentCandidate: copy(parent.value), sourceCheck: copy(selectedCheck.value.reference) }; const value = await saveCandidate(scope.value, command.iterationId, command.candidateId, check, operationId => api.save({ ...command, operationId })); check(); if (value.result) { const saved = response(await api.read(value.result.iterationId, value.result.candidateId, value.result.version)); check(); current.value = saved; clearEditor(); const values = response(await api.list(props.iterationId, search.value, page.value)); check(); rows.value = values; notice.value = '方案原件已保存，评审与冻结状态保持独立。' } }) }
async function recover(close: boolean) { await run(async check => { if (!scope.value) return; const value = await resolveCandidate(scope.value, close, check); check(); if (value.result && value.result.iterationId === props.iterationId) { const v = response(await api.read(value.result.iterationId, value.result.candidateId, value.result.version)); check(); current.value = v; clearEditor() } notice.value = value.state === 'UNKNOWN' ? '原结果仍未确认，请保留记录。' : '原操作已确认。' }) }
function history() { if (current.value) return open(current.value.reference.id, historyVersion.value) }
function pickComparison() { if (current.value) { compareFrom.value = copy(current.value.reference); comparison.value = null } }
async function compare() { await run(async check => { comparison.value = null; if (!current.value || !compareFrom.value) return; const value = response(await api.compare(props.iterationId, compareFrom.value, current.value.reference)); check(); comparison.value = value }) }
function invalidate() { generation++; busy.value = false; error.value = ''; clearPrivate() }
watch(() => [permission.loaded, can('query')], () => { if (!can('query')) invalidate() })
watch(() => props.iterationId, () => { invalidate(); void reload() })
onMounted(() => { window.addEventListener(AUTH_EVENT, invalidate); void reload() })
onBeforeUnmount(() => { live = false; generation++; window.removeEventListener(AUTH_EVENT, invalidate) })
</script>
<style scoped>
.candidates{max-width:1200px;margin:auto;padding:24px;color:var(--el-text-color-primary,#253047)}header{display:flex;justify-content:space-between;gap:16px;flex-wrap:wrap}section,form{border:1px solid var(--el-border-color,#d8dce3);border-radius:8px;padding:18px;margin:18px 0}label{display:flex;flex-direction:column;gap:6px;margin:12px 0}input,textarea,select,button{font:inherit;padding:8px;border:1px solid #c4cbd6;border-radius:5px}textarea{min-height:70px}button{margin:5px;cursor:pointer}button:disabled{opacity:.55;cursor:default}fieldset{border:0;padding:0;min-width:0}table{width:100%;text-align:left;border-collapse:collapse}th,td{padding:9px;border-bottom:1px solid #d8dce3;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere}p{overflow-wrap:anywhere}[role=alert]{color:#b42318}[role=status]{color:#167044}@media(max-width:640px){.candidates{padding:12px}section,form{padding:12px}}
</style>
