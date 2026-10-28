<template>
  <section :aria-busy="busy" class="iteration-review">
    <header><h2>迭代送审</h2><button :disabled="busy || disabled" @click="reload">刷新送审状态</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>原送审操作结果待确认，请先查询。</p><button :disabled="busy || disabled" @click="recover(false)">查询原评审操作</button><button :disabled="busy || disabled" @click="recover(true)">结束未提交评审操作</button></div>
    <template v-if="context">
      <p>迭代 {{ context.iteration.body.name }} · 输入版本 {{ context.iteration.body.iteration?.inputVersion }}</p>
      <p v-if="!sameVersion" role="alert">迭代已有新版本，请重新读取迭代后送审。</p>
      <h3>完整候选清单（{{ context.candidates.length }} 项）</h3>
      <p>送审包含当前迭代的全部候选方案，固定每项版本、原始技术依据和差异。</p>
      <article v-for="candidate in context.candidates" :key="candidate.reference.id">
        <h4>{{ candidate.original.definition.title }} · 版本 {{ candidate.reference.version }}</h4>
        <p>{{ candidate.original.definition.changeSummary }}</p><p>{{ candidate.original.definition.assumptions }}</p>
        <p>技术预检：{{ candidate.original.definition.technicalSource.state }} · {{ candidate.sourceDrift === 'UNCHANGED' ? '来源未变化' : '来源需重新核对' }}</p>
        <ul v-if="candidate.findings.length"><li v-for="item in candidate.findings" :key="item">{{ item }}</li></ul>
        <details><summary>查看准确技术来源与差异</summary><pre>{{ candidate.original.definition.technicalSourceOriginal }}</pre><pre>{{ candidate.original.previousChanges }}</pre><pre>{{ candidate.original.parentChanges }}</pre></details>
      </article>
      <p v-if="!ready" role="alert">候选、来源或迭代阶段尚未满足送审条件。</p>
      <h3>评审规则</h3><p v-if="!policy">尚未登记有效评审规则。</p>
      <article v-for="p in context.policies" :key="p.reference.id"><p>{{ p.fact.body.state === 'ENABLED' ? '启用' : '已撤销' }} · {{ p.fact.recordedAt }}</p>
        <ul><li v-for="r in p.fact.body.requirements" :key="r.code">{{ r.code }}：{{ r.description }}；角色 {{ roleName(r.roleId) }}，至少 {{ r.minimumApprovals }} 人。</li></ul>
        <p>不通过时：{{ p.fact.body.rejectDisposition === 'RETURN' ? '退回设计' : '拒绝本轮' }}。编制、提交与评审人员分离。</p>
      </article>
      <details v-if="can('review-policy')"><summary>登记评审规则</summary><form @submit.prevent="savePolicy"><fieldset :disabled="locked">
        <button type="button" @click="loadRoles">读取可用角色</button>
        <article v-for="(r, i) in requirements" :key="i">
          <label>要求编号<input v-model="r.code" :aria-label="`要求编号 ${i + 1}`" required maxlength="100" /></label>
          <label>审查内容<textarea v-model="r.description" :aria-label="`审查内容 ${i + 1}`" required maxlength="8000" /></label>
          <label>专业角色<select v-model="r.roleId" :aria-label="`专业角色 ${i + 1}`" required><option :value="null">请选择</option><option v-for="role in roles" :key="role.roleId" :value="role.roleId">{{ role.roleName }}</option></select></label>
          <label>最少人数<input v-model.number="r.minimumApprovals" :aria-label="`最少人数 ${i + 1}`" type="number" min="1" max="1000" required /></label>
          <button type="button" :disabled="requirements.length === 1" @click="requirements.splice(i, 1)">移除此要求</button>
        </article>
        <button type="button" :disabled="requirements.length >= 100" @click="addRequirement">增加专业要求</button>
        <label>不通过时<select v-model="rejectDisposition" aria-label="不通过处理"><option value="RETURN">退回设计</option><option value="REJECT">拒绝本轮</option></select></label>
        <label>生效时间<input v-model="from" aria-label="评审规则生效时间" type="datetime-local" required /></label><label>截止时间<input v-model="to" aria-label="评审规则截止时间" type="datetime-local" required /></label>
        <label>规则状态<select v-model="state" aria-label="评审规则状态"><option value="ENABLED">启用</option><option value="REVOKED">撤销</option></select></label>
        <label>登记原因<textarea v-model="policyReason" aria-label="评审规则登记原因" required maxlength="8000" /></label><button>保存评审规则</button>
      </fieldset></form></details>
      <form v-if="can('review-submit') && !context.submissions.length" @submit.prevent="submit"><fieldset :disabled="locked || !sameVersion || !ready || !policy">
        <h3>送审完整候选集</h3><button type="button" @click="loadReviewers">读取项目评审成员</button>
        <label>独立评审人（可多选）<select v-model="selectedReviewers" multiple aria-label="独立评审人" required><option v-for="person in reviewers" :key="person.id" :value="person.id">{{ person.name }}</option></select></label>
        <label>评审依据<textarea v-model="basis" aria-label="迭代评审依据" required maxlength="8000" /></label><label>送审原因<textarea v-model="reason" aria-label="迭代送审原因" required /></label><button>提交迭代评审</button>
      </fieldset></form>
      <h3>送审记录</h3><p v-if="!context.submissions.length">尚未送审。</p>
      <article v-for="s in context.submissions" :key="s.submissionId"><p>已进入评审 · 投递：{{ delivery(s.dispatchState) }}</p><p>业务批准尚未应用。</p><button :disabled="locked" @click="inspect(s.submissionId)">查看冻结评审包</button></article>
      <article v-if="detail"><h3>冻结评审包</h3><p>{{ detail.package.reviewBasis }}</p><p>完整候选：{{ detail.package.candidates.length }} 项</p><details><summary>查看送审原件</summary><pre>{{ detail.originalSnapshot }}</pre><pre>{{ detail.originalSubmission }}</pre></details></article>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringIterationScope, EngineeringIterationSnapshot } from '@/api/plm/engineeringIteration'
import { iterationReviewApi as api, type ReviewContext, type ReviewDetail, type ReviewRequirement } from '@/api/plm/iterationReview'
import { requirementSetResponse as response } from '@/auth/requirementSetRecovery'
import { pendingIterationReview, writeIterationReview, resolveIterationReview, type PendingIterationReview } from '@/auth/iterationReviewRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ iteration: EngineeringIterationSnapshot; scope: EngineeringIterationScope; disabled?: boolean }>()
const emit = defineEmits<{ changed: [] }>(), permission = usePermissionStore()
const can = (action: string) => permission.loaded && permission.has('plm-engineering-iteration:query') && permission.has(`plm-engineering-iteration:${action}`)
const context = ref<ReviewContext | null>(null), detail = ref<ReviewDetail | null>(null), busy = ref(false), error = ref(''), notice = ref(''), pending = ref<PendingIterationReview | null>(null)
const roles = ref<{ roleId: number; roleName: string }[]>([]), reviewers = ref<{ id: string; name: string }[]>([]), selectedReviewers = ref<string[]>([])
const requirements = ref<(Omit<ReviewRequirement, 'roleId'> & { roleId: number | null })[]>([]), state = ref<'ENABLED' | 'REVOKED'>('ENABLED'), rejectDisposition = ref<'RETURN' | 'REJECT'>('RETURN')
const from = ref(''), to = ref(''), policyReason = ref(''), basis = ref(''), reason = ref('')
const locked = computed(() => busy.value || props.disabled || !!pending.value), sameVersion = computed(() => context.value?.iteration.reference.digest === props.iteration.reference.digest)
const policy = computed(() => { const p = context.value?.policies.at(-1); return p?.fact.body.state === 'ENABLED' ? p : null })
const ready = computed(() => context.value?.iteration.body.iteration?.stage === 'IN_DESIGN' && context.value.iteration.body.iteration.holdState === 'NORMAL'
  && context.value.candidates.length > 0 && context.value.candidates.every(c => c.isCurrent && c.sourceDrift === 'UNCHANGED' && !c.findings.length && c.original.definition.technicalSource.state === 'PASS'))
let generation = 0, live = true
const copy = <T,>(v: T): T => JSON.parse(JSON.stringify(v))
function roleName(id: number) { return roles.value.find(r => r.roleId === id)?.roleName ?? String(id) }
function delivery(code: string) { return ({ PENDING: '待投递', RECEIVED: '已接收', DUPLICATE_OF: '已确认原受理', OUTCOME_UNKNOWN: '结果待确认', BLOCKED: '待处理', CONFLICT: '原件冲突' } as Record<string, string>)[code] ?? code }
function addRequirement() { requirements.value.push({ code: '', description: '', roleId: null, minimumApprovals: 1 }) }
function syncPending() { pending.value = pendingIterationReview(props.scope) }
function reset() { generation++; busy.value = false; context.value = detail.value = null; roles.value = []; reviewers.value = []; selectedReviewers.value = []; pending.value = null; requirements.value = []; addRequirement(); error.value = notice.value = from.value = to.value = policyReason.value = basis.value = reason.value = ''; state.value = 'ENABLED'; rejectDisposition.value = 'RETURN' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || props.disabled || !can('query') || !authenticationCanRead()) return
  const current = ++generation, stamp = identityStamp(), id = props.iteration.reference.id, scope = JSON.stringify(props.scope)
  const check = () => { requireIdentity(stamp); if (!live || generation !== current || !can('query') || !authenticationCanRead() || props.iteration.reference.id !== id || JSON.stringify(props.scope) !== scope) throw new Error('当前迭代或身份已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); syncPending(); await action(check); check(); syncPending() }
  catch (e) { if (live && generation === current) { context.value = detail.value = null; roles.value = []; reviewers.value = []; error.value = e instanceof Error ? e.message : '迭代送审失败。'; try { syncPending() } catch { error.value = '评审原操作记录异常，请保留记录。' } } }
  finally { if (generation === current) busy.value = false }
}
async function load(check: () => void) { detail.value = null; const value = response(await api.context(props.iteration.reference.id)); check(); if (value.iterationId !== props.iteration.reference.id || value.iteration.tenantId !== props.scope.tenantId) throw new Error('评审来源与当前迭代不一致。'); context.value = value }
async function reload() { await run(load) }
async function loadRoles() { if (can('review-policy')) await run(async check => { const v = response(await api.roles()); check(); if (!can('review-policy')) throw new Error('规则权限已变化。'); roles.value = v }) }
async function loadReviewers() { await run(async check => { const v = response(await api.reviewers(props.iteration.reference.id)); check(); reviewers.value = v.filter(x => x.id !== props.scope.actorId); selectedReviewers.value = [] }) }
async function savePolicy() {
  const c = context.value; if (!c || locked.value || !can('review-policy')) return
  await run(async check => {
    if (requirements.value.some(r => r.roleId === null)) throw new Error('请选择每项要求的专业角色。')
    const input = { state: state.value, requirements: copy(requirements.value.map(r => ({ ...r, roleId: r.roleId! }))), rejectDisposition: rejectDisposition.value, separateAuthorAndReviewer: true as const, effectiveFrom: new Date(from.value).toISOString(), effectiveTo: new Date(to.value).toISOString(), reason: policyReason.value }
    await writeIterationReview(props.scope, c.iterationId, 'POLICY', () => { check(); if (!can('review-policy')) throw new Error('规则权限已变化。') }, operationId => api.policy(c.iterationId, c.etag, { ...input, operationId }))
    check(); await load(check); notice.value = '评审规则已保存。'
  })
}
async function submit() {
  const c = context.value, p = policy.value; if (!c || !p || locked.value || !sameVersion.value || !ready.value || !can('review-submit')) return
  const input = { iteration: copy(c.iteration.reference), iterationEtag: c.iteration.etag, inputVersion: c.iteration.body.iteration!.inputVersion, candidates: copy(c.candidates.map(v => v.reference)), policy: copy(p.reference), reviewers: [...selectedReviewers.value], reviewBasis: basis.value, reason: reason.value }
  await run(async check => {
    if (!input.reviewers.length || input.reviewers.some(id => !reviewers.value.some(v => v.id === id))) throw new Error('请选取已核对的独立评审人。')
    if ([...input.reason].length > 500) throw new Error('送审原因最多 500 个字符。')
    await writeIterationReview(props.scope, c.iterationId, 'SUBMIT', () => { check(); if (!can('review-submit')) throw new Error('送审权限已变化。') }, operationId => api.submit(c.iterationId, c.etag, { ...input, operationId }))
    check(); await load(check); notice.value = '完整候选集已送审，等待审批受理。'; emit('changed')
  })
}
async function recover(close: boolean) { await run(async check => { const value = await resolveIterationReview(props.scope, close, check); check(); await load(check); notice.value = value.state === 'COMMITTED' ? '原评审操作已提交。' : value.state === 'CLOSED' ? '未提交评审操作已结束。' : '原结果仍未知，请继续查询。'; if (value.state === 'COMMITTED') emit('changed') }) }
async function inspect(id: string) { await run(async check => { detail.value = null; const value = response(await api.detail(props.iteration.reference.id, id)); check(); if (value.submission.submissionId !== id || value.package.iteration.id !== props.iteration.reference.id) throw new Error('评审包与当前迭代不一致。'); detail.value = value }) }
function changed() { reset(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_iteration_review_operation_v1:')) { try { syncPending() } catch { context.value = detail.value = null; error.value = '评审原操作记录异常，请保留记录。' } } }
watch(() => [props.iteration.reference.id, props.iteration.reference.digest, JSON.stringify(props.scope)], changed)
watch(() => can('query'), allowed => { if (!allowed) reset() })
watch(() => props.disabled, disabled => { if (!disabled && !context.value && !error.value && !busy.value) void reload() })
onMounted(() => { addRequirement(); window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; reset(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.iteration-review{padding:18px;margin-top:18px;border:1px solid var(--el-border-color);border-radius:8px}header{display:flex;gap:12px;justify-content:space-between;flex-wrap:wrap}article{padding:12px;margin:12px 0;border:1px solid var(--el-border-color);border-radius:6px}label{display:grid;gap:6px;margin:10px 0}fieldset{border:0;padding:0}input,select,textarea,button{font:inherit;padding:8px;border:1px solid var(--el-border-color);border-radius:4px;color:inherit;background:var(--el-bg-color);max-width:100%;box-sizing:border-box}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}pre{white-space:pre-wrap;overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}
</style>
