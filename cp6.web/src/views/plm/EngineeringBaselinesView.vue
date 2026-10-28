<template>
  <main class="baseline-page" :aria-busy="busy">
    <header><div><h1>工程基线</h1><p>选择已应用专业评审的准确候选，核对技术检查后冻结。冻结事实与当前来源资格分别显示。</p></div><button :disabled="busy" @click="reload">刷新</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p><p v-if="!canRead">当前没有工程基线读取权限。</p>
    <section v-if="pending"><h2>原操作结果待确认</h2><p>请保留原操作，查询或结束后再继续。</p><button :disabled="busy" @click="recover(false)">查询原操作</button><button :disabled="busy || pending.action === 'FREEZE' && !has('cancel')" @click="recover(true)">结束未提交操作</button></section>
    <section v-if="canRead"><h2>已冻结基线</h2><nav><button v-for="row in rows" :key="row.reference.id" :disabled="busy" @click="open(row.reference.id)">{{ row.original.revision.revisionCode }} · v{{ row.original.command.revision.version }} · {{ row.original.frozenAt }}</button></nav><button :disabled="busy || page === 1" @click="loadList(page - 1)">上一页</button><button :disabled="busy || rows.length < 20" @click="loadList(page + 1)">下一页</button></section>
    <section v-if="canRead && has('freeze')"><h2>冻结已评审候选</h2><form @submit.prevent="findIterations(1)"><label>迭代名称<input v-model="search" aria-label="迭代搜索" /></label><button :disabled="busy">查找迭代</button></form>
      <label>实际迭代<select v-model="iterationId" aria-label="实际迭代" :disabled="locked" @change="loadChoices"><option value="">请选择</option><option v-for="it in iterations" :key="it.reference.id" :value="it.reference.id">{{ it.body.name }} · {{ it.body.number }}</option></select></label>
      <button :disabled="busy || iterationPage === 1" @click="findIterations(iterationPage - 1)">上一页迭代</button><button :disabled="busy || iterations.length < 20" @click="findIterations(iterationPage + 1)">下一页迭代</button>
      <label>准确评审候选<select v-model="selected" aria-label="准确评审候选" :disabled="locked"><option value="">请选择</option><option v-for="(item, i) in candidates" :key="i" :value="String(i)">{{ item.candidate.original.definition.title }} · 候选v{{ item.candidate.reference.version }} · {{ item.choice.application.receipt?.application === 'APPLIED' ? '评审已应用' : '尚未应用' }}</option></select></label>
      <div v-if="selection"><p>技术检查：{{ selection.candidate.original.definition.technicalSource.state === 'PASS' ? '已通过，提交时重新核对' : '尚未通过' }}；评审应用：{{ eligible ? '已批准并应用' : '尚不满足冻结前置' }}；冻结：待提交。</p><p>工程候选 v{{ selection.candidate.original.definition.engineeringCandidate.version }}。原始技术摘要与评审包摘要分别固定。</p><details><summary>准确技术与评审依据</summary><pre>{{ selection.candidate.original.definition.technicalSource.manifest?.originalJson }}</pre><p>技术 {{ selection.candidate.original.definition.technicalSource.manifest?.digest }}</p><p>评审 {{ selection.choice.submission.package.digest }}</p></details></div>
      <label>冻结原因<textarea v-model="reason" aria-label="冻结原因" :disabled="locked" /></label><button :disabled="locked || !eligible || !reason.trim()" @click="freeze">冻结准确候选</button>
    </section>
    <section v-if="current"><h2>{{ current.original.revision.revisionCode }} · 已冻结原件</h2><p>冻结：已固定；来源资格：{{ current.sourceStatus === 'CURRENT' ? '当前有效' : '已有变化，请核对' }}；发布：未申请；用途验证：未申请。</p><p>冻结不授予生产、库存或发布资格。</p><details v-if="current.changes.length"><summary>来源变化依据</summary><ul><li v-for="change in current.changes" :key="change">{{ change }}</li></ul></details>
      <p>输出关系：{{ current.original.outputApplicability === 'APPLIES' ? '已固定准确关系与绑定原件' : '本候选按已批准分类政策不适用' }}。</p>
      <details><summary>完整冻结原件</summary><pre>{{ current.originalJson }}</pre></details><details v-if="current.outputBindingOriginal"><summary>输出绑定原件</summary><pre>{{ current.outputBindingOriginal }}</pre></details>
      <button :disabled="busy" @click="whereUsed">读取引用情况</button><ul><li v-for="use in uses" :key="use.owner">{{ use.owner }}：{{ use.status === 'UNKNOWN' ? '覆盖范围未知' : '已读取原生后继引用' }}（{{ use.references.length }}）</li></ul>
      <label>同修订历史基线<select v-model="compareId" aria-label="比较基线" :disabled="busy"><option value="">请选择</option><option v-for="row in comparable" :key="row.reference.id" :value="row.reference.id">v{{ row.original.command.revision.version }} · {{ row.original.frozenAt }}</option></select></label><button :disabled="busy || !compareId" @click="compare">比较准确原件</button><pre v-if="diff">{{ diff }}</pre>
      <div v-if="has('successor')"><label>后继设计原因<textarea v-model="successorReason" aria-label="后继设计原因" :disabled="locked" /></label><button :disabled="locked || !successorReason.trim()" @click="successor">开始后继设计</button><p>后继工作版本保留父基线，重新取得技术检查和专业评审资格。</p></div>
    </section>
  </main>
</template>
<script setup lang="ts">
import { computed, ref, onMounted, onBeforeUnmount, watch } from 'vue'
import { baselineApi, type BaselineScope, type BaselineView, type FreezeChoice, type FreezeCommand, type FreezeResult, type BaselineUsage } from '@/api/plm/engineeringBaselines'
import { engineeringIterationApi, type EngineeringIterationSnapshot } from '@/api/plm/engineeringIteration'
import { baselineResponse as result, pendingBaseline, saveBaseline, resolveBaseline, type PendingBaseline } from '@/auth/baselineRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const permissions = usePermissionStore(), has = (action: string) => permissions.has(`plm-engineering-baseline:${action}`), canRead = computed(() => permissions.loaded && has('query'))
const scope = ref<BaselineScope | null>(null), pending = ref<PendingBaseline | null>(null), busy = ref(false), error = ref(''), notice = ref('')
const rows = ref<BaselineView[]>([]), current = ref<BaselineView | null>(null), page = ref(1), iterations = ref<EngineeringIterationSnapshot[]>([]), iterationPage = ref(1), search = ref(''), iterationId = ref(''), choices = ref<FreezeChoice[]>([]), selected = ref(''), reason = ref(''), successorReason = ref(''), uses = ref<BaselineUsage[]>([]), compareId = ref(''), diff = ref('')
const candidates = computed(() => choices.value.flatMap(choice => choice.candidates.map(candidate => ({ choice, candidate }))))
const selection = computed(() => selected.value === '' ? null : candidates.value[Number(selected.value)])
const eligible = computed(() => { const v = selection.value; return !!v && v.choice.application.businessStage === 'REVIEW_APPROVED' && v.choice.application.decision === 'APPROVED' && v.choice.application.receipt?.application === 'APPLIED' && v.choice.application.receipt.outcomeCode === 'ITERATION_APPROVED' && v.candidate.original.definition.technicalSource.state === 'PASS' })
const locked = computed(() => busy.value || !!pending.value || !scope.value), comparable = computed(() => rows.value.filter(x => x.reference.id !== current.value?.reference.id && x.original.command.revision.id === current.value?.original.command.revision.id))
let live = true, epoch = 0
function clearPrivate() { rows.value = []; current.value = null; iterations.value = []; choices.value = []; selected.value = iterationId.value = reason.value = successorReason.value = compareId.value = diff.value = ''; uses.value = [] }
function verify(v: BaselineView) { if (!scope.value || v.original.environmentId !== scope.value.environmentId || v.original.tenantId !== scope.value.tenantId || v.original.evidenceMode !== scope.value.evidenceMode) throw new Error('原件身份范围不一致。') }
async function run(body: (check: () => void) => Promise<void>) {
  if (busy.value) return; const stamp = identityStamp(), generation = epoch
  const check = () => { requireIdentity(stamp); if (!live || generation !== epoch || !authenticationCanRead() || !canRead.value) throw new Error('当前身份或权限已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); await body(check); check() } catch (e: any) { try { check(); if ([401, 403].includes(e?.response?.status)) clearPrivate(); error.value = e?.response?.status ? '当前来源、评审或权限不满足要求。请查询原操作并重新核对。' : e?.message || '结果未确认。' } catch { /* obsolete identity */ } }
  finally { try { check(); pending.value = scope.value ? pendingBaseline(scope.value) : null } catch { /* durable recovery retained */ } if (live && epoch === generation) busy.value = false }
}
async function list(p: number, check: () => void) { const v = result(await baselineApi.list(p)); check(); v.forEach(verify); rows.value = v; page.value = p }
async function reload() { await run(async check => { check(); clearPrivate(); const s = result(await baselineApi.scope()); check(); scope.value = s; await list(1, check) }) }
async function loadList(p: number) { await run(check => list(p, check)) }
async function open(id: string) { await run(async check => { current.value = null; uses.value = []; diff.value = compareId.value = ''; const v = result(await baselineApi.read(id)); check(); verify(v); current.value = v }) }
async function findIterations(p: number) { await run(async check => { const v = result(await engineeringIterationApi.list('ITERATION', undefined, search.value, p)); check(); iterations.value = v; iterationPage.value = p }) }
async function loadChoices() { choices.value = []; selected.value = ''; const id = iterationId.value; if (id) await run(async check => { const v = result(await baselineApi.choices(id)); check(); if (id === iterationId.value) choices.value = v }) }
async function freeze() {
  const s = scope.value, item = selection.value; if (!s || !item || locked.value || !eligible.value || !reason.value.trim() || !has('freeze')) return
  const d = item.candidate.original.definition, source = d.technicalSource
  if (!source.input.configuration || !source.manifest) return
  const command: Omit<FreezeCommand, 'operationId'> = { iterationId: item.choice.iterationId, sourceCandidate: item.candidate.reference, reviewSubmissionId: item.choice.submission.submissionId, reviewRoundId: item.choice.submission.roundId, applicationReceiptId: item.choice.application.receipt!.receiptId, revision: d.engineeringRevision, engineeringCandidate: d.engineeringCandidate, expectedEtag: source.command.expectedEtag, configuration: source.input.configuration.reference, sourceCheck: d.sourceCheck, technicalManifestDigest: source.manifest.digest, reviewPackageDigest: item.choice.submission.package.digest, reason: reason.value }
  await run(async check => {
    const receipt = await saveBaseline(s, 'FREEZE', item.candidate.reference.id, check, id => baselineApi.freeze({ ...command, operationId: id })); check()
    const accepted = receipt.result as FreezeResult, detail = result(await baselineApi.read(accepted.baseline.id)); check(); verify(detail)
    if (detail.reference.id !== accepted.baseline.id || detail.reference.owner !== accepted.baseline.owner || detail.reference.version !== accepted.baseline.version || detail.reference.digest !== accepted.baseline.digest) throw new Error('读回原件与冻结回执不一致。')
    current.value = detail; choices.value = []; selected.value = ''; await list(1, check); notice.value = '工程基线已固定，准确原件已读回。'
  })
}
async function successor() { const s = scope.value, v = current.value; if (!s || !v || locked.value || !has('successor') || !successorReason.value.trim()) return; const reason = successorReason.value; await run(async check => { await saveBaseline(s, 'SUCCESSOR', v.reference.id, check, id => baselineApi.successor(id, v, reason)); check(); current.value = null; notice.value = '后继工作版本已建立，请在工程修订中继续设计并重新检查、评审。' }) }
async function recover(close: boolean) { const s = scope.value; if (!s) return; await run(async check => { const v = await resolveBaseline(s, close, check); check(); clearPrivate(); await list(1, check); notice.value = v.state === 'UNKNOWN' ? '结果仍未知，请继续查询。' : v.state === 'COMMITTED' ? '原操作已提交，请打开原件。' : '未提交操作已结束。' }) }
async function whereUsed() { const id = current.value?.reference.id; if (id) await run(async check => { const v = result(await baselineApi.usage(id)); check(); uses.value = v }) }
async function compare() { const id = current.value?.reference.id; if (id && compareId.value) await run(async check => { const v = result(await baselineApi.diff(compareId.value, id)); check(); diff.value = JSON.stringify(v.changes, null, 2) }) }
function changed() { epoch++; busy.value = false; clearPrivate(); scope.value = null; pending.value = null; error.value = notice.value = ''; if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_engineering_baseline_operation_v1:')) { try { pending.value = scope.value ? pendingBaseline(scope.value) : null } catch { error.value = '原操作记录异常，请保留记录。' } } }
watch(canRead, v => { if (!v) { epoch++; clearPrivate(); scope.value = null; pending.value = null } })
onMounted(async () => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); if (!permissions.loaded) await permissions.loadMyActions(); if (live) void reload() })
onBeforeUnmount(() => { live = false; epoch++; clearPrivate(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.baseline-page{display:grid;gap:18px;padding:24px;color:var(--el-text-color-primary)}header,nav{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}section{padding:18px;border:1px solid var(--el-border-color);border-radius:8px;background:var(--el-bg-color)}label{display:grid;gap:6px;margin:8px 0}button,input,select,textarea{font:inherit;padding:8px;max-width:100%;border:1px solid var(--el-border-color);border-radius:4px;background:var(--el-bg-color);color:inherit;box-sizing:border-box}button{cursor:pointer;margin:4px}button:disabled{opacity:.5;cursor:default}p,pre,li{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}@media(max-width:600px){.baseline-page{padding:12px}section{padding:12px}}
</style>
