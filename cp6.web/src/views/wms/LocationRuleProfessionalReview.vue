<template>
  <section class="professional-review" :aria-busy="busy">
    <header><h3>准确版本评审与批准</h3><button :disabled="busy || disabled" @click="reload">重新核对送审资料</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>原签署或审批绑定操作尚未确认。</p><button :disabled="busy || disabled" @click="recover(false)">查询原审批绑定操作</button><button :disabled="busy || disabled" @click="recover(true)">结束未提交审批绑定操作</button></div>
    <template v-if="detail">
      <p><strong>{{ detail.locationRule.body.draft.selectorKey.ruleFamily }} · 送审版本 {{ detail.locationRule.reference.version }}</strong></p>
      <p v-if="!detail.locationRule.isCurrent">这是已冻结的历史送审版本；当前工作版本的修改不属于本次签署。</p>
      <p class="digest">内容摘要：{{ detail.locationRule.reference.digest }}</p>
      <p>评审依据：{{ detail.package.reviewBasis }}</p>
      <ul><li v-for="(relation, index) in detail.package.declaredChangeRelations" :key="index">{{ relation }}</li></ul>
      <h4>本次冻结的规则候选</h4>
      <dl><dt>容量</dt><dd>{{ detail.package.body.draft.capacity.mode }}<template v-if="detail.package.body.draft.capacity.mode === 'LIMITED'"> · {{ detail.package.body.draft.capacity.value }} · 单位 {{ detail.package.body.draft.capacity.uomRef.id }}（版本 {{ detail.package.body.draft.capacity.uomRef.version }}）</template></dd>
      <dt>允许品类</dt><dd>{{ detail.package.body.draft.allowedProductTypes.join('、') || '无声明项' }}</dd><dt>允许动作</dt><dd>{{ detail.package.body.draft.allowedOperations.join('、') || '无声明项' }}</dd>
      <dt>拣货</dt><dd>{{ detail.package.body.draft.isPickable ? '候选允许' : '候选不允许' }}</dd><dt>有效区间</dt><dd>{{ detail.package.body.draft.validFrom }} 至 {{ detail.package.body.draft.validTo ?? '明确无截止' }}</dd></dl>
      <details><summary>完整候选与准确来源原件</summary><pre>{{ detail.locationRule.originalJson }}</pre></details>
      <h4>前版差异</h4><p v-if="!detail.previous">没有前版。</p><details v-else><summary>前版 {{ detail.previous.reference.version }} 完整原件</summary><pre>{{ detail.previous.originalJson }}</pre></details>
      <p class="digest">固定差异摘要：{{ detail.package.changeDigest }}</p>
      <h4>本次冻结的专业规则</h4>
      <ul><li v-for="item in detail.policy.fact.body.requirements" :key="item.code">{{ item.code }}：{{ item.description }}；专业角色 {{ item.roleId }}，至少 {{ item.minimumApprovals }} 人。</li></ul>
      <ul><li v-for="(item, index) in detail.policy.fact.body.externalRequirements" :key="index">{{ item }}</li></ul>
      <form v-if="canDecide && tasks.length" @submit.prevent="vote('approve')"><fieldset :disabled="locked">
        <h4>我的专业评审任务</h4><label>待办任务<select v-model="taskId" aria-label="库位规则专业评审任务"><option v-for="task in tasks" :key="task.task.taskId" :value="task.task.taskId">{{ task.task.taskId }}</option></select></label>
        <label>评审意见<textarea v-model="comment" aria-label="库位规则专业评审意见" maxlength="1000" required /></label>
        <p>意见和签署绑定上方准确版本。签署完成后由规则 Owner 绑定最终决定。</p>
        <button :disabled="!taskId || !comment.trim() || Array.from(comment).length > 500">同意本次库位规则评审</button>
        <button type="button" :disabled="!taskId || !comment.trim() || Array.from(comment).length > 500" @click="vote('reject')">驳回本次库位规则评审</button>
      </fieldset></form>
      <p v-else-if="canDecide">当前没有属于我的本次评审待办。</p>
      <h4>规则审批绑定结果</h4>
      <p v-if="detail.application.application">{{ detail.application.application.approved ? (detail.locationRule.state === 'WITHDRAWN' ? '候选已撤回，保留历史批准' : '审批通过，待生效检查') : '该次评审已驳回' }} · {{ detail.application.application.appliedAt }}</p>
      <p v-else>{{ detail.application.decision ? `FLOW 最终决定：${detail.application.decision}；待绑定准确候选。` : '尚未收到 FLOW 最终决定。' }}</p>
      <p v-if="detail.application.receipt?.application === 'STALE'">此审批已归入历史，未改变当前规则。</p>
      <form v-if="canApply && detail.application.decision && !detail.application.receipt" @submit.prevent="apply"><fieldset :disabled="locked">
        <label>应用原因<textarea v-model="reason" aria-label="库位规则审批绑定原因" maxlength="4000" required /></label><button>绑定库位规则最终决定</button>
      </fieldset></form>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

import { locationRuleReviewApi, type LocationRuleReviewTask, type ReviewDetail } from '@/api/wms/locationRuleReview'
import type { LocationRuleReviewScope } from '@/api/wms/locationRuleReview'
import { locationRuleResponse } from '@/auth/locationRuleReviewResponse'
import { pendingLocationRuleApproval, resolveLocationRuleApproval, writeLocationRuleApplication, writeLocationRuleDecision, type PendingLocationRuleApproval } from '@/auth/locationRuleApprovalRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'

const props = defineProps<{ locationRuleId: string; submissionId: string; scope: LocationRuleReviewScope; disabled?: boolean }>()
const emit = defineEmits<{ changed: [] }>(), permissions = usePermissionStore()
const canRead = computed(() => permissions.loaded && permissions.has('wms-location-rule:query'))
const canDecide = computed(() => canRead.value && permissions.has('wms-location-rule:review-decide') && permissions.has('oa-inbox:approve'))
const canApply = computed(() => canRead.value && permissions.has('wms-location-rule:review-bind'))
const detail = ref<ReviewDetail | null>(null), tasks = ref<LocationRuleReviewTask[]>([])
const busy = ref(false), error = ref(''), notice = ref(''), taskId = ref(''), comment = ref(''), reason = ref('')
const pending = ref<PendingLocationRuleApproval | null>(null), locked = computed(() => busy.value || props.disabled || !!pending.value)
const urls = new Set<string>(); let generation = 0, live = true
const display = (value: unknown) => typeof value === 'string' ? value : JSON.stringify(value)
const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b)
function syncPending() { pending.value = pendingLocationRuleApproval(props.scope) }
function reset() {
  generation++; busy.value = false; detail.value = null; tasks.value = []; pending.value = null
  error.value = notice.value = taskId.value = comment.value = reason.value = ''
  for (const url of urls) URL.revokeObjectURL(url); urls.clear()
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || props.disabled || !canRead.value || !authenticationCanRead()) return
  const current = ++generation, stamp = identityStamp(), identity = [props.locationRuleId, props.submissionId, props.scope.tenantId, props.scope.actorId].join('/')
  const check = () => { requireIdentity(stamp); if (!live || generation !== current || !canRead.value || !authenticationCanRead()
    || identity !== [props.locationRuleId, props.submissionId, props.scope.tenantId, props.scope.actorId].join('/')) throw new Error('当前评审或身份已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); syncPending(); await action(check); check(); syncPending() }
  catch (e) { if (live && generation === current) { detail.value = null; tasks.value = []; error.value = e instanceof Error ? e.message : '评审操作失败。'; try { syncPending() } catch { error.value = '批准原操作记录异常，请保留记录。' } } }
  finally { if (generation === current) busy.value = false }
}
async function load(check: () => void) {
  const value = locationRuleResponse(await locationRuleReviewApi.detail(props.locationRuleId, props.submissionId)); check()
  if (value.locationRule.body.tenantId !== props.scope.tenantId || value.submission.submissionId !== props.submissionId
    || value.locationRule.reference.id !== props.locationRuleId || !same(value.locationRule.reference, value.submission.locationRule)
    || !same(value.package.locationRule, value.submission.locationRule) || !same(value.policy.reference, value.submission.policy)
    || !same(value.package.previous, value.previous?.reference ?? null) || value.application.locationRuleId !== props.locationRuleId || value.application.submissionId !== props.submissionId)
    throw new Error('送审资料的准确来源不一致。')
  const available = canDecide.value ? await locationRuleReviewApi.tasks(props.locationRuleId) : []; check()
  if (!Array.isArray(available) || available.some(x => x.locationRuleId !== props.locationRuleId)) throw new Error('专业评审任务来源不一致。')
  detail.value = value; tasks.value = value.locationRule.isCurrent && value.locationRule.state === 'SUBMITTED' && value.locationRule.activeSubmissionId === props.submissionId
    ? available.filter(x => x.submissionId === props.submissionId && x.locationRuleVersion === value.locationRule.reference.version) : []
  taskId.value = tasks.value[0]?.task.taskId ?? ''
}
async function reload() { await run(load) }
async function vote(decision: 'approve' | 'reject') {
  const selected = tasks.value.find(x => x.task.taskId === taskId.value), message = comment.value
  if (locked.value || !canDecide.value || !selected || !message.trim() || Array.from(message).length > 500) return
  await run(async check => {
    const current = await locationRuleReviewApi.task(selected.task.taskId); check()
    if (current.taskId !== selected.task.taskId || current.instanceId !== selected.task.instanceId) throw new Error('评审任务已变化。')
    await writeLocationRuleDecision(props.scope, props.locationRuleId, props.submissionId, current.taskId, () => { check(); if (!canDecide.value) throw new Error('当前无专业评审权限。') },
      commandId => locationRuleReviewApi.decide(current.taskId, { commandId, decision, comment: message, taskRowVersion: current.taskRowVersion, instanceRowVersion: current.instanceRowVersion }))
    check(); await load(check); comment.value = ''; notice.value = '本次专业意见已签署。'; emit('changed')
  })
}
async function apply() {
  const value = detail.value, message = reason.value
  if (!value || locked.value || !canApply.value || !message.trim()) return
  await run(async check => {
    await writeLocationRuleApplication(props.scope, props.locationRuleId, props.submissionId, () => { check(); if (!canApply.value) throw new Error('当前无审批绑定权限。') },
      operationId => locationRuleReviewApi.apply(props.locationRuleId, value.application.etag, { operationId, submissionId: props.submissionId, expectedReceiptId: value.application.receipt?.receiptId ?? null, reason: message }))
    check(); await load(check); reason.value = ''; notice.value = '库位规则最终决定已绑定，尚未生效。'; emit('changed')
  })
}
async function recover(close: boolean) { await run(async check => { const state = await resolveLocationRuleApproval(props.scope, close, check); check(); await load(check); notice.value = state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : state === 'CLOSED' ? '未提交原操作已结束。' : '原签署或应用结果已恢复。'; emit('changed') }) }
function changed() { reset(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_locationRule_approval_operation_v1:')) { try { syncPending() } catch { reset(); error.value = '批准原操作记录异常，请保留记录。' } } }
watch(() => [props.locationRuleId, props.submissionId, props.scope.tenantId, props.scope.actorId], changed)
watch(canRead, allowed => { if (!allowed) reset() })
watch(() => props.disabled, disabled => { if (!disabled && !detail.value && !error.value && !busy.value) void reload() })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; reset(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.professional-review{padding:16px;margin-top:16px;border:1px solid var(--el-border-color);border-radius:8px}header{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}label{display:grid;gap:6px;margin:12px 0}fieldset{border:0;padding:0}button,input,select,textarea{font:inherit;padding:8px;max-width:100%;box-sizing:border-box;color:inherit;background:var(--el-bg-color);border:1px solid var(--el-border-color);border-radius:4px}button{cursor:pointer;margin:4px}button:disabled{opacity:.5;cursor:default}.digest,dd,li,pre{overflow-wrap:anywhere}pre{white-space:pre-wrap;max-height:30rem;overflow:auto}dt{font-weight:600;margin-top:8px}dd{margin-left:0}article{padding:8px 0}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}
</style>
