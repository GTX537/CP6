<template>
  <section class="requirement-set-review" :aria-busy="busy">
    <header><h2>需求集专业评审</h2><button :disabled="busy || disabled" @click="reload">刷新评审状态</button></header>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>评审原操作待确认，请先恢复原结果。</p><button :disabled="busy || disabled" @click="recover(false)">查询原评审操作</button><button :disabled="busy || disabled" @click="recover(true)">结束未提交评审操作</button></div>
    <template v-if="context">
      <p>工作版本 {{ context.requirementSet.reference.version }} · {{ context.requirementSet.body.name }}。送审固定所见内容、前版差异和评审依据。</p>
      <p v-if="!sameVersion" role="alert">需求集已有新版本，请重新读取需求集后送审。</p>
      <h3>评审规则</h3>
      <p v-if="!context.policies.length">尚未登记评审规则。</p>
      <article v-for="policy in context.policies" :key="policy.reference.id">
        <p>{{ policy.fact.body.state === 'ENABLED' ? '启用' : '已撤销' }} · {{ policy.fact.recordedAt }}</p>
        <ul><li v-for="requirement in policy.fact.body.requirements" :key="requirement.code">{{ requirement.code }}：{{ requirement.description }}；角色 {{ roleName(requirement.roleId) }}，至少 {{ requirement.minimumApprovals }} 人。</li></ul>
        <p>{{ policy.fact.body.separateAuthorAndReviewer ? '编制、提交与评审人员分离' : '按本规则允许编制人参与评审' }}</p>
        <ul v-if="policy.fact.body.externalRequirements.length"><li v-for="(requirement, index) in policy.fact.body.externalRequirements" :key="index">{{ requirement }}</li></ul>
      </article>
      <details v-if="canPolicy"><summary>登记新的评审规则</summary>
        <form @submit.prevent="savePolicy"><fieldset :disabled="locked">
          <p>按实际专业职责填写角色、审查要求和有效期；新规则保留历史版本。</p>
          <button type="button" @click="loadRoles">读取可用角色</button>
          <article v-for="(item, index) in requirements" :key="index" class="requirement">
            <label>要求编号<input v-model="item.code" :aria-label="`要求编号 ${index + 1}`" required maxlength="4000" /></label>
            <label>审查内容<textarea v-model="item.description" :aria-label="`审查内容 ${index + 1}`" required maxlength="4000" /></label>
            <label>专业角色<select v-model="item.roleId" :aria-label="`专业角色 ${index + 1}`" required><option :value="null">请选择</option><option v-for="role in roles" :key="role.roleId" :value="role.roleId">{{ role.roleName }}</option></select></label>
            <label>最少人数<input v-model.number="item.minimumApprovals" type="number" :aria-label="`最少人数 ${index + 1}`" min="1" max="1000" required /></label>
            <button type="button" :disabled="requirements.length === 1" @click="requirements.splice(index, 1)">移除此要求</button>
          </article>
          <button type="button" :disabled="requirements.length >= 100" @click="addRequirement">增加专业要求</button>
          <p>签署必须独立于编制和提交人员。未接入的外部裁决不能作为已满足要求。</p>

          <div class="grid"><label>生效时间<input v-model="from" aria-label="评审规则生效时间" type="datetime-local" required /></label><label>截止时间<input v-model="to" aria-label="评审规则截止时间" type="datetime-local" required /></label></div>
          <label>规则状态<select v-model="policyState" aria-label="评审规则状态"><option value="ENABLED">启用</option><option value="REVOKED">撤销</option></select></label>
          <label>登记原因<textarea v-model="policyReason" aria-label="评审规则登记原因" required maxlength="4000" /></label><button>保存评审规则</button>
        </fieldset></form>
      </details>
      <form v-if="canSubmit" @submit.prevent="submit"><fieldset :disabled="locked || !sameVersion || !activePolicy">
        <h3>提交准确版本</h3><p>批准将绑定本次送审版本。后续修订需要重新评审。</p>
        <label>评审依据<textarea v-model="reviewBasis" aria-label="需求集评审依据" maxlength="4000" required /></label>
        <label>相关变更说明（每行一项）<textarea v-model="changeRelations" aria-label="需求集相关变更说明" /></label>
        <label>替代的被拒绝评审<select v-model="replaces" aria-label="替代被拒绝评审"><option :value="null">首次送审</option><option v-for="item in rejected" :key="item.submissionId" :value="item.submissionId">工作版本 {{ item.requirementSet.version }} · {{ item.frozenAt }}</option></select></label>
        <label>送审原因<textarea v-model="submitReason" aria-label="需求集送审原因" maxlength="4000" required /></label><button>提交需求集评审</button>
      </fieldset></form>
      <h3>送审历史</h3><p v-if="!context.submissions.length">尚未送审。</p>
      <article v-for="item in context.submissions" :key="item.submissionId">
        <p>工作版本 {{ item.requirementSet.version }} · {{ item.frozenAt }} · {{ reviewState(item.state) }}</p>
        <p class="digest">内容摘要：{{ item.requirementSet.digest }}</p>
        <p>提交投递：{{ transportState(item.transportState) }}</p>
        <p v-if="item.isApproved">该准确需求集版本已形成受控批准；生产用途许可仍需单独确认。</p>
        <button :disabled="locked" @click="selectedSubmission = item.submissionId">查看此版本并评审</button>
      </article>
      <RequirementSetProfessionalReview v-if="selectedSubmission" :requirement-set-id="requirementSet.reference.id" :submission-id="selectedSubmission" :scope="scope" :disabled="locked" @changed="reload" />
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { RequirementSetSnapshot } from '@/api/plm/requirementSet'
import type { RequirementSetScope } from '@/api/plm/requirementSet'
import { requirementSetReviewApi, type ReviewContext, type ReviewPolicyCommand, type ReviewRequirement } from '@/api/plm/requirementSetReview'
import { requirementSetResponse } from '@/auth/requirementSetRecovery'
import { pendingRequirementSetReview, resolveRequirementSetReview, writeRequirementSetReview, type PendingRequirementSetReview } from '@/auth/requirementSetReviewRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { authenticationCanRead } from '@/auth/browserAuthentication'
import { usePermissionStore } from '@/stores/permission'
import RequirementSetProfessionalReview from './RequirementSetProfessionalReview.vue'

const props = defineProps<{ requirementSet: RequirementSetSnapshot; scope: RequirementSetScope; disabled?: boolean; initialSubmission?: string }>()
const selectedSubmission = ref(props.initialSubmission ?? '')
const permissions = usePermissionStore()
const canRead = computed(() => permissions.loaded && permissions.has('plm-requirement-set:query'))
const canPolicy = computed(() => canRead.value && permissions.has('plm-requirement-set:review-policy'))
const canSubmit = computed(() => canRead.value && permissions.has('plm-requirement-set:review-submit'))
const context = ref<ReviewContext | null>(null), roles = ref<{ roleId: number; roleName: string }[]>([])
const busy = ref(false), error = ref(''), notice = ref(''), pending = ref<PendingRequirementSetReview | null>(null)
const requirements = ref<(Omit<ReviewRequirement, 'roleId'> & { roleId: number | null })[]>([])
const external = ref(''), separate = ref(true), from = ref(''), to = ref(''), policyReason = ref('')
const policyState = ref<'ENABLED' | 'REVOKED'>('ENABLED'), reviewBasis = ref(''), changeRelations = ref(''), submitReason = ref(''), replaces = ref<string | null>(null)
const locked = computed(() => busy.value || props.disabled || !!pending.value)
const sameVersion = computed(() => context.value?.requirementSet.reference.digest === props.requirementSet.reference.digest)
const activePolicy = computed(() => { const p = context.value?.policies.at(-1); return p?.fact.body.state === 'ENABLED' ? p : null })
const rejected = computed(() => context.value?.submissions.filter(x => x.state === 'REJECTED' && x.requirementSet.digest === props.requirementSet.reference.digest) ?? [])
let generation = 0, live = true
const lines = (text: string) => text.split(/\r?\n/).map(x => x.trim()).filter(Boolean)
const copy = <T,>(value: T): T => JSON.parse(JSON.stringify(value))
function addRequirement() { requirements.value.push({ code: '', description: '', roleId: null, minimumApprovals: 1 }) }
function roleName(id: number) { return roles.value.find(x => x.roleId === id)?.roleName ?? String(id) }
function reviewState(value: string) { return ({ SUBMITTED: '已送审，待专业评审', APPROVED: '受控版本已批准', REJECTED: '评审已拒绝', DECIDED: '审批已决定，待需求集应用', BLOCKED: '需求集应用待处理' } as Record<string, string>)[value] ?? value }
function transportState(value: string) { return ({ PENDING: '待投递', ACCEPTED: '已受理', DELIVERED: '已受理', OUTCOME_UNKNOWN: '结果待确认', BLOCKED: '待处理' } as Record<string, string>)[value] ?? value }
function syncPending() { pending.value = pendingRequirementSetReview(props.scope) }
function reset() {
  generation++; busy.value = false; context.value = null; roles.value = []; pending.value = null; error.value = notice.value = ''
  requirements.value = []; addRequirement(); external.value = from.value = to.value = policyReason.value = reviewBasis.value = changeRelations.value = submitReason.value = ''
  replaces.value = null; separate.value = true; policyState.value = 'ENABLED'
  selectedSubmission.value = props.initialSubmission ?? ''
}
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || props.disabled || !canRead.value || !authenticationCanRead()) return
  const current = ++generation, stamp = identityStamp(), id = props.requirementSet.reference.id, tenant = props.scope.tenantId, actor = props.scope.actorId
  const check = () => { requireIdentity(stamp); if (!live || current !== generation || !canRead.value || !authenticationCanRead()
    || props.requirementSet.reference.id !== id || props.scope.tenantId !== tenant || props.scope.actorId !== actor) throw new Error('当前需求集或身份已变化。') }
  busy.value = true; error.value = notice.value = ''
  try { check(); syncPending(); await action(check); check(); syncPending() }
  catch (e) { if (live && current === generation) { context.value = null; roles.value = []; error.value = e instanceof Error ? e.message : '需求集评审操作失败。'; try { syncPending() } catch { error.value = '评审原操作记录异常，请保留记录。' } } }
  finally { if (current === generation) busy.value = false }
}
async function loadContext(check: () => void) {
  const value = requirementSetResponse(await requirementSetReviewApi.context(props.requirementSet.reference.id)); check()
  if (value.requirementSetId !== props.requirementSet.reference.id || value.tenantId !== props.scope.tenantId) throw new Error('评审来源与当前需求集不一致。')
  context.value = value
}
async function reload() { await run(loadContext) }
async function loadRoles() { if (!canPolicy.value) return; await run(async check => { const value = requirementSetResponse(await requirementSetReviewApi.roles()); check(); if (!canPolicy.value) throw new Error('当前无规则维护权限。'); roles.value = value }) }
async function savePolicy() {
  const c = context.value; if (!c || locked.value || !canPolicy.value) return
  await run(async check => {
    if (requirements.value.some(x => x.roleId === null)) throw new Error('请选择每项要求对应的专业角色。')
    const body: Omit<ReviewPolicyCommand, 'operationId'> = { state: policyState.value, requirements: copy(requirements.value.map(x => ({ ...x, roleId: x.roleId! }))),
      externalRequirements: lines(external.value), separateAuthorAndReviewer: separate.value, effectiveFrom: new Date(from.value).toISOString(), effectiveTo: new Date(to.value).toISOString(), reason: policyReason.value }
    await writeRequirementSetReview(props.scope, c.requirementSetId, 'POLICY', () => { check(); if (!canPolicy.value) throw new Error('当前无规则维护权限。') }, operationId => requirementSetReviewApi.policy(c.requirementSetId, c.etag, { ...body, operationId }))
    check(); await loadContext(check); notice.value = '评审规则已保存。'
  })
}
async function submit() {
  const c = context.value, policy = activePolicy.value; if (!c || !policy || locked.value || !sameVersion.value || !canSubmit.value) return
  const input = { requirementSet: copy(c.requirementSet.reference), policy: copy(policy.reference), reviewBasis: reviewBasis.value,
    declaredChangeRelations: lines(changeRelations.value), reviewRoundId: crypto.randomUUID(), replacesSubmissionId: replaces.value, reason: submitReason.value }
  await run(async check => {
    await writeRequirementSetReview(props.scope, c.requirementSetId, 'SUBMIT', () => { check(); if (!canSubmit.value) throw new Error('当前无送审权限。') }, operationId => requirementSetReviewApi.submit(c.requirementSetId, c.etag, { ...input, operationId }))
    check(); await loadContext(check); reviewBasis.value = changeRelations.value = submitReason.value = ''; replaces.value = null; notice.value = '准确版本已送审，等待专业评审。'
  })
}
async function recover(close: boolean) { await run(async check => { const value = await resolveRequirementSetReview(props.scope, close, check); check(); await loadContext(check); notice.value = value.state === 'COMMITTED' ? '原评审操作已提交。' : value.state === 'CLOSED' ? '未提交评审操作已结束。' : '原结果仍未知，请继续查询。' }) }
function changed() { reset(); if (authenticationCanRead()) void reload() }
function storage(e: StorageEvent) { if (e.key?.startsWith('cp6_requirementSet_review_operation_v1:')) { try { syncPending() } catch { context.value = null; error.value = '评审原操作记录异常，请保留记录。' } } }
watch(() => [props.requirementSet.reference.id, props.requirementSet.reference.digest, props.scope.tenantId, props.scope.actorId], changed)
watch(() => props.initialSubmission, value => { selectedSubmission.value = value ?? '' })
watch(canRead, allowed => { if (!allowed) reset() })
watch(() => props.disabled, disabled => { if (!disabled && !context.value && !error.value && !busy.value) void reload() })
onMounted(() => { addRequirement(); window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', storage); void reload() })
onBeforeUnmount(() => { live = false; reset(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', storage) })
</script>
<style scoped>
.requirement-set-review{padding:18px;border:1px solid var(--el-border-color);border-radius:8px;background:var(--el-bg-color)}header,.grid{display:flex;gap:12px;align-items:center;flex-wrap:wrap}header{justify-content:space-between}article{padding:12px;margin:12px 0;border:1px solid var(--el-border-color);border-radius:6px}label{display:grid;gap:6px;margin:10px 0}fieldset{border:0;padding:0}input,select,textarea,button{font:inherit;padding:8px;border:1px solid var(--el-border-color);border-radius:4px;color:inherit;background:var(--el-bg-color);max-width:100%;box-sizing:border-box}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}.digest{overflow-wrap:anywhere}[role=alert]{color:var(--el-color-danger)}:focus-visible{outline:2px solid var(--el-color-primary);outline-offset:2px}
</style>
