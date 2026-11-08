<template>
  <section class="payload-disposition-panel">
    <h3>原生导出载荷处置</h3>
    <p>撤销下载只关闭访问。此处按准确制品、明确保留政策和独立批准，删除本库载荷并保留最小回执；WAL、备份、已下载文件及其他业务来源仍未覆盖。</p>
    <el-button :disabled="busy" @click="load">读取载荷处置权限与原操作</el-button>
    <el-alert v-if="message" :title="message" type="info" :closable="false" class="notice" />
    <div v-if="pending" class="notice">
      <p>载荷原操作待确认：{{ pending }}。查询不到不代表没有效果，请查询或关闭原操作。</p>
      <el-button :disabled="busy" @click="recover(false)">查询载荷原操作</el-button>
      <el-button :disabled="busy" @click="recover(true)">关闭载荷原操作</el-button>
    </div>
    <el-form v-if="context" label-width="150px" :disabled="locked">
      <el-form-item label="准确制品 ID"><el-input v-model="draft.artifactId" aria-label="载荷制品 ID" /></el-form-item>
      <el-form-item label="保留政策 ID"><el-input v-model="draft.policyId" aria-label="载荷保留政策 ID" /></el-form-item>
      <el-button :disabled="locked || !targetReady" @click="preview">预览准确载荷与保留条件</el-button>
      <details class="notice">
        <summary>载荷保留政策</summary>
        <p>访问到期不是保留期限。政策须有明确依据；这里不自动判断法律期限。</p>
        <el-button :disabled="locked || !uuid(draft.policyId)" @click="readPolicy">读取载荷政策当前版本</el-button>
        <p v-if="policyLoaded">{{ policy ? `当前版本：${policy.version}` : '此载荷政策尚未登记' }}</p>
        <el-form-item label="政策目标租户"><el-input v-model="draft.targetTenantId" aria-label="载荷政策目标租户" /></el-form-item>
        <el-form-item label="政策依据引用"><el-input v-model="draft.policyBasis" aria-label="载荷政策依据引用" /></el-form-item>
        <el-form-item label="最短保留天数"><el-input v-model="draft.minimumDays" type="number" min="0" :max="context.maximumRetentionDays" aria-label="载荷最短保留天数" /></el-form-item>
        <el-checkbox v-model="draft.enabled">启用载荷政策</el-checkbox>
        <el-checkbox v-model="draft.requireClosed">处置前要求访问已关闭</el-checkbox>
        <el-button :disabled="locked || !policyReady || !can('payload-policy')" @click="configure">确认保存载荷保留政策</el-button>
      </details>
      <div v-if="previewResult" class="notice">
        <h4>准确范围</h4>
        <p>制品：{{ previewResult.scope.artifactId }} · 主体：{{ previewResult.scope.targetTenantId }} / {{ previewResult.scope.subjectId }}</p>
        <p>原 SHA-256：{{ previewResult.scope.originalSha256 }} · 原大小：{{ previewResult.scope.originalLength }} 字节</p>
        <p>当前载荷：{{ previewResult.scope.payloadLength ?? '缺失' }} 字节 · 访问：{{ accessName(previewResult.accessState) }}</p>
        <p>捕获时间：{{ previewResult.scope.capturedAt }} · 最早可处置：{{ previewResult.scope.earliestDispositionAt }}（UTC 连续天数）</p>
        <p v-if="previewResult.blockers.length">当前限制：{{ previewResult.blockers.map(blockerName).join('；') }}</p>
        <p v-if="previewResult.holdReviewOverdue">保留限制已到复核期，仍然生效。</p>
        <el-form-item label="申请依据引用"><el-input v-model="draft.requestRef" aria-label="载荷处置申请依据引用" /></el-form-item>
        <el-button type="primary" :disabled="locked || !can('payload-prepare') || !uuid(draft.requestRef) || !previewMatches || previewResult.blockers.length > 0" @click="requestPlan">提交准确载荷处置计划</el-button>
      </div>
      <details class="notice">
        <summary>主体保留限制</summary>
        <p>从准确制品确定主体，原用户已不存在也可管理。限制对同一主体所有相关处置生效，复核到期不会自动解除。</p>
        <el-button :disabled="locked || !uuid(draft.artifactId)" @click="readHold">读取制品主体保留限制</el-button>
        <p v-if="holdLoaded">{{ hold ? (hold.active ? '保留限制生效' : '保留限制已解除') : '无已登记限制' }}</p>
        <el-form-item label="限制依据引用"><el-input v-model="draft.holdBasis" aria-label="载荷限制依据引用" /></el-form-item>
        <el-form-item label="复核期限"><el-input v-model="draft.reviewAt" type="datetime-local" aria-label="载荷限制复核期限" /></el-form-item>
        <el-button :disabled="locked || !holdReady || !validReview" @click="changeHold(true)">设置载荷主体保留限制</el-button>
        <el-button :disabled="locked || !holdReady || !hold?.active" @click="changeHold(false)">解除载荷主体保留限制</el-button>
      </details>
    </el-form>
    <div v-if="context" class="lookup">
      <el-input v-model="planRef" aria-label="载荷处置计划 ID" placeholder="交给独立审批人的计划 ID" :disabled="busy" />
      <el-button :disabled="busy || !uuid(planRef)" @click="readPlan">读取载荷处置计划</el-button>
    </div>
    <div v-if="plan" class="notice">
      <h4>载荷处置计划 · {{ stateName(plan.state) }}</h4>
      <p>计划 ID：{{ plan.planId }} · 申请人：{{ plan.requesterId }} · 审批人：{{ plan.approverId || '尚无' }}</p>
      <p>冻结制品：{{ plan.scope.artifactId }} · 载荷修订：{{ plan.scope.payloadVersion }}</p>
      <p>冻结政策：{{ plan.scope.policyVersion }} · 最早可处置：{{ plan.scope.earliestDispositionAt }}</p>
      <p>当前访问：{{ accessName(plan.accessState) }} · 当前批准有效：{{ plan.approvalCurrent ? '是' : '否' }}</p>
      <p v-if="plan.currentBlockers.length">当前限制：{{ plan.currentBlockers.map(blockerName).join('；') }}</p>
      <p v-if="plan.requesterId === context?.actorId">申请人不能批准自己的载荷处置计划。</p>
      <el-input v-model="decisionRef" aria-label="载荷审批依据引用" placeholder="审批或撤销依据 UUID" :disabled="locked || !!plan.execution" />
      <el-button type="primary" :disabled="locked || !!plan.execution || !independent || !uuid(decisionRef) || plan.state !== 'REQUESTED' || plan.currentBlockers.length > 0" @click="decide('APPROVE')">独立批准载荷处置</el-button>
      <el-button :disabled="locked || !!plan.execution || !independent || !uuid(decisionRef) || plan.state !== 'REQUESTED'" @click="decide('REJECT')">拒绝载荷处置</el-button>
      <el-button :disabled="locked || !!plan.execution || !can('payload-revoke') || !uuid(decisionRef) || !['REQUESTED','APPROVED'].includes(plan.state)" @click="decide('REVOKE')">撤销载荷处置计划</el-button>
      <div class="notice">
        <el-input v-model="confirmationRef" aria-label="载荷删除确认引用" placeholder="实际删除确认依据 UUID" :disabled="locked || !!plan.execution" />
        <el-button type="danger" :disabled="locked || !!plan.execution || !plan.approvalCurrent || !can('payload-execute') || !uuid(confirmationRef)" @click="execute">确认删除本库载荷</el-button>
        <p>实际删除本库载荷，同时关闭访问。原回执与墓碑保留；备份、外部副本及其他业务数据不会因此清除。</p>
      </div>
    </div>
    <div v-if="receipt" class="payload-execution-result notice">
      <h4>本库原生载荷已删除 · 整体仍为部分范围</h4>
      <p>制品：{{ receipt.artifactId }} · 实际删除：{{ receipt.removedBytes }} 字节</p>
      <p>执行人：{{ receipt.operatorId }} · 时间：{{ receipt.occurredAt }} · 原操作：{{ receipt.operationId }}</p>
      <p>未覆盖：{{ receipt.uncoveredOwners.map(ownerName).join('；') }}</p>
      <p>这是已提交的原回执，查询不会再次删除。</p>
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
import { exportPayloadDispositionApi as api, type PayloadContext, type PayloadPolicy, type PayloadPreview, type PayloadPlan, type PayloadOperation, type PayloadDecision, type PayloadExecution } from '@/api/platform/exportPayloadDisposition'
const context = ref<PayloadContext | null>(null), policy = ref<PayloadPolicy | null>(null), hold = ref<DispositionHold | null>(null)
const previewResult = ref<PayloadPreview | null>(null), plan = ref<PayloadPlan | null>(null)
const originalReceipt = ref<PayloadExecution | null>(null), receipt = computed(() => originalReceipt.value ?? plan.value?.execution ?? null)
const busy = ref(false), pending = ref(''), message = ref(''), storageError = ref(''), planRef = ref(''), decisionRef = ref(''), confirmationRef = ref('')
const policyFor = ref(''), holdFor = ref('')
const emptyDraft = () => ({ artifactId: '', policyId: '', targetTenantId: '', policyBasis: '', minimumDays: '0', enabled: true, requireClosed: true, requestRef: '', holdBasis: '', reviewAt: '' })
const draft = reactive(emptyDraft())
let stamp = identityStamp(), disposed = false, resolved = ''
const active = (start: string) => !disposed && start === stamp && start === identityStamp()
const uuid = (v: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const can = (action: string) => !!context.value?.actions.includes(action)
const locked = computed(() => busy.value || !!pending.value || !!storageError.value)
const targetReady = computed(() => uuid(draft.artifactId) && uuid(draft.policyId))
const policyLoaded = computed(() => !!policyFor.value && policyFor.value === draft.policyId)
const policyReady = computed(() => policyLoaded.value && uuid(draft.targetTenantId) && uuid(draft.policyBasis) && /^\d+$/.test(draft.minimumDays) && Number(draft.minimumDays) <= (context.value?.maximumRetentionDays ?? 0))
const holdLoaded = computed(() => !!holdFor.value && holdFor.value === draft.artifactId)
const holdReady = computed(() => holdLoaded.value && uuid(draft.holdBasis) && can('hold'))
const validReview = computed(() => Number.isFinite(Date.parse(draft.reviewAt)) && Date.parse(draft.reviewAt) > Date.now())
const previewMatches = computed(() => previewResult.value?.scope.artifactId === draft.artifactId && previewResult.value.scope.policyId === draft.policyId)
const independent = computed(() => !!plan.value && plan.value.requesterId !== context.value?.actorId && can('payload-approve'))
const ownerName = (v: string) => ({ MAIN_IDENTITY: '身份资料', 'MAIN.IDENTITY_PROFILE_AND_SECURITY_OR_BUSINESS_HISTORY': '身份资料、安全日志及业务历史', FILE_AND_OTHER_OWNER_COPIES: 'FILE 与其他来源副本', WAL_AND_BACKUPS: 'WAL 与备份', EXTERNAL_DOWNLOADED_COPIES: '外部已下载副本', UNREGISTERED_OWNERS: '未接入来源' }[v] || v)
const accessName = (v: string) => ({ AVAILABLE: '访问开放，载荷仍在', REVOKED: '访问已撤销，未表示载荷已删除', EXPIRED: '访问已到期，未表示载荷已删除', DISPOSED: '本库载荷已删除' }[v] || v)
const stateName = (v: string) => ({ REQUESTED: '待独立审批', APPROVED: '已批准', REJECTED: '已拒绝', REVOKED: '已撤销' }[v] || v)
const blockerName = (v: string) => ({ POLICY_DISABLED: '政策停用', POLICY_TARGET_CHANGED: '政策目标租户已变化', PAYLOAD_ALREADY_DISPOSED: '载荷已处置', PAYLOAD_MISSING: '载荷缺失，不能冒称已删除', PAYLOAD_LENGTH_MISMATCH: '载荷大小与原回执不符', MINIMUM_RETENTION_NOT_REACHED: '最短保留期限未到', ACCESS_MUST_BE_CLOSED: '须先关闭访问', ACTIVE_HOLD: '主体保留限制生效', PAYLOAD_VERSION_CHANGED: '载荷修订已变化', POLICY_VERSION_CHANGED: '政策版本已变化', HOLD_VERSION_CHANGED: '限制版本已变化', APPROVER_AUTHORITY_CHANGED: '批准者权限已变化' }[v] || v)
function key() { const c = context.value!; return `cp6-export-payload-disposition-v1:${c.environmentId}:${c.actorTenantId}:${c.actorId}` }
function stored() { const value = localStorage.getItem(key()) || ''; if (value && !uuid(value)) throw new Error('载荷原操作记录无效，请保留记录并修复浏览器存储。'); return value }
function error(e: unknown) { if (e !== 'cancel' && e !== 'close') message.value = (e as any)?.response?.data?.code || (e as Error)?.message || '操作未完成，请查询原结果。' }
async function run(body: (start: string) => Promise<void>) { if (busy.value) return; const start = stamp; busy.value = true; message.value = ''; try { await body(start) } catch (e) { if (active(start)) error(e) } finally { if (active(start)) busy.value = false } }
async function apply(op: PayloadOperation, start: string) {
  if (!active(start)) return
  if (op.state === 'NOT_FOUND') { pending.value = op.operationId; message.value = '尚未查到原结果，可查询或关闭，不能换号重做。'; return }
  pending.value = ''; resolved = op.operationId; previewResult.value = null
  if (op.state === 'CLOSED') { message.value = '载荷原操作已关闭，迟到请求被阻止。'; return }
  originalReceipt.value = op.execution ?? null
  message.value = op.execution ? '已读取实际删除的原回执，整体仍有未覆盖范围。' : '已读取原操作结果；批准不等于删除。'
  if (op.planId) { planRef.value = op.planId; plan.value = null; const value = await api.plan(op.planId); if (active(start)) plan.value = value }
  if (op.kind === 'POLICY' && op.policyId) { const value = await api.policy(op.policyId); if (active(start)) { policy.value = value; draft.policyId = op.policyId; policyFor.value = op.policyId } }
  if (op.kind === 'HOLD' && op.artifactId) { const value = await api.hold(op.artifactId); if (active(start)) { draft.artifactId = op.artifactId; hold.value = value; holdFor.value = op.artifactId } }
  if (plan.value && !op.planId) { const value = await api.plan(plan.value.planId); if (active(start)) plan.value = value }
}
async function load() { await run(async start => { const c = await api.context(); if (!active(start)) return; context.value = c; storageError.value = ''; let record: string; try { record = stored() } catch (e) { storageError.value = (e as Error).message; return }; if (record) { pending.value = record; await apply(await api.operation(record), start) } }) }
async function mutate(label: string, send: (id: string) => Promise<PayloadOperation>) {
  if (locked.value || !context.value) return
  await run(async start => {
    await ElMessageBox.confirm(label, '确认载荷处置操作', { type: 'warning' }); if (!active(start)) return
    if (!navigator.locks) throw new Error('当前浏览器不支持安全协调，请更换受支持的浏览器。')
    await navigator.locks.request(key(), async () => {
      if (!active(start)) return
      const latest = stored(); if (latest && latest !== resolved) { pending.value = latest; await apply(await api.operation(latest), start); return }
      const id = crypto.randomUUID(); try { localStorage.setItem(key(), id) } catch { storageError.value = '无法保存原操作号，尚未发送请求。'; return }
      pending.value = id; await apply(await send(id), start)
    })
  })
}
async function recover(close: boolean) { const id = pending.value; if (!id) return; await run(async start => { if (close) await ElMessageBox.confirm('关闭尚未到达的原操作，已提交则返回原回执。', '关闭载荷原操作'); if (active(start)) await apply(await (close ? api.close(id) : api.operation(id)), start) }) }
async function readPolicy() { const id = draft.policyId; await run(async start => { policyFor.value = ''; const value = await api.policy(id); if (active(start)) { policy.value = value; policyFor.value = id; if (value) Object.assign(draft, { targetTenantId: value.targetTenantId, policyBasis: value.basisRef, minimumDays: String(value.minimumRetentionDays), enabled: value.enabled, requireClosed: value.requireAccessClosed }) } }) }
async function configure() { if (!policyReady.value || !can('payload-policy')) return; const input = { policyId: draft.policyId, targetTenantId: draft.targetTenantId, expectedVersion: policy.value?.version || null, basisRef: draft.policyBasis, enabled: draft.enabled, minimumRetentionDays: Number(draft.minimumDays), requireAccessClosed: draft.requireClosed }; await mutate(`保存载荷政策 ${input.policyId}，目标租户 ${input.targetTenantId}，最短保留 ${input.minimumRetentionDays} 个 UTC 连续天。${input.requireAccessClosed ? '须先关闭访问。' : '明确允许删除访问尚开放的载荷，执行将一并关闭访问。'}依据 ${input.basisRef}。`, operationId => api.configure({ operationId, ...input })) }
async function preview() { const target = { artifactId: draft.artifactId, policyId: draft.policyId }; await run(async start => { previewResult.value = null; const value = await api.preview(target); if (active(start)) { previewResult.value = value; hold.value = value.hold; holdFor.value = target.artifactId } }) }
async function requestPlan() { const p = previewResult.value; if (!p || !previewMatches.value || p.blockers.length || !can('payload-prepare') || !uuid(draft.requestRef)) return; const input = { target: { artifactId: p.scope.artifactId, policyId: p.scope.policyId }, requestRef: draft.requestRef, expectedScopeDigest: p.scopeDigest }; await mutate(`申请删除准确制品 ${p.scope.artifactId} 的本库载荷，冻结修订 ${p.scope.payloadVersion} 与政策 ${p.scope.policyVersion}。本次只申请独立审批。`, operationId => api.request({ operationId, ...input })) }
async function readHold() { const id = draft.artifactId; await run(async start => { holdFor.value = ''; const value = await api.hold(id); if (active(start)) { hold.value = value; holdFor.value = id } }) }
async function changeHold(value: boolean) { if (!holdReady.value || value && !validReview.value) return; const id = draft.artifactId, input = { expectedVersion: hold.value?.version || null, active: value, basisRef: draft.holdBasis, reviewAt: value ? new Date(draft.reviewAt).toISOString() : null }; await mutate(`${value ? '设置' : '解除'}制品 ${id} 所属主体的全局保留限制，依据 ${input.basisRef}。复核到期不会自动解除，旧批准不会自动恢复。`, operationId => api.setHold(id, { operationId, ...input })) }
async function readPlan() { const id = planRef.value; await run(async start => { plan.value = null; originalReceipt.value = null; const value = await api.plan(id); if (active(start)) plan.value = value }) }
async function decide(decision: PayloadDecision['decision']) { if (!plan.value || !uuid(decisionRef.value)) return; const id = plan.value.planId, input = { expectedVersion: plan.value.version, decision, decisionRef: decisionRef.value }; await mutate(`${decision === 'APPROVE' ? '独立批准' : decision === 'REJECT' ? '拒绝' : '撤销'}载荷计划 ${id}，依据 ${input.decisionRef}。批准不会自动删除。`, operationId => api.decide(id, { operationId, ...input })) }
async function execute() { const p = plan.value; if (!p?.approvalCurrent || p.execution || !can('payload-execute') || !uuid(confirmationRef.value)) return; const input = { expectedVersion: p.version, expectedScopeDigest: p.scopeDigest, confirmationRef: confirmationRef.value }; await mutate(`实际删除制品 ${p.scope.artifactId} 的本库载荷 ${p.scope.payloadLength} 字节并关闭访问，保留最小回执。此处无法还原；WAL、备份和已下载副本仍未清除。确认依据 ${input.confirmationRef}。`, operationId => api.execute(p.planId, { operationId, ...input })) }
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
