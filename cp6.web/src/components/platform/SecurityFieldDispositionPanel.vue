<template>
  <section class="security-field-disposition-panel">
    <h3>安全日志已分类字段处置</h3>
    <p>仅将准确安全事件中的用户名和客户端 IP 置空。事件身份、原时间及其他内容保留；设备、原因、上下文等未知内容仍未覆盖。</p>
    <el-button :disabled="busy" @click="load">读取安全字段处置权限与原操作</el-button>
    <el-alert v-if="message" :title="message" type="info" :closable="false" class="notice" />
    <div v-if="pending" class="notice">
      <p>安全字段原操作待确认：{{ pending }}。查询不到不代表没有效果，请查询或关闭原操作。</p>
      <el-button :disabled="busy" @click="recover(false)">查询安全字段原操作</el-button>
      <el-button :disabled="busy" @click="recover(true)">关闭安全字段原操作</el-button>
    </div>
    <el-form v-if="context" label-width="150px" :disabled="locked">
      <el-form-item label="准确事件 ID"><el-input v-model="draft.logId" aria-label="安全字段事件 ID" /></el-form-item>
      <el-form-item label="保留政策 ID"><el-input v-model="draft.policyId" aria-label="安全字段保留政策 ID" /></el-form-item>
      <el-button :disabled="locked || !targetReady" @click="preview">预览准确安全字段与保留条件</el-button>
      <details class="notice">
        <summary>安全字段保留政策</summary>
        <p>旧事件时间未标注时区，不能据此推断保留期限。政策须显式设置最早可处置时刻及依据，这里不自动判断法律期限。</p>
        <el-button :disabled="locked || !uuid(draft.policyId)" @click="readPolicy">读取安全字段政策当前版本</el-button>
        <p v-if="policyLoaded">{{ policy ? `当前版本：${policy.version}` : '此安全字段政策尚未登记' }}</p>
        <el-form-item label="政策目标租户"><el-input v-model="draft.targetTenantId" aria-label="安全字段政策目标租户" /></el-form-item>
        <el-form-item label="政策依据引用"><el-input v-model="draft.policyBasis" aria-label="安全字段政策依据引用" /></el-form-item>
        <el-form-item label="最早可处置时刻"><el-input v-model="draft.notBefore" type="datetime-local" step="0.001" aria-label="安全字段最早可处置时刻" /></el-form-item>
        <el-checkbox v-model="draft.enabled">启用安全字段政策</el-checkbox>
        <el-button :disabled="locked || !policyReady || !can('security-policy')" @click="configure">确认保存安全字段保留政策</el-button>
      </details>
      <div v-if="previewResult" class="notice">
        <h4>准确范围</h4>
        <p>事件：{{ previewResult.scope.logId }} · 主体：{{ previewResult.scope.targetTenantId }} / {{ previewResult.scope.subjectId }}</p>
        <p>原事件时间：{{ previewResult.scope.originalCreatedAt }}（未标注时区） · 政策最早可处置：{{ previewResult.scope.notBeforeUtc }}（UTC）</p>
        <p>已分类字段：{{ previewResult.scope.fields.join("、") }} · 当前有值：{{ previewResult.classifiedValueCount }} 项</p>
        <p v-if="previewResult.blockers.length">当前限制：{{ previewResult.blockers.map(blockerName).join('；') }}</p>
        <p v-if="previewResult.holdReviewOverdue">保留限制已到复核期，仍然生效。</p>
        <el-form-item label="申请依据引用"><el-input v-model="draft.requestRef" aria-label="安全字段处置申请依据引用" /></el-form-item>
        <el-button type="primary" :disabled="locked || !can('security-prepare') || !uuid(draft.requestRef) || !previewMatches || previewResult.blockers.length > 0" @click="requestPlan">提交准确安全字段处置计划</el-button>
      </div>
      <details class="notice">
        <summary>主体保留限制</summary>
        <p>从准确事件确定主体，原用户已不存在也可管理。限制对同一主体所有相关处置生效，复核到期不会自动解除。</p>
        <el-button :disabled="locked || !uuid(draft.logId)" @click="readHold">读取事件主体保留限制</el-button>
        <p v-if="holdLoaded">{{ hold ? (hold.active ? '保留限制生效' : '保留限制已解除') : '无已登记限制' }}</p>
        <el-form-item label="限制依据引用"><el-input v-model="draft.holdBasis" aria-label="安全字段限制依据引用" /></el-form-item>
        <el-form-item label="复核期限"><el-input v-model="draft.reviewAt" type="datetime-local" aria-label="安全字段限制复核期限" /></el-form-item>
        <el-button :disabled="locked || !holdReady || !validReview" @click="changeHold(true)">设置安全字段主体保留限制</el-button>
        <el-button :disabled="locked || !holdReady || !hold?.active" @click="changeHold(false)">解除安全字段主体保留限制</el-button>
      </details>
    </el-form>
    <div v-if="context" class="lookup">
      <el-input v-model="planRef" aria-label="安全字段处置计划 ID" placeholder="交给独立审批人的计划 ID" :disabled="busy" />
      <el-button :disabled="busy || !uuid(planRef)" @click="readPlan">读取安全字段处置计划</el-button>
    </div>
    <div v-if="plan" class="notice">
      <h4>安全字段处置计划 · {{ stateName(plan.state) }}</h4>
      <p>计划 ID：{{ plan.planId }} · 申请人：{{ plan.requesterId }} · 审批人：{{ plan.approverId || '尚无' }}</p>
      <p>冻结事件：{{ plan.scope.logId }} · 事件修订：{{ plan.scope.targetVersion }}</p>
      <p>冻结政策：{{ plan.scope.policyVersion }} · 最早可处置：{{ plan.scope.notBeforeUtc }}（UTC）</p>
      <p>当前批准有效：{{ plan.approvalCurrent ? "是" : "否" }}</p>
      <p v-if="plan.currentBlockers.length">当前限制：{{ plan.currentBlockers.map(blockerName).join('；') }}</p>
      <p v-if="plan.requesterId === context?.actorId">申请人不能批准自己的安全字段处置计划。</p>
      <el-input v-model="decisionRef" aria-label="安全字段审批依据引用" placeholder="审批或撤销依据 UUID" :disabled="locked || !!plan.execution" />
      <el-button type="primary" :disabled="locked || !!plan.execution || !independent || !uuid(decisionRef) || plan.state !== 'REQUESTED' || plan.currentBlockers.length > 0" @click="decide('APPROVE')">独立批准安全字段处置</el-button>
      <el-button :disabled="locked || !!plan.execution || !independent || !uuid(decisionRef) || plan.state !== 'REQUESTED'" @click="decide('REJECT')">拒绝安全字段处置</el-button>
      <el-button :disabled="locked || !!plan.execution || !can('security-revoke') || !uuid(decisionRef) || !['REQUESTED','APPROVED'].includes(plan.state)" @click="decide('REVOKE')">撤销安全字段处置计划</el-button>
      <div class="notice">
        <el-input v-model="confirmationRef" aria-label="安全字段清理确认引用" placeholder="实际清理确认依据 UUID" :disabled="locked || !!plan.execution" />
        <el-button type="danger" :disabled="locked || !!plan.execution || !plan.approvalCurrent || !can('security-execute') || !uuid(confirmationRef)" @click="execute">确认清理已分类安全字段</el-button>
        <p>实际将 UserName、ClientIp 置空，保留事件身份及其他内容。原回执与墓碑保留，整体仍为部分范围。</p>
      </div>
    </div>
    <div v-if="receipt" class="security-field-execution-result notice">
      <h4>已分类安全字段已清理 · 整体仍为部分范围</h4>
      <p>事件：{{ receipt.logId }} · 实际置空：{{ receipt.clearedValueCount }} 项 · 字段：{{ receipt.fields.join("、") }}</p>
      <p>执行人：{{ receipt.operatorId }} · 时间：{{ receipt.occurredAt }} · 原操作：{{ receipt.operationId }}</p>
      <p>未覆盖：{{ receipt.uncoveredOwners.map(ownerName).join('；') }}</p>
      <p>这是已提交的原回执，查询不会再次清理。</p>
    </div>
    <p v-if="context">未覆盖：{{ context.uncoveredOwners.map(ownerName).join('；') }}</p>
    <el-alert v-if="storageError" :title="storageError" type="error" :closable="false" />
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import type { DispositionHold } from '@/api/platform/privacyDisposition'
import { securityFieldDispositionApi as api, type SecurityFieldContext, type SecurityFieldPolicy, type SecurityFieldPreview, type SecurityFieldPlan, type SecurityFieldOperation, type SecurityFieldDecision, type SecurityFieldExecution } from '@/api/platform/securityFieldDisposition'
const context = ref<SecurityFieldContext | null>(null), policy = ref<SecurityFieldPolicy | null>(null), hold = ref<DispositionHold | null>(null)
const previewResult = ref<SecurityFieldPreview | null>(null), plan = ref<SecurityFieldPlan | null>(null)
const originalReceipt = ref<SecurityFieldExecution | null>(null), receipt = computed(() => originalReceipt.value ?? plan.value?.execution ?? null)
const busy = ref(false), pending = ref(''), message = ref(''), storageError = ref(''), planRef = ref(''), decisionRef = ref(''), confirmationRef = ref('')
const policyFor = ref(''), holdFor = ref('')
const emptyDraft = () => ({ logId: "", policyId: "", targetTenantId: "", policyBasis: "", notBefore: "", enabled: true, requestRef: "", holdBasis: "", reviewAt: "" })
const draft = reactive(emptyDraft())
let stamp = identityStamp(), disposed = false, resolved = ''
const active = (start: string) => !disposed && start === stamp && start === identityStamp()
const uuid = (v: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const can = (action: string) => !!context.value?.actions.includes(action)
const locked = computed(() => busy.value || !!pending.value || !!storageError.value)
const targetReady = computed(() => uuid(draft.logId) && uuid(draft.policyId))
const policyLoaded = computed(() => !!policyFor.value && policyFor.value === draft.policyId)
const policyReady = computed(() => policyLoaded.value && uuid(draft.targetTenantId) && uuid(draft.policyBasis) && Number.isFinite(Date.parse(draft.notBefore)))
const holdLoaded = computed(() => !!holdFor.value && holdFor.value === draft.logId)
const holdReady = computed(() => holdLoaded.value && uuid(draft.holdBasis) && can('hold'))
const validReview = computed(() => Number.isFinite(Date.parse(draft.reviewAt)) && Date.parse(draft.reviewAt) > Date.now())
const previewMatches = computed(() => previewResult.value?.scope.logId === draft.logId && previewResult.value.scope.policyId === draft.policyId)
const independent = computed(() => !!plan.value && plan.value.requesterId !== context.value?.actorId && can('security-approve'))
const ownerName = (v: string) => ({ SECURITY_UNCLASSIFIED_CONTEXT_REASON_DEVICE_AND_ACTOR_METADATA: '未分类上下文、原因、设备与操作者信息', ANONYMOUS_OR_UNATTRIBUTABLE_EVENTS: '匿名及不可归属事件', IDENTITY_AND_BUSINESS_HISTORY: '身份与业务历史', FILE_AND_EXPORT_COPIES: '文件及导出副本', WAL_BACKUPS_AND_EXTERNAL_COPIES: 'WAL、备份及外部副本', UNREGISTERED_OWNERS: '未接入来源' }[v] || v)
const stateName = (v: string) => ({ REQUESTED: '待独立审批', APPROVED: '已批准', REJECTED: '已拒绝', REVOKED: '已撤销' }[v] || v)
const blockerName = (v: string) => ({ POLICY_DISABLED: '政策停用', POLICY_TARGET_CHANGED: '政策目标租户已变化', POLICY_NOT_BEFORE_NOT_REACHED: '政策最早可处置时刻未到', FIELDS_ALREADY_DISPOSED: '字段已处置', NO_CLASSIFIED_CONTENT: '已分类字段没有值，不能冒称新清理', LOG_MISSING: '事件不存在', LOG_BINDING_CHANGED: '事件归属已变化', LOG_VERSION_CHANGED: '事件修订已变化', POLICY_VERSION_CHANGED: '政策版本已变化', HOLD_VERSION_CHANGED: '限制版本已变化', ACTIVE_HOLD: '主体保留限制生效', APPROVER_AUTHORITY_CHANGED: '批准者权限已变化' }[v] || v)
function key() { const c = context.value!; return `cp6-security-fields-v1:${c.environmentId}:${c.actorTenantId}:${c.actorId}` }
function stored() { const value = localStorage.getItem(key()) || ''; if (value && !uuid(value)) throw new Error('安全字段原操作记录无效，请保留记录并修复浏览器存储。'); return value }
function error(e: unknown) { if (e !== 'cancel' && e !== 'close') message.value = (e as any)?.response?.data?.code || (e as Error)?.message || '操作未完成，请查询原结果。' }
async function run(body: (start: string) => Promise<void>) { if (busy.value) return; const start = stamp; busy.value = true; message.value = ''; try { await body(start) } catch (e) { if (active(start)) error(e) } finally { if (active(start)) busy.value = false } }
async function apply(op: SecurityFieldOperation, start: string) {
  if (!active(start)) return
  if (op.state === 'NOT_FOUND') { pending.value = op.operationId; message.value = '尚未查到原结果，可查询或关闭，不能换号重做。'; return }
  pending.value = ''; resolved = op.operationId; previewResult.value = null
  if (op.state === 'CLOSED') { message.value = '安全字段原操作已关闭，迟到请求被阻止。'; return }
  originalReceipt.value = op.execution ?? null
  message.value = op.execution ? '已读取实际清理的原回执，整体仍有未覆盖范围。' : '已读取原操作结果；批准不等于清理。'
  if (op.planId) { planRef.value = op.planId; plan.value = null; const value = await api.plan(op.planId); if (active(start)) plan.value = value }
  if (op.kind === 'POLICY' && op.policyId) { const value = await api.policy(op.policyId); if (active(start)) { policy.value = value; draft.policyId = op.policyId; policyFor.value = op.policyId } }
  if (op.kind === 'HOLD' && op.logId) { const value = await api.hold(op.logId); if (active(start)) { draft.logId = op.logId; hold.value = value; holdFor.value = op.logId } }
  if (plan.value && !op.planId) { const value = await api.plan(plan.value.planId); if (active(start)) plan.value = value }
}
async function load() { await run(async start => { const c = await api.context(); if (!active(start)) return; context.value = c; storageError.value = ''; let record: string; try { record = stored() } catch (e) { storageError.value = (e as Error).message; return }; if (record) { pending.value = record; await apply(await api.operation(record), start) } }) }
async function mutate(label: string, send: (id: string) => Promise<SecurityFieldOperation>) {
  if (locked.value || !context.value) return
  await run(async start => {
    await ElMessageBox.confirm(label, '确认安全字段处置操作', { type: 'warning' }); if (!active(start)) return
    if (!navigator.locks) throw new Error('当前浏览器不支持安全协调，请更换受支持的浏览器。')
    await navigator.locks.request(key(), async () => {
      if (!active(start)) return
      const latest = stored(); if (latest && latest !== resolved) { pending.value = latest; await apply(await api.operation(latest), start); return }
      const id = crypto.randomUUID(); try { localStorage.setItem(key(), id) } catch { storageError.value = '无法保存原操作号，尚未发送请求。'; return }
      pending.value = id; await apply(await send(id), start)
    })
  })
}
async function recover(close: boolean) { const id = pending.value; if (!id) return; await run(async start => { if (close) await ElMessageBox.confirm('关闭尚未到达的原操作，已提交则返回原回执。', '关闭安全字段原操作'); if (active(start)) await apply(await (close ? api.close(id) : api.operation(id)), start) }) }
async function readPolicy() { const id = draft.policyId; await run(async start => { policyFor.value = ''; const value = await api.policy(id); if (active(start)) { policy.value = value; policyFor.value = id; if (value) Object.assign(draft, { targetTenantId: value.targetTenantId, policyBasis: value.basisRef, notBefore: localTime(value.notBeforeUtc), enabled: value.enabled }) } }) }
function localTime(value: string) { const d = new Date(value); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, -1) }
async function configure() { if (!policyReady.value || !can('security-policy')) return; const input = { policyId: draft.policyId, targetTenantId: draft.targetTenantId, expectedVersion: policy.value?.version || null, basisRef: draft.policyBasis, enabled: draft.enabled, notBeforeUtc: policy.value && draft.notBefore === localTime(policy.value.notBeforeUtc) ? policy.value.notBeforeUtc : new Date(draft.notBefore).toISOString() }; await mutate(`保存安全字段政策 ${input.policyId}，目标租户 ${input.targetTenantId}，最早可处置 ${input.notBeforeUtc}，依据 ${input.basisRef}。旧事件时间不作为 UTC 期限依据。`, operationId => api.configure({ operationId, ...input })) }
async function preview() { const target = { logId: draft.logId, policyId: draft.policyId }; await run(async start => { previewResult.value = null; const value = await api.preview(target); if (active(start)) { previewResult.value = value; hold.value = value.hold; holdFor.value = target.logId } }) }
async function requestPlan() { const p = previewResult.value; if (!p || !previewMatches.value || p.blockers.length || !can('security-prepare') || !uuid(draft.requestRef)) return; const input = { target: { logId: p.scope.logId, policyId: p.scope.policyId }, requestRef: draft.requestRef, expectedScopeDigest: p.scopeDigest }; await mutate(`申请清理准确事件 ${p.scope.logId} 的 UserName、ClientIp，冻结修订 ${p.scope.targetVersion} 与政策 ${p.scope.policyVersion}。只申请独立审批，其他内容保留。`, operationId => api.request({ operationId, ...input })) }
async function readHold() { const id = draft.logId; await run(async start => { holdFor.value = ''; const value = await api.hold(id); if (active(start)) { hold.value = value; holdFor.value = id } }) }
async function changeHold(value: boolean) { if (!holdReady.value || value && !validReview.value) return; const id = draft.logId, input = { expectedVersion: hold.value?.version || null, active: value, basisRef: draft.holdBasis, reviewAt: value ? new Date(draft.reviewAt).toISOString() : null }; await mutate(`${value ? '设置' : '解除'}事件 ${id} 所属主体的全局保留限制，依据 ${input.basisRef}。复核到期不会自动解除，旧批准不会自动恢复。`, operationId => api.setHold(id, { operationId, ...input })) }
async function readPlan() { const id = planRef.value; await run(async start => { plan.value = null; originalReceipt.value = null; const value = await api.plan(id); if (active(start)) plan.value = value }) }
async function decide(decision: SecurityFieldDecision['decision']) { if (!plan.value || !uuid(decisionRef.value)) return; const id = plan.value.planId, input = { expectedVersion: plan.value.version, decision, decisionRef: decisionRef.value }; await mutate(`${decision === 'APPROVE' ? '独立批准' : decision === 'REJECT' ? '拒绝' : '撤销'}安全字段计划 ${id}，依据 ${input.decisionRef}。批准不会自动清理。`, operationId => api.decide(id, { operationId, ...input })) }
async function execute() { const p = plan.value; if (!p?.approvalCurrent || p.execution || !can('security-execute') || !uuid(confirmationRef.value)) return; const input = { expectedVersion: p.version, expectedScopeDigest: p.scopeDigest, confirmationRef: confirmationRef.value }; await mutate(`实际将事件 ${p.scope.logId} 的 UserName、ClientIp 置空，保留事件身份、原时间及其他内容。整体仍有未覆盖范围。确认依据 ${input.confirmationRef}。`, operationId => api.execute(p.planId, { operationId, ...input })) }
function identityChanged() { const next = identityStamp(); if (next === stamp) return; stamp = next; resolved = ''; context.value = null; policy.value = null; hold.value = null; previewResult.value = null; plan.value = null; originalReceipt.value = null; pending.value = ''; planRef.value = ''; decisionRef.value = ''; confirmationRef.value = ''; policyFor.value = ''; holdFor.value = ''; busy.value = false; message.value = ''; storageError.value = ''; Object.assign(draft, emptyDraft()) }
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onMounted(load); onBeforeUnmount(() => { disposed = true; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<style scoped>
.notice, .lookup { margin-top: 18px; }
.lookup { display: flex; gap: 12px; }
p { line-height: 1.6; overflow-wrap: anywhere; }
summary { cursor: pointer; font-weight: 600; }
</style>
