<template>
  <section class="professional-review" :aria-busy="busy">
    <header><h3>准确版本评审与批准</h3><button :disabled="busy || disabled" @click="reload">重新核对送审资料</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>原签署或批准操作尚未确认。</p><button :disabled="busy || disabled" @click="recover(false)">查询原批准操作</button><button :disabled="busy || disabled" @click="recover(true)">结束未提交批准操作</button></div>
    <template v-if="detail">
      <p><strong>{{ detail.requirementSet.body.name }} · 送审版本 {{ detail.requirementSet.reference.version }}</strong></p>
      <p v-if="!detail.requirementSet.isCurrent">这是已冻结的历史送审版本；当前工作版本的修改不属于本次签署。</p>
      <p class="digest">内容摘要：{{ detail.requirementSet.reference.digest }}</p>
      <p>评审依据：{{ detail.package.reviewBasis }}</p>
      <ul><li v-for="(relation, index) in detail.package.declaredChangeRelations" :key="index">{{ relation }}</li></ul>
      <h4>固定成员与适用范围</h4><p>{{ detail.package.body.scope }}</p><p>完整性依据：{{ detail.package.body.completenessBasis }}</p>
      <article v-for="member in detail.requirementSet.memberStatus || []" :key="member.requirement.reference.id">
        <p>{{ member.requirement.body.name }} · 需求版本 {{ member.requirement.reference.version }}</p>
        <p>适用对象：{{ member.requirement.body.requirement?.applicableObject }}</p><p>必要性原文：{{ member.requirement.body.requirement?.necessity }}</p>
        <h5>来源原条款</h5><p>{{ member.source?.body.source?.sourceLabel }} · {{ member.source?.body.source?.acquisitionBasis }}</p><blockquote v-for="clause in member.source?.body.source?.clauses.filter(x => member.requirement.body.requirement?.clauseIds.includes(x.id)) || []" :key="clause.id">{{ clause.id }} · {{ clause.locator }}：{{ clause.originalText }}</blockquote>
        <h5>需求解释</h5><blockquote>{{ member.requirement.body.requirement?.interpretation }}</blockquote>
        <h5>评价条件</h5><ul><li v-for="criterion in member.requirement.body.requirement?.criteria || []" :key="criterion.id">{{ criterion.object }}；条件：{{ criterion.conditions }}；方法要求：{{ criterion.methodRequirement }}。<template v-if="criterion.kind === 'QUANTITATIVE'">下界 {{ criterion.lower ?? (criterion.evaluation?.lowerUnbounded ? '明确无界' : '未定') }}（{{ criterion.lowerInclusive === true ? '含端点' : criterion.lowerInclusive === false ? '不含端点' : '无端点' }}），上界 {{ criterion.upper ?? (criterion.evaluation?.upperUnbounded ? '明确无界' : '未定') }}（{{ criterion.upperInclusive === true ? '含端点' : criterion.upperInclusive === false ? '不含端点' : '无端点' }}），单位 {{ criterion.proposedUnit }}；精度 {{ criterion.precision }}。</template><template v-else>{{ criterion.qualitativeBasis }}</template></li></ul><p>成员批准来源：{{ member.evaluation.availability === 'AVAILABLE' ? '当前可用' : '待核对' }}</p>
      </article>
      <ul><li v-for="missing in detail.requirementSet.missing" :key="missing">{{ missingText(missing) }}</li></ul>
      <h4>前版差异</h4>
      <p v-if="!detail.previous">首次修订，没有前版。</p>
      <p v-else>前版 {{ detail.previous.reference.version }} → 本次 {{ detail.requirementSet.reference.version }}</p><details v-if="detail.previous"><summary>前版完整范围与成员</summary><p>{{ detail.previous.body.scope }}</p><p>{{ detail.previous.body.completenessBasis }}</p><article v-for="member in detail.previous.memberStatus || []" :key="member.requirement.reference.id"><p>{{ member.requirement.body.name }} · 版本 {{ member.requirement.reference.version }}</p><p>必要性：{{ member.requirement.body.requirement?.necessity }}</p><p>{{ member.requirement.body.requirement?.interpretation }}</p></article></details>
      <ul><li v-for="change in detail.requirementSet.differences || []" :key="change.requirementId">{{ change.requirementId }}：{{ change.change }} · {{ change.previous?.requirement.version || "无" }} → {{ change.current?.requirement.version || "无" }}</li></ul>
      <h4>本次冻结的专业规则</h4>
      <ul><li v-for="item in detail.policy.fact.body.requirements" :key="item.code">{{ item.code }}：{{ item.description }}；专业角色 {{ item.roleId }}，至少 {{ item.minimumApprovals }} 人。</li></ul>
      <ul><li v-for="(item, index) in detail.policy.fact.body.externalRequirements" :key="index">{{ item }}</li></ul>
      <form v-if="canDecide && tasks.length" @submit.prevent="vote('approve')"><fieldset :disabled="locked">
        <h4>我的专业评审任务</h4><label>待办任务<select v-model="taskId" aria-label="需求集专业评审任务"><option v-for="task in tasks" :key="task.task.taskId" :value="task.task.taskId">{{ task.task.taskId }}</option></select></label>
        <label>评审意见<textarea v-model="comment" aria-label="需求集专业评审意见" maxlength="1000" required /></label>
        <p>意见和签署绑定上方准确版本。签署完成后由需求集 Owner 应用最终决定。</p>
        <button :disabled="!taskId || !comment.trim() || Array.from(comment).length > 500">同意本次需求集评审</button>
        <button type="button" :disabled="!taskId || !comment.trim() || Array.from(comment).length > 500" @click="vote('reject')">驳回本次需求集评审</button>
      </fieldset></form>
      <p v-else-if="canDecide">当前没有属于我的本次评审待办。</p>
      <h4>需求集 Owner 应用结果</h4>
      <p v-if="detail.application.application">{{ detail.application.application.approved ? '该准确版本已批准' : '该次评审已驳回' }} · {{ detail.application.application.appliedAt }}</p>
      <p v-else>{{ detail.application.decision ? `FLOW 最终决定：${detail.application.decision}；待需求集 Owner 应用。` : '尚未收到 FLOW 最终决定。' }}</p>
      <form v-if="canApply && detail.application.decision && !detail.application.application" @submit.prevent="apply"><fieldset :disabled="locked">
        <label>应用原因<textarea v-model="reason" aria-label="需求集批准应用原因" maxlength="4000" required /></label><button>应用需求集最终决定</button>
      </fieldset></form>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { requirementSetMissingText as missingText } from '@/api/plm/requirementSet'
import { requirementSetReviewApi, type RequirementSetReviewTask, type ReviewDetail } from '@/api/plm/requirementSetReview'
import type { RequirementSetScope } from '@/api/plm/requirementSet'
import { requirementSetResponse } from '@/auth/requirementSetRecovery'
import { pendingRequirementSetApproval, resolveRequirementSetApproval, writeRequirementSetApplication, writeRequirementSetDecision, type PendingRequirementSetApproval } from '@/auth/requirementSetApprovalRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'

const props = defineProps<{ requirementSetId: string; submissionId: string; scope: RequirementSetScope; disabled?: boolean }>()
const emit = defineEmits<{ changed: [] }>(), permissions = usePermissionStore()
const canRead = computed(() => permissions.loaded && permissions.has('plm-requirement-set:query'))
const canDecide = computed(() => canRead.value && permissions.has('plm-requirement-set:review-decide') && permissions.has('oa-inbox:approve'))
const canApply = computed(() => canRead.value && permissions.has('plm-requirement-set:review-apply'))
const detail = ref<ReviewDetail | null>(null), tasks = ref<RequirementSetReviewTask[]>([])
const busy = ref(false), error = ref(''), notice = ref(''), taskId = ref(''), comment = ref(''), reason = ref('')
const pending = ref<PendingRequirementSetApproval | null>(null), locked = computed(() => busy.value || props.disabled || !!pending.value)
const urls = new Set<string>(); let generation = 0, live = true
const display = (value: unknown) => typeof value === 'string' ? value : JSON.stringify(value)
const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b)
function syncPending() { pending.value = pendingRequirementSetApproval(props.scope) }
function reset() {
  generation++; busy.value = false; detail.value = null; tasks.value = []; pending.value = null
  error.value = notice.value = taskId.value = comment.value = reason.value = ''
  for (const url of urls) URL.revokeObjectURL(url); urls.clear()
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || props.disabled || !canRead.value || !authenticationCanRead()) return
  const current = ++generation, stamp = identityStamp(), identity = [props.requirementSetId, props.submissionId, props.scope.tenantId, props.scope.actorId].join('/')
  const check = () => { requireIdentity(stamp); if (!live || generation !== current || !canRead.value || !authenticationCanRead()
    || identity !== [props.requirementSetId, props.submissionId, props.scope.tenantId, props.scope.actorId].join('/')) throw new Error('当前评审或身份已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); syncPending(); await action(check); check(); syncPending() }
  catch (e) { if (live && generation === current) { detail.value = null; tasks.value = []; error.value = e instanceof Error ? e.message : '评审操作失败。'; try { syncPending() } catch { error.value = '批准原操作记录异常，请保留记录。' } } }
  finally { if (generation === current) busy.value = false }
}
async function load(check: () => void) {
  const value = requirementSetResponse(await requirementSetReviewApi.detail(props.requirementSetId, props.submissionId)); check()
  if (value.requirementSet.tenantId !== props.scope.tenantId || value.submission.submissionId !== props.submissionId
    || value.requirementSet.reference.id !== props.requirementSetId || !same(value.requirementSet.reference, value.submission.requirementSet)
    || !same(value.package.requirementSet, value.submission.requirementSet) || !same(value.policy.reference, value.submission.policy)
    || !same(value.package.previous, value.previous?.reference ?? null) || value.application.requirementSetId !== props.requirementSetId || value.application.submissionId !== props.submissionId)
    throw new Error('送审资料的准确来源不一致。')
  const available = canDecide.value ? await requirementSetReviewApi.tasks(props.requirementSetId) : []; check()
  if (!Array.isArray(available) || available.some(x => x.requirementSetId !== props.requirementSetId)) throw new Error('专业评审任务来源不一致。')
  detail.value = value; tasks.value = available.filter(x => x.submissionId === props.submissionId && x.requirementSetVersion === value.requirementSet.reference.version)
  taskId.value = tasks.value[0]?.task.taskId ?? ''
}
async function reload() { await run(load) }
async function vote(decision: 'approve' | 'reject') {
  const selected = tasks.value.find(x => x.task.taskId === taskId.value), message = comment.value
  if (locked.value || !canDecide.value || !selected || !message.trim() || Array.from(message).length > 500) return
  await run(async check => {
    const current = await requirementSetReviewApi.task(selected.task.taskId); check()
    if (current.taskId !== selected.task.taskId || current.instanceId !== selected.task.instanceId) throw new Error('评审任务已变化。')
    await writeRequirementSetDecision(props.scope, props.requirementSetId, props.submissionId, current.taskId, () => { check(); if (!canDecide.value) throw new Error('当前无专业评审权限。') },
      commandId => requirementSetReviewApi.decide(current.taskId, { commandId, decision, comment: message, taskRowVersion: current.taskRowVersion, instanceRowVersion: current.instanceRowVersion }))
    check(); await load(check); comment.value = ''; notice.value = '本次专业意见已签署。'; emit('changed')
  })
}
async function apply() {
  const value = detail.value, message = reason.value
  if (!value || locked.value || !canApply.value || !message.trim()) return
  await run(async check => {
    await writeRequirementSetApplication(props.scope, props.requirementSetId, props.submissionId, () => { check(); if (!canApply.value) throw new Error('当前无批准应用权限。') },
      operationId => requirementSetReviewApi.apply(props.requirementSetId, value.application.etag, { operationId, submissionId: props.submissionId, expectedReceiptId: value.application.receipt?.receiptId ?? null, reason: message }))
    check(); await load(check); reason.value = ''; notice.value = '需求集最终决定已应用。'; emit('changed')
  })
}
async function recover(close: boolean) { await run(async check => { const state = await resolveRequirementSetApproval(props.scope, close, check); check(); await load(check); notice.value = state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : state === 'CLOSED' ? '未提交原操作已结束。' : '原签署或应用结果已恢复。'; emit('changed') }) }
function changed() { reset(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_requirementSet_approval_operation_v1:')) { try { syncPending() } catch { reset(); error.value = '批准原操作记录异常，请保留记录。' } } }
watch(() => [props.requirementSetId, props.submissionId, props.scope.tenantId, props.scope.actorId], changed)
watch(canRead, allowed => { if (!allowed) reset() })
watch(() => props.disabled, disabled => { if (!disabled && !detail.value && !error.value && !busy.value) void reload() })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; reset(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.professional-review{padding:16px;margin-top:16px;border:1px solid var(--el-border-color);border-radius:8px}header{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}label{display:grid;gap:6px;margin:12px 0}fieldset{border:0;padding:0}button,input,select,textarea{font:inherit;padding:8px;max-width:100%;box-sizing:border-box;color:inherit;background:var(--el-bg-color);border:1px solid var(--el-border-color);border-radius:4px}button{cursor:pointer;margin:4px}button:disabled{opacity:.5;cursor:default}.digest,dd,li{overflow-wrap:anywhere}dt{font-weight:600;margin-top:8px}dd{margin-left:0}article{padding:8px 0}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}
</style>
