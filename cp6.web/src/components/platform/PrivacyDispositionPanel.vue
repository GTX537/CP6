<template>
  <section class="privacy-disposition-panel">
    <h3>主体资料处置计划</h3>
    <p>先预览精确范围，再由独立审批人决定。批准不会自动更改资料；当前尚未提供擦除执行。范围外的业务记录、历史资料和副本仍然保留。</p>
    <el-button :disabled="busy" @click="load">读取处置权限与原操作</el-button>
    <el-alert v-if="message" :title="message" type="info" :closable="false" class="notice" />
    <div v-if="pending" class="notice">
      <p>原操作结果待确认：<code>{{ pending }}</code>。查询不到不代表请求未到达；关闭会阻止迟到请求。</p>
      <el-button :disabled="busy" @click="recover(false)">查询处置原操作</el-button>
      <el-button :disabled="busy" @click="recover(true)">关闭处置原操作</el-button>
    </div>
    <el-form v-if="context" label-width="130px" :disabled="locked">
      <el-form-item label="目标租户 ID"><el-input v-model="draft.targetTenantId" aria-label="处置目标租户 ID" /></el-form-item>
      <el-form-item label="主体用户 ID"><el-input v-model="draft.subjectId" aria-label="处置主体用户 ID" /></el-form-item>
      <el-form-item label="政策 ID"><el-input v-model="draft.policyId" aria-label="处置政策 ID" /></el-form-item>
      <el-button :disabled="!targetReady" @click="preview">读取精确范围预览</el-button>
      <details class="notice">
        <summary>政策管理</summary>
        <p>依据引用应指向已登记的处理依据；这里不自动判定法律适用性。</p>
        <el-button :disabled="!uuid(draft.policyId)" @click="readPolicy">读取政策当前版本</el-button>
        <p v-if="policyLoaded">{{ policy ? `当前版本：${policy.version}` : '此政策尚未登记' }}</p>
        <el-form-item label="政策依据引用"><el-input v-model="draft.policyBasis" aria-label="政策依据引用" /></el-form-item>
        <el-checkbox v-model="draft.policyEnabled">启用政策</el-checkbox>
        <el-button :disabled="!policyReady || !can('policy')" @click="configure">确认保存政策</el-button>
      </details>
      <div v-if="previewResult" class="scope-result">
        <h4>精确范围预览</h4>
        <p>目标：{{ previewResult.scope.targetTenantId }} / {{ previewResult.scope.subjectId }}</p>
        <p>字段：{{ previewResult.scope.fields.join('、') }}</p>
        <p>拟执行效果：{{ previewResult.scope.effects.map(effectName).join('；') }}</p>
        <p>未覆盖：{{ previewResult.scope.uncoveredOwners.map(ownerName).join('；') }}</p>
        <p v-if="previewResult.blockers.length">当前限制：{{ previewResult.blockers.map(blockerName).join('；') }}</p>
        <el-form-item label="申请依据引用"><el-input v-model="draft.requestRef" aria-label="处置申请依据引用" /></el-form-item>
        <el-button type="primary" :disabled="!can('prepare') || !uuid(draft.requestRef) || previewResult.blockers.length > 0 || !previewMatches" @click="requestPlan">确认范围并提交计划</el-button>
      </div>
      <details class="notice" open>
        <summary>保留限制（hold）</summary>
        <p>限制对同一主体的处置生效。期限用于提醒复核，到期不会自动解除。</p>
        <el-button :disabled="!subjectReady" @click="readHold">读取保留限制</el-button>
        <p v-if="holdLoaded">{{ hold ? (hold.active ? '保留限制生效' : '保留限制已解除') : '无已登记限制' }}<span v-if="hold"> · 管理人 {{ hold.ownerId }} · 复核期限 {{ hold.reviewAt || '无' }}</span></p>
        <el-form-item label="限制依据引用"><el-input v-model="draft.holdBasis" aria-label="限制依据引用" /></el-form-item>
        <el-form-item label="复核期限"><el-input v-model="draft.reviewAt" type="datetime-local" aria-label="保留限制复核期限" /></el-form-item>
        <el-button :disabled="!holdReady || !can('hold') || !validReview" @click="changeHold(true)">设置或续期保留限制</el-button>
        <el-button :disabled="!holdReady || !hold?.active || !can('hold')" @click="changeHold(false)">解除保留限制</el-button>
      </details>
    </el-form>
    <div v-if="context" class="lookup">
      <el-input v-model="planRef" aria-label="处置计划 ID" placeholder="填写计划 ID，交给独立审批人读取" :disabled="busy" />
      <el-button :disabled="busy || !uuid(planRef)" @click="readPlan">读取计划当前状态</el-button>
    </div>
    <div v-if="plan" class="plan-result">
      <h4>计划 {{ stateName(plan.state) }}</h4>
      <p>计划 ID：{{ plan.planId }} · 申请人：{{ plan.requesterId }} · 审批人：{{ plan.approverId || '尚无' }}</p>
      <p>冻结目标：{{ plan.scope.targetTenantId }} / {{ plan.scope.subjectId }}</p>
      <p>冻结政策版本：{{ plan.scope.policyVersion }} · 依据：{{ plan.scope.basisRef }}</p>
      <p>字段：{{ plan.scope.fields.join('、') }}</p>
      <p>拟执行效果：{{ plan.scope.effects.map(effectName).join('；') }}</p>
      <p>未覆盖：{{ plan.scope.uncoveredOwners.map(ownerName).join('；') }}</p>
      <p>当前批准有效：{{ plan.approvalCurrent ? '是' : '否' }}。尚未执行资料处置。</p>
      <p v-if="plan.currentBlockers.length">当前限制：{{ plan.currentBlockers.map(blockerName).join('；') }}</p>
      <p v-if="plan.holdReviewOverdue">保留限制已到复核期限，仍然生效。</p>
      <p v-if="plan.requesterId === context?.actorId">申请人不能审批自己的计划。</p>
      <el-input v-model="decisionRef" aria-label="审批依据引用" placeholder="审批或撤销依据引用 UUID" :disabled="locked" />
      <el-button type="primary" :disabled="locked || !decisionReady || !independent || plan.state !== 'REQUESTED' || plan.currentBlockers.length > 0" @click="decide('APPROVE')">独立批准计划</el-button>
      <el-button :disabled="locked || !decisionReady || !independent || plan.state !== 'REQUESTED'" @click="decide('REJECT')">拒绝计划</el-button>
      <el-button type="warning" :disabled="locked || !uuid(decisionRef) || !can('revoke') || !['REQUESTED','APPROVED'].includes(plan.state)" @click="decide('REVOKE')">撤销计划</el-button>
    </div>
    <el-alert v-if="storageError" :title="storageError" type="error" :closable="false" />
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { privacyDispositionApi as api, type DispositionContext, type DispositionHold, type DispositionPolicy, type DispositionPreview, type DispositionPlan, type DispositionOperation, type DispositionDecision } from '@/api/platform/privacyDisposition'
const context = ref<DispositionContext | null>(null), hold = ref<DispositionHold | null>(null), policy = ref<DispositionPolicy | null>(null)
const previewResult = ref<DispositionPreview | null>(null), plan = ref<DispositionPlan | null>(null)
const busy = ref(false), pending = ref(''), message = ref(''), storageError = ref(''), planRef = ref(''), decisionRef = ref('')
const policyFor = ref(''), holdFor = ref('')
const draft = reactive({ targetTenantId: '', subjectId: '', policyId: '', policyBasis: '', policyEnabled: true, requestRef: '', holdBasis: '', reviewAt: '' })
let stamp = identityStamp(), disposed = false, resolvedOperation = ''
const active = (start: string) => !disposed && start === stamp && start === identityStamp()
const uuid = (v: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const can = (action: string) => !!context.value?.actions.includes(action)
const locked = computed(() => busy.value || !!pending.value || !!storageError.value)
const subjectReady = computed(() => [draft.targetTenantId, draft.subjectId].every(uuid))
const targetReady = computed(() => subjectReady.value && uuid(draft.policyId))
const policyLoaded = computed(() => policyFor.value === draft.policyId && !!policyFor.value)
const holdLoaded = computed(() => holdFor.value === `${draft.targetTenantId}/${draft.subjectId}`)
const policyReady = computed(() => policyLoaded.value && uuid(draft.targetTenantId) && uuid(draft.policyBasis))
const holdReady = computed(() => holdLoaded.value && subjectReady.value && uuid(draft.holdBasis))
const validReview = computed(() => Number.isFinite(Date.parse(draft.reviewAt)) && Date.parse(draft.reviewAt) > Date.now())
const previewMatches = computed(() => { const s = previewResult.value?.scope; return s && s.subjectId === draft.subjectId && s.targetTenantId === draft.targetTenantId && s.policyId === draft.policyId })
const independent = computed(() => !!plan.value && plan.value.requesterId !== context.value?.actorId && can('approve'))
const decisionReady = computed(() => uuid(decisionRef.value))
const effectName = (v: string) => ({ PSEUDONYMIZE_LOGIN_NAME_PRESERVE_STABLE_ID: '登录名去标识化，保留稳定用户 ID', CLEAR_LISTED_PROFILE_FIELDS: '清理列出的资料字段', DISABLE_LOGIN_AND_REVOKE_AUTHENTICATION_MATERIAL: '停用登录并撤销认证材料' }[v] || v)
const ownerName = (v: string) => ({ 'MAIN.SECURITY_LOG_CONTENT': '安全日志内容', 'MAIN.AUTHORIZATION_AND_ORGANIZATION_REFERENCES': '授权及组织关联', 'MAIN.OA_AND_PLM_HISTORY': 'OA 和 PLM 历史', CRM_BUSINESS_AND_HISTORY: 'CRM 业务及历史', FILE_AND_PRIOR_EXPORT_COPIES: '文件及既有导出副本', BACKUPS_AND_EXTERNAL_COPIES: '备份及外部副本', UNREGISTERED_OWNERS: '尚未接入的业务来源' }[v] || v)
const blockerName = (v: string) => ({ ACTIVE_HOLD: '保留限制生效', POLICY_DISABLED: '政策停用', TARGET_VERSION_CHANGED: '主体版本已变化', POLICY_VERSION_CHANGED: '政策版本已变化', HOLD_VERSION_CHANGED: '保留限制版本已变化', APPROVER_AUTHORITY_CHANGED: '审批人权限已变化', PLATFORM_AUTHORITY_MUST_BE_REVOKED_SEPARATELY: '须先独立撤销主体的平台权限', PLATFORM_TENANT_PROTECTED: '平台租户受保护' }[v] || v)
const stateName = (v: string) => ({ REQUESTED: '待独立审批', APPROVED: '已批准', REJECTED: '已拒绝', REVOKED: '已撤销' }[v] || v)
function key() { const c = context.value!; return `cp6-privacy-disposition-v1:${c.environmentId}:${c.actorTenantId}:${c.actorId}` }
function stored() { const value = localStorage.getItem(key()) || ''; if (value && !uuid(value)) throw new Error('原操作记录无效，请保留记录并修复浏览器存储。'); return value }
function error(e: unknown) { if (e !== 'cancel' && e !== 'close') message.value = (e as any)?.response?.data?.code || (e as Error)?.message || '操作未完成，请查询原结果。' }
async function run(body: (start: string) => Promise<void>) { if (busy.value) return; const start = stamp; busy.value = true; message.value = ''; try { await body(start) } catch (e) { if (active(start)) error(e) } finally { if (active(start)) busy.value = false } }
async function apply(op: DispositionOperation, start: string) {
  if (!active(start)) return
  if (op.state === 'NOT_FOUND') { pending.value = op.operationId; message.value = '尚未查到原结果，可查询或关闭；不能另行重复提交。'; return }
  pending.value = ''; resolvedOperation = op.operationId; previewResult.value = null
  message.value = op.state === 'CLOSED' ? '原操作已关闭，迟到请求不会改变计划。' : '已读取原操作结果；批准不会自动更改主体资料。'
  if (op.state === 'CLOSED') return
  if (op.planId) { const value = await api.plan(op.planId); if (active(start)) { planRef.value = op.planId; plan.value = value } }
  if (op.kind === 'POLICY' && op.policyId) { const value = await api.policy(op.policyId); if (active(start)) { draft.policyId = op.policyId; policy.value = value; policyFor.value = op.policyId } }
  if (op.kind === 'HOLD' && op.targetTenantId && op.subjectId) {
    const value = await api.hold(op.targetTenantId, op.subjectId)
    if (active(start)) { draft.targetTenantId = op.targetTenantId; draft.subjectId = op.subjectId; hold.value = value; holdFor.value = `${op.targetTenantId}/${op.subjectId}` }
  }
  if (plan.value && !op.planId) { const value = await api.plan(plan.value.planId); if (active(start)) plan.value = value }
}
async function load() { await run(async start => {
  const c = await api.context(); if (!active(start)) return; context.value = c; storageError.value = ''
  let original: string; try { original = stored() } catch (e) { storageError.value = (e as Error).message; return }
  if (original) { pending.value = original; await apply(await api.operation(original), start) }
}) }
async function mutate(label: string, send: (id: string) => Promise<DispositionOperation>) {
  if (locked.value || !context.value) return
  await run(async start => {
    await ElMessageBox.confirm(label, '确认处置计划操作', { type: 'warning' }); if (!active(start)) return
    if (!navigator.locks) throw new Error('当前浏览器不支持安全协调，请更换受支持的浏览器。')
    await navigator.locks.request(key(), async () => {
      if (!active(start)) return
      const latest = stored(); if (latest && latest !== resolvedOperation) { pending.value = latest; await apply(await api.operation(latest), start); return }
      const id = crypto.randomUUID(); try { localStorage.setItem(key(), id) } catch { storageError.value = '无法保存原操作号，尚未发送请求。'; return }
      pending.value = id; await apply(await send(id), start)
    })
  })
}
async function recover(close: boolean) { if (!pending.value) return; const id = pending.value; await run(async start => {
  if (close) await ElMessageBox.confirm('关闭尚未到达的原操作；已提交的操作会返回原结果。', '关闭原操作')
  if (active(start)) await apply(await (close ? api.close(id) : api.operation(id)), start)
}) }
async function readPolicy() { const id = draft.policyId; await run(async start => { policyFor.value = ''; const value = await api.policy(id); if (active(start)) { policy.value = value; policyFor.value = id; if (value) { draft.policyBasis = value.basisRef; draft.policyEnabled = value.enabled } } }) }
async function configure() {
  if (!policyReady.value || !can('policy')) return
  const command = { policyId: draft.policyId, targetTenantId: draft.targetTenantId, expectedVersion: policy.value?.version || null, basisRef: draft.policyBasis, enabled: draft.policyEnabled }
  await mutate(`保存政策 ${command.policyId}，目标租户 ${command.targetTenantId}，依据 ${command.basisRef}，${command.enabled ? '启用' : '停用'}。既有计划不会自动更新。`, operationId => api.configure({ operationId, ...command }))
}
async function preview() { const target = { targetTenantId: draft.targetTenantId, subjectId: draft.subjectId, policyId: draft.policyId }; await run(async start => { previewResult.value = null; const value = await api.preview(target); if (active(start)) { previewResult.value = value; hold.value = value.hold; holdFor.value = `${target.targetTenantId}/${target.subjectId}` } }) }
async function requestPlan() {
  if (!previewMatches.value || !previewResult.value || !uuid(draft.requestRef) || !can('prepare') || previewResult.value.blockers.length) return
  const p = previewResult.value, command = { requestRef: draft.requestRef, target: { targetTenantId: p.scope.targetTenantId, subjectId: p.scope.subjectId, policyId: p.scope.policyId }, expectedScopeDigest: p.scopeDigest }
  await mutate(`冻结主体 ${p.scope.subjectId} 的列出字段和效果，政策版本 ${p.scope.policyVersion}。未覆盖范围保持不变；本次仅提交审批计划。`, operationId => api.request({ operationId, ...command }))
}
async function readHold() { const tenant = draft.targetTenantId, subject = draft.subjectId; await run(async start => { holdFor.value = ''; const value = await api.hold(tenant, subject); if (active(start)) { hold.value = value; holdFor.value = `${tenant}/${subject}` } }) }
async function changeHold(activeHold: boolean) {
  if (!holdReady.value || !can('hold') || activeHold && !validReview.value) return
  const command = { targetTenantId: draft.targetTenantId, subjectId: draft.subjectId, expectedVersion: hold.value?.version || null, active: activeHold, basisRef: draft.holdBasis, reviewAt: activeHold ? new Date(draft.reviewAt).toISOString() : null }
  await mutate(`${activeHold ? '设置或续期' : '解除'}主体 ${command.subjectId} 的保留限制；依据 ${command.basisRef}。${activeHold ? `复核期限 ${command.reviewAt}，到期仍保持限制。` : '解除不会恢复旧计划批准，须重新预览申请。'}`, operationId => api.setHold({ operationId, ...command }))
}
async function readPlan() { const id = planRef.value; await run(async start => { plan.value = null; const value = await api.plan(id); if (active(start)) plan.value = value }) }
async function decide(decision: DispositionDecision['decision']) {
  if (!plan.value || !uuid(decisionRef.value)) return
  const id = plan.value.planId, command = { expectedVersion: plan.value.version, decision, decisionRef: decisionRef.value }
  await mutate(`${decision === 'APPROVE' ? '独立批准' : decision === 'REJECT' ? '拒绝' : '撤销'}计划 ${id}；依据 ${command.decisionRef}。批准不等于已经执行。`, operationId => api.decide(id, { operationId, ...command }))
}
function identityChanged() {
  const next = identityStamp(); if (next === stamp) return
  stamp = next; resolvedOperation = ''; context.value = null; hold.value = null; policy.value = null; previewResult.value = null; plan.value = null
  pending.value = ''; planRef.value = ''; decisionRef.value = ''; policyFor.value = ''; holdFor.value = ''; busy.value = false; message.value = ''; storageError.value = ''
  Object.assign(draft, { targetTenantId: '', subjectId: '', policyId: '', policyBasis: '', policyEnabled: true, requestRef: '', holdBasis: '', reviewAt: '' })
}
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onMounted(load); onBeforeUnmount(() => { disposed = true; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<style scoped>
.notice, .scope-result, .lookup, .plan-result { margin-top: 18px; }
.lookup { display: flex; gap: 12px; }
p { line-height: 1.6; overflow-wrap: anywhere; }
summary { cursor: pointer; font-weight: 600; }
</style>
