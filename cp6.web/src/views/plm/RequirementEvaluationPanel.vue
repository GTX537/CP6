<template>
  <section aria-label="需求专业评价" :aria-busy="busy">
    <header><h2>专业评价与批准</h2><button :disabled="busy || !canRead" @click="load">读取已保存版本的评价</button></header>
    <p>批准只确认需求定义和评价条件，不表示需求已满足；完整需求集仍需后续审批。此处读取已保存版本，未保存的编辑内容不进入评审。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>有尚未确认的专业评价操作，请先核对原结果。</p><button :disabled="busy" @click="recover(false)">查询评价原操作</button><button :disabled="busy" @click="recover(true)">结束未提交评价操作</button></div>
    <template v-if="view">
      <p>{{ view.requirement.body.name }} · 需求版本 {{ view.requirement.reference.version }} · {{ view.requirement.isCurrent ? '当前编制版本' : '历史编制版本' }} · {{ state(view.control?.state || 'DRAFT') }}</p>
      <p v-if="view.evidenceMode === 'DEMO'">当前为演示证据环境。</p>
      <p>评价记录 {{ view.control?.revision || '尚未登记' }} · {{ view.controlIsCurrent ? '当前记录' : '历史记录，仅供查阅' }}</p>
      <p v-if="view.currentApproval">{{ view.controlIsCurrent ? '当前批准' : '当时批准' }}指向需求版本 {{ view.currentApproval.body.requirement.version }}，批准时间 {{ view.currentApproval.body.approvedAt }}。</p><p v-else>{{ view.controlIsCurrent ? '当前' : '当时' }}无专业批准。</p>
      <ul aria-label="评价资格缺项"><li v-for="m in view.missing" :key="m">{{ missing(m) }}</li></ul>
      <details v-if="view.policy"><summary>已登记专业规则</summary><p>{{ view.policy.body.input.basis }} · {{ view.policy.body.input.state === 'ENABLED' ? '启用' : '停用' }}</p><p>{{ view.policy.body.input.effectiveFrom }} 至 {{ view.policy.body.input.effectiveTo }}</p><ul><li v-for="r in view.policy.body.input.roles" :key="r.code">{{ r.code }} · 角色 {{ r.roleId }} · 至少 {{ r.minimumApprovals }} 人</li></ul></details>
      <details v-if="view.packet"><summary>固定评审包 · 需求版本 {{ view.packet.body.requirement.version }}</summary>
        <p>{{ view.packet.body.inputs.requirement.body.requirement?.interpretation }}</p>
        <article v-for="r in view.packet.body.inputs.relatedOriginals" :key="r.reference.digest"><h3>{{ r.body.name }} · 版本 {{ r.reference.version }}</h3><blockquote v-for="c in r.body.source?.clauses || []" :key="c.id">{{ c.id }} · {{ c.originalText }}</blockquote></article>
        <article v-for="c in view.packet.body.inputs.requirement.body.requirement?.criteria || []" :key="c.id"><h3>{{ c.id }} · {{ c.object }}</h3><p>{{ c.conditions }} · 方法需求：{{ c.methodRequirement }}</p><p v-if="c.kind === 'QUANTITATIVE'">下界 {{ bound(c.lower, c.lowerInclusive, c.evaluation?.lowerUnbounded) }}；上界 {{ bound(c.upper, c.upperInclusive, c.evaluation?.upperUnbounded) }}；单位 {{ c.proposedUnit }}；精度 {{ c.precision }}。</p><p v-else>{{ c.qualitativeBasis }}</p></article>
      </details>
      <ul aria-label="专业签署"><li v-for="v in view.votes" :key="v.reference.id">{{ v.body.actorId }} · {{ v.body.approved ? '同意定义' : '退回' }} · {{ v.body.comment }} · {{ v.body.signedAt }}</li></ul>
      <form v-if="can('review-policy')" @submit.prevent="savePolicy"><fieldset :disabled="locked || !view.requirement.isCurrent">
        <legend>登记专业规则</legend><button type="button" @click="roles">读取可用角色</button>
        <label>规则状态<select v-model="policyState" aria-label="评价规则状态"><option value="ENABLED">启用</option><option value="DISABLED">停用</option></select></label>
        <label>规则依据<textarea v-model="basis" aria-label="评价规则依据" required maxlength="8000" /></label>
        <div v-for="(r, i) in policyRoles" :key="i"><label>专业职责<input v-model="r.code" :aria-label="`专业职责 ${i + 1}`" required maxlength="256" /></label><label>角色<select v-model="r.roleId" :aria-label="`专业角色 ${i + 1}`" required><option :value="0">请选择实际角色</option><option v-for="choice in roleChoices" :key="choice.roleId" :value="choice.roleId">{{ choice.roleName }}</option></select></label><label>独立签署人数<input v-model.number="r.minimumApprovals" type="number" min="1" max="100" :aria-label="`专业人数 ${i + 1}`" required /></label><button type="button" @click="policyRoles.splice(i, 1)">移除职责</button></div>
        <button type="button" @click="policyRoles.push({ code: '', roleId: 0, minimumApprovals: 1 })">添加专业职责</button>
        <label>生效时间（本地）<input v-model="from" type="datetime-local" aria-label="评价生效时间" required /></label><label>失效时间（本地）<input v-model="to" type="datetime-local" aria-label="评价失效时间" required /></label>
        <button :disabled="!policyRoles.length || policyRoles.some(r => !r.roleId) || !reason">保存专业规则</button><p>修改送审中的规则会将该评审退回，旧签署仍保留。</p>
      </fieldset></form>
      <label>本次操作原因或专业意见<textarea v-model="reason" aria-label="评价操作原因" maxlength="8000" :disabled="locked" /></label>
      <div><button v-if="can('review-submit')" :disabled="locked || !reason || !view.requirement.isCurrent || view.control?.state === 'IN_REVIEW' || !!view.missing.length" @click="command('SUBMIT')">送交专业评价</button><button v-if="can('review-decide')" :disabled="locked || !reason || !independent || view.control?.state !== 'IN_REVIEW'" @click="command('APPROVE')">签署同意定义</button><button v-if="can('review-decide')" :disabled="locked || !reason || !independent || view.control?.state !== 'IN_REVIEW'" @click="command('RETURN')">退回需求定义</button><button v-if="can('withdraw')" :disabled="locked || !reason || !view.currentApproval" @click="command('WITHDRAW')">撤回当前批准</button><button :disabled="busy" @click="observe">核对当前批准来源</button></div>
      <p v-if="proof" role="status">{{ proof.availability === 'AVAILABLE' ? '该准确版本当前可作为专业批准来源。' : '该版本当前不可作为专业批准来源。' }} 此为读取时状态，后续采用仍须重新核对。</p><ul v-if="proof"><li v-for="m in proof.missing" :key="m">{{ missing(m) }}</li></ul>
      <form @submit.prevent="history"><label>评价记录版本<input v-model="version" aria-label="评价历史版本" pattern="[1-9][0-9]*" required /></label><button :disabled="busy">读取评价历史</button></form>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { evaluationApi, type EvaluationAction, type EvaluationPolicy, type EvaluationProof, type EvaluationScope, type EvaluationView } from '@/api/plm/requirementEvaluation'
import { evaluationResponse, pendingEvaluation, resolveEvaluation, saveEvaluation, type PendingEvaluation } from '@/auth/requirementEvaluationRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ requirementId: string }>(), permission = usePermissionStore()
const canRead = computed(() => permission.loaded && permission.has('plm-engineering-requirement:query'))
const can = (action: string) => canRead.value && permission.has(`plm-engineering-requirement:${action}`)
const busy = ref(false), error = ref(''), notice = ref(''), scope = ref<EvaluationScope | null>(null), view = ref<EvaluationView | null>(null), proof = ref<EvaluationProof | null>(null), pending = ref<PendingEvaluation | null>(null)
const locked = computed(() => busy.value || !!pending.value || view.value?.controlIsCurrent === false), independent = computed(() => !!view.value?.packet && view.value.packet.body.authorId !== scope.value?.actorId && view.value.packet.body.submitterId !== scope.value?.actorId && !view.value.votes.some(v => v.body.actorId === scope.value?.actorId))
const reason = ref(''), basis = ref(''), from = ref(''), to = ref(''), version = ref('1'), policyState = ref<'ENABLED' | 'DISABLED'>('ENABLED'), roleChoices = ref<{ roleId: number; roleName: string }[]>([]), policyRoles = ref<EvaluationPolicy['roles']>([])
let generation = 0, live = true
function clear() { generation++; busy.value = false; scope.value = view.value = proof.value = pending.value = null; roleChoices.value = []; policyRoles.value = []; reason.value = basis.value = from.value = to.value = error.value = notice.value = ''; version.value = '1' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value) return
  const epoch = generation, stamp = identityStamp(), id = props.requirementId
  const check = () => { requireIdentity(stamp); if (!live || generation !== epoch || id !== props.requirementId || !authenticationCanRead() || !canRead.value) throw new Error('身份已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); await action(check); check() } catch (e: any) { try { check(); if (e?.response?.status === 403) { clear(); error.value = '当前无权执行或查看这项专业评价及来源。' } else error.value = failure(e) } catch { /* Discard obsolete private response. */ } }
  finally { if (live && epoch === generation) { try { if (scope.value) pending.value = pendingEvaluation(scope.value) } catch { error.value = '原操作记录异常，请保留记录。' } busy.value = false } }
}
function failure(e: any) { const c = e?.response?.data?.message || e?.message || ''; const labels: Record<string, string> = { REQUIREMENT_REVIEW_CHANGED: '需求或评价状态已变化，请重新读取并核对原操作。', REQUIREMENT_REVIEW_SOURCE_INCOMPLETE: '评价条件尚不完整，请核对缺项。', REQUIREMENT_REVIEW_SOURCE_CHANGED: '送审来源已变化，请退回核对。', REQUIREMENT_REVIEW_POLICY_UNAVAILABLE: '专业规则当前不可用，请核对启用状态和有效期。', OPERATIONS_OUTCOME_UNKNOWN: '提交结果未确认，请查询原操作。' }; return labels[c] || (/^[A-Z][A-Z0-9_]+$/.test(c) ? '无法完成，请核对权限、规则和原操作。' : c) || '暂时无法完成。' }
const state = (v: string) => ({ DRAFT: '尚未送审', IN_REVIEW: '专业评审中', RETURNED: '已退回', APPROVED: '存在专业批准', WITHDRAWN: '已撤回批准' }[v] || '待核对')
function missing(v: string) { const [c, id] = v.split(':'); const labels: Record<string, string> = { EVALUATION_UNIT_AND_BOUNDARY_REQUIRED: '尚未选择准确单位和边界意图', UPPER_BOUND_INTENT_REQUIRED: '上界未定', LOWER_BOUND_INTENT_REQUIRED: '下界未定', UNIT_SOURCE_CHANGED: '单位已改版或停用', UNIT_LABEL_MISMATCH: '拟用单位与准确单位不符', SOURCE_CLARIFICATION_PENDING: '来源问题尚未澄清', SOURCE_UNRESOLVED: '来源尚未确定', CRITERIA_REQUIRED: '尚未编制评价判据', CONDITIONS_REQUIRED: '适用条件未定', METHOD_REQUIREMENT_REQUIRED: '方法需求未定', PRECISION_REQUIRED: '精度要求未定', ENDPOINT_REQUIRED: '端点包含方式未定', BOUND_REQUIRED: '比较边界未定', REQUIREMENT_APPROVAL_REQUIRED: '该准确版本尚无当前批准', REQUIREMENT_APPROVAL_WITHDRAWN: '批准已撤回', SOURCE_VERSION_CHANGED: '来源已改版', RELATION_VERSION_CHANGED: '关系目标已改版', APPLICABLE_OBJECT_REQUIRED: '适用对象未定', NECESSITY_REQUIRED: '必要性未定', ORIGIN_CONFIRMATION_REQUIRED: '来源须由原负责人确认', UNIT_BASIS_REQUIRED: '单位依据未定', QUALITATIVE_BASIS_REQUIRED: '定性判定依据未定' }; return (labels[c!] || '存在待核对的来源或评价条件') + (id ? `（${id}）` : '') }
const bound = (v: string | null, inclusive: boolean | null, unbounded?: boolean) => v !== null ? `${v}（${inclusive === true ? '含端点' : inclusive === false ? '不含端点' : '端点未定'}）` : unbounded ? '明确无界' : '未定'
async function read(check: () => void, selected?: string) { view.value = null; proof.value = null; const value = evaluationResponse(await evaluationApi.read(props.requirementId, selected)); check(); view.value = value }
async function load() { await run(async check => { view.value = null; proof.value = null; const s = evaluationResponse(await evaluationApi.scope()); check(); scope.value = s; await read(check) }) }
async function history() { await run(async check => { view.value = null; proof.value = null; const value = evaluationResponse(await evaluationApi.history(props.requirementId, version.value)); check(); view.value = value }) }
async function roles() { await run(async check => { roleChoices.value = []; const value = evaluationResponse(await evaluationApi.roles()); check(); roleChoices.value = value }) }
async function command(action: EvaluationAction, policy?: EvaluationPolicy) { await run(async check => {
  if (!scope.value || !view.value || !view.value.controlIsCurrent || pending.value || !reason.value) return
  const v = view.value, text = reason.value
  const result = await saveEvaluation(scope.value, props.requirementId, action, check, operationId => evaluationApi.save({ operationId, requirementId: props.requirementId, action, expectedEtag: v.etag, reason: text, ...(action === 'SUBMIT' ? { requirement: v.requirement.reference } : {}), ...(policy ? { policy } : {}) })); check()
  if (result.result) { await read(check); reason.value = ''; notice.value = '已保存专业评价原结果。' }
}) }
async function savePolicy() { if (!from.value || !to.value) return; await command('POLICY', { state: policyState.value, basis: basis.value, roles: JSON.parse(JSON.stringify(policyRoles.value)), effectiveFrom: new Date(from.value).toISOString(), effectiveTo: new Date(to.value).toISOString() }) }
async function recover(close: boolean) { await run(async check => { if (!scope.value) return; const result = await resolveEvaluation(scope.value, close, check); check(); if (result.result?.objectId === props.requirementId) await read(check); notice.value = result.state === 'UNKNOWN' ? '仍未确认，请保留原操作。' : result.state === 'CLOSED' ? '未提交操作已结束。' : '已取得原操作结果。' }) }
async function observe() { await run(async check => { if (!view.value) return; proof.value = null; const value = evaluationResponse(await evaluationApi.source(view.value.requirement.reference)); check(); proof.value = value }) }
watch(() => props.requirementId, clear)
watch(canRead, value => { if (!value) clear() })
onMounted(() => window.addEventListener(AUTH_EVENT, clear))
onBeforeUnmount(() => { live = false; clear(); window.removeEventListener(AUTH_EVENT, clear) })
</script>
<style scoped>
section{border:1px solid #c4cbd6;border-radius:8px;padding:18px;margin:18px 0}header{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}label{display:flex;flex-direction:column;gap:6px;margin:10px 0}input,textarea,select,button{font:inherit;padding:8px;border:1px solid #c4cbd6;border-radius:5px}button{margin:5px;cursor:pointer}button:disabled{opacity:.55;cursor:default}fieldset{border:1px solid #d8dce3;margin:12px 0}textarea{min-height:65px}p,li,blockquote{overflow-wrap:anywhere}blockquote{white-space:pre-wrap}[role=alert]{color:#b42318}[role=status]{color:#167044}
</style>
