<template>
  <section class="privacy-restore-panel">
    <h3>受控恢复候选</h3>
    <p>仅处理管理员已登记的新库候选。源库保留处置权威，候选在准确墓碑重施并保存原结果前保持隔离；其他业务、文件和环境切换仍有独立门禁。</p>
    <el-button :disabled="busy" @click="load">读取恢复候选与原操作</el-button>
    <el-alert v-if="message" :title="message" type="info" :closable="false" class="notice" />
    <div v-if="pending" class="notice">
      <p>恢复原操作待确认：{{ pending.slice(2) }}。查询不到不代表没有效果，禁止换号盲目重做。</p>
      <el-button :disabled="busy" @click="recover('READ')">查询恢复原操作</el-button>
      <el-button v-if="pending.startsWith('E:')" :disabled="busy" @click="recover('RECOVER')">核对并保存恢复原结果</el-button>
      <el-button :disabled="busy" @click="recover('CLOSE')">关闭恢复原操作</el-button>
    </div>
    <el-form v-if="context" label-width="140px" :disabled="locked">
      <p v-if="context.candidates.length === 0">尚无已登记候选。请由负责恢复的管理员登记新库、准确备份及连接引用后继续。</p>
      <el-form-item label="恢复候选">
        <select v-model="candidateId" aria-label="恢复候选" :disabled="locked"><option v-for="item in context.candidates" :key="item.id" :value="item.id">{{ item.displayName }}</option></select>
      </el-form-item>
      <el-button :disabled="!uuid(candidateId)" @click="preview">预览恢复范围与当前限制</el-button>
      <div v-if="previewResult" class="restore-preview">
        <p>目标库：{{ previewResult.scope.target.name }} · 数据库身份：{{ previewResult.scope.target.oid }}</p>
        <p>备份 SHA-256：{{ previewResult.scope.backupSha256 }}</p>
        <p>已接身份墓碑：{{ previewResult.scope.tombstoneCount }} · 导出载荷墓碑：{{ previewResult.scope.exportPayloadTombstoneCount || 0 }}；当前来源版本：{{ previewResult.scope.sourceVersion }}</p>
        <p>未覆盖：{{ previewResult.scope.uncoveredOwners.map(ownerName).join('；') }}</p>
        <p v-if="previewResult.blockers.length">当前限制：{{ previewResult.blockers.join('；') }}</p>
        <el-form-item label="申请依据引用"><el-input v-model="requestRef" aria-label="恢复申请依据引用" /></el-form-item>
        <el-button type="primary" :disabled="!can('restore-prepare') || !uuid(requestRef) || previewResult.scope.candidateId !== candidateId || previewResult.blockers.length > 0" @click="prepare">提交恢复计划</el-button>
      </div>
    </el-form>
    <div v-if="context" class="lookup">
      <el-input v-model="planRef" aria-label="恢复计划 ID" placeholder="交给独立审批人的恢复计划 ID" :disabled="busy" />
      <el-button :disabled="busy || !uuid(planRef)" @click="readPlan">读取恢复计划</el-button>
    </div>
    <div v-if="plan" class="restore-plan">
      <h4>恢复计划 · {{ stateName(plan.state) }}</h4>
      <p>计划 ID：{{ plan.planId }} · 申请人：{{ plan.requesterId }}</p>
      <p>准确目标库：{{ plan.scope.target.name }} · 身份：{{ plan.scope.target.oid }}</p>
      <p>备份：{{ plan.scope.backupSha256 }} · 冻结来源版本：{{ plan.scope.sourceVersion }}</p>
      <p>已接身份墓碑：{{ plan.scope.tombstoneCount }} · 导出载荷墓碑：{{ plan.scope.exportPayloadTombstoneCount || 0 }}；未覆盖：{{ plan.scope.uncoveredOwners.map(ownerName).join('；') }}</p>
      <p>当前批准有效：{{ plan.approvalCurrent ? '是' : '否' }}。批准不会自动重施资料。</p>
      <p v-if="plan.currentBlockers.length">当前限制：{{ plan.currentBlockers.join('；') }}</p>
      <p v-if="plan.requesterId === context?.actorId">申请人不能批准自己的恢复计划。</p>
      <el-input v-model="decisionRef" aria-label="恢复审批依据引用" placeholder="独立审批或撤销依据 UUID" :disabled="locked || !!plan.outcome" />
      <el-button type="primary" :disabled="locked || !!plan.outcome || !can('restore-approve') || !uuid(decisionRef) || plan.requesterId === context?.actorId || plan.state !== 'REQUESTED' || plan.currentBlockers.length > 0" @click="decide('APPROVE')">独立批准恢复计划</el-button>
      <el-button :disabled="locked || !!plan.outcome || !can('restore-approve') || !uuid(decisionRef) || plan.requesterId === context?.actorId || plan.state !== 'REQUESTED'" @click="decide('REJECT')">拒绝恢复计划</el-button>
      <el-button :disabled="locked || !!plan.outcome || !can('restore-approve') || !uuid(decisionRef) || !['REQUESTED','APPROVED'].includes(plan.state)" @click="decide('REVOKE')">撤销恢复计划</el-button>
      <div class="notice">
        <el-input v-model="confirmationRef" aria-label="恢复执行确认引用" placeholder="重施处置的明确确认依据 UUID" :disabled="locked || !!plan.outcome" />
        <el-button type="danger" :disabled="locked || !!plan.outcome || !plan.approvalCurrent || !can('restore-execute') || !uuid(confirmationRef)" @click="execute">确认重施墓碑并核对开放门</el-button>
        <p>会在准确候选中重施已经完成的处置，停用相应认证。候选和源结果都核实后才允许通过隐私门；来源政策、保留限制或新处置改变后，旧资格失效。</p>
      </div>
    </div>
    <div v-if="result" class="restore-result">
      <h4 v-if="result.state === 'APPLIED'">已接墓碑已重施 · 整体仍为部分范围</h4>
      <p v-else-if="result.state === 'AWAITING_SOURCE_RESULT'">候选已有原回执，源结果待保存。候选继续保持隔离，请核对并保存恢复原结果。</p>
      <p v-else-if="['PENDING','NOT_FOUND'].includes(result.state)">尚无已核实的候选结果。请查询、核对或关闭原操作，不要重新执行。</p>
      <p v-else-if="result.state === 'CLOSED'">原操作已关闭，迟到执行被阻止。</p>
      <p>原操作：{{ result.operationId }}</p>
      <p v-for="unit in result.units || []" :key="unit.sourcePlanId">{{ unit.artifactId ? `原生导出制品 ${unit.artifactId}` : `主体 ${unit.targetTenantId} / ${unit.subjectId}` }}：{{ unit.outcome === 'ABSENT_FENCED' ? '原备份中不存在，墓碑已施加' : '原处置已重施' }}<span v-if="unit.artifactId"> · 实际删除 {{ unit.removedBytes }} 字节</span></p>
      <p>此处仅证明上列来源的实际结果。其他历史、文件、WAL、备份和完整生产恢复仍有未覆盖范围。</p>
    </div>
    <el-alert v-if="storageError" :title="storageError" type="error" :closable="false" />
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { privacyRestoreApi as api, type RestoreContext, type RestorePreview, type RestorePlan, type RestoreMetaOperation, type RestoreResult } from '@/api/platform/privacyRestore'
const context = ref<RestoreContext | null>(null), previewResult = ref<RestorePreview | null>(null), plan = ref<RestorePlan | null>(null), result = ref<RestoreResult | null>(null)
const candidateId = ref(''), requestRef = ref(''), planRef = ref(''), decisionRef = ref(''), confirmationRef = ref('')
const busy = ref(false), pending = ref(''), message = ref(''), storageError = ref('')
let stamp = identityStamp(), disposed = false, resolved = ''
const active = (start: string) => !disposed && start === stamp && start === identityStamp()
const uuid = (v: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const can = (action: string) => !!context.value?.actions.includes(action)
const locked = computed(() => busy.value || !!pending.value || !!storageError.value)
const ownerName = (v: string) => ({ 'MAIN.SECURITY_LOG_CONTENT': '安全日志内容', 'MAIN.AUTHORIZATION_AND_ORGANIZATION_REFERENCES': '授权与组织引用', 'MAIN.OA_AND_PLM_HISTORY': 'OA 与 PLM 历史', CRM_BUSINESS_AND_HISTORY: 'CRM 业务及历史', FILE_AND_PRIOR_EXPORT_COPIES: '文件与旧导出副本', FILE_AND_OTHER_OWNER_COPIES: 'FILE 与其他来源副本', BACKUPS_AND_EXTERNAL_COPIES: '备份与外部副本', UNREGISTERED_OWNERS: '其他未接入来源' }[v] || v)
const stateName = (v: string) => ({ REQUESTED: '待独立审批', APPROVED: '已批准', REJECTED: '已拒绝', REVOKED: '已撤销' }[v] || v)
function key() { const c = context.value!; return `cp6-privacy-restore-v1:${c.environmentId}:${c.actorTenantId}:${c.actorId}` }
function stored() { const value = localStorage.getItem(key()) || ''; if (value && (!/^[ME]:/.test(value) || !uuid(value.slice(2)))) throw new Error('恢复原操作记录无效，请保留记录并修复浏览器存储。'); return value }
function error(e: unknown) { if (e !== 'cancel' && e !== 'close') message.value = (e as any)?.response?.data?.code || (e as Error)?.message || '恢复操作未完成，请核对原结果。' }
async function run(body: (start: string) => Promise<void>) { if (busy.value) return; const start = stamp; busy.value = true; message.value = ''; try { await body(start) } catch (e) { if (active(start)) error(e) } finally { if (active(start)) busy.value = false } }
async function applyMeta(value: RestoreMetaOperation | null, record: string, start: string) {
  if (!active(start)) return
  if (!value) { pending.value = record; message.value = '尚未查到恢复元数据原结果；可查询或关闭，不可盲目重发。'; return }
  pending.value = ''; resolved = record
  if (value.state === 'CLOSED') { message.value = '原操作已关闭，迟到申请或审批被阻止。'; return }
  if (value.planId) { planRef.value = value.planId; plan.value = null; const current = await api.plan(value.planId); if (active(start)) plan.value = current }
}
async function applyResult(value: RestoreResult, record: string, start: string) {
  if (!active(start)) return
  result.value = value
  if (!['APPLIED','CLOSED'].includes(value.state)) { pending.value = record; return }
  pending.value = ''; resolved = record
  if (value.state === 'CLOSED') { message.value = '原操作已关闭，迟到执行被阻止。'; return }
  if (value.planId) { planRef.value = value.planId; plan.value = null; const current = await api.plan(value.planId); if (active(start)) plan.value = current }
}
async function original(record: string, action: 'READ' | 'RECOVER' | 'CLOSE', start: string) {
  const id = record.slice(2)
  if (record.startsWith('M:')) await applyMeta(await (action === 'CLOSE' ? api.closeMetadata(id) : api.metadata(id)), record, start)
  else await applyResult(await (action === 'RECOVER' ? api.recover(id) : action === 'CLOSE' ? api.close(id) : api.operation(id)), record, start)
}
async function load() { await run(async start => {
  const value = await api.context(); if (!active(start)) return; context.value = value; storageError.value = ''
  candidateId.value = value.candidates[0]?.id || ''
  let record: string; try { record = stored() } catch (e) { storageError.value = (e as Error).message; return }
  if (record) { pending.value = record; await original(record, 'READ', start) }
}) }
async function mutate(phase: 'M' | 'E', label: string, send: (id: string) => Promise<RestoreMetaOperation | RestoreResult>) {
  if (locked.value || !context.value) return
  await run(async start => {
    await ElMessageBox.confirm(label, '确认受控恢复操作', { type: 'warning' }); if (!active(start)) return
    if (!navigator.locks) throw new Error('当前浏览器不支持安全协调，请更换受支持的浏览器。')
    await navigator.locks.request(key(), async () => {
      if (!active(start)) return
      const latest = stored(); if (latest && latest !== resolved) { pending.value = latest; await original(latest, 'READ', start); return }
      const id = crypto.randomUUID(), record = `${phase}:${id}`
      try { localStorage.setItem(key(), record) } catch { storageError.value = '无法保存恢复原操作号，尚未发送请求。'; return }
      pending.value = record
      if (phase === 'M') await applyMeta(await send(id) as RestoreMetaOperation, record, start)
      else await applyResult(await send(id) as RestoreResult, record, start)
    })
  })
}
async function recover(action: 'READ' | 'RECOVER' | 'CLOSE') {
  const record = pending.value; if (!record) return
  await run(async start => { if (action !== 'READ') await ElMessageBox.confirm(action === 'CLOSE' ? '已提交则读取原回执，否则关闭并阻止迟到执行；不会直接重做处置。' : '只核对准确候选中的原回执并保存已发生结果，不重新执行。', '核对恢复原操作'); if (active(start)) await original(record, action, start) })
}
async function preview() { const id = candidateId.value; await run(async start => { previewResult.value = null; const value = await api.preview(id); if (active(start)) previewResult.value = value }) }
async function prepare() {
  const p = previewResult.value; if (!p || p.scope.candidateId !== candidateId.value || p.blockers.length || !uuid(requestRef.value) || !can('restore-prepare')) return
  const command = { candidateId: p.scope.candidateId, expectedScopeDigest: p.scopeDigest, requestRef: requestRef.value }
  await mutate('M', `申请恢复目标 ${p.scope.target.name}（身份 ${p.scope.target.oid}），备份 ${p.scope.backupSha256}；冻结当前来源与 ${p.scope.tombstoneCount} 个已接墓碑，不覆盖源库。`, operationId => api.request({ operationId, ...command }))
}
async function readPlan() { const id = planRef.value; await run(async start => { plan.value = null; const value = await api.plan(id); if (active(start)) plan.value = value }) }
async function decide(decision: string) {
  if (!plan.value || !uuid(decisionRef.value)) return
  const p = plan.value, command = { expectedVersion: p.version, decision, decisionRef: decisionRef.value }
  await mutate('M', `${decision === 'APPROVE' ? '独立批准' : decision === 'REJECT' ? '拒绝' : '撤销'}恢复计划 ${p.planId}；目标 ${p.scope.target.name}，依据 ${command.decisionRef}。批准不会自动执行。`, operationId => api.decide(p.planId, { operationId, ...command }))
}
async function execute() {
  const p = plan.value; if (!p?.approvalCurrent || p.outcome || !can('restore-execute') || !uuid(confirmationRef.value)) return
  const command = { expectedVersion: p.version, expectedScopeDigest: p.scopeDigest, confirmationRef: confirmationRef.value }
  await mutate('E', `对目标 ${p.scope.target.name}（身份 ${p.scope.target.oid}）重施已批准范围的 ${p.scope.tombstoneCount} 个墓碑，停用相应身份认证。备份 ${p.scope.backupSha256}；来源版本 ${p.scope.sourceVersion}。整体仍有未覆盖Owner，不执行生产切换。`, operationId => api.execute(p.planId, { operationId, ...command }))
}
function identityChanged() {
  const next = identityStamp(); if (next === stamp) return
  stamp = next; resolved = ''; context.value = null; previewResult.value = null; plan.value = null; result.value = null
  for (const field of [candidateId, requestRef, planRef, decisionRef, confirmationRef, pending, message, storageError]) field.value = ''
  busy.value = false
}
window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged)
onMounted(load); onBeforeUnmount(() => { disposed = true; window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })
</script>
<style scoped>
.notice, .lookup, .restore-preview, .restore-plan, .restore-result { margin-top: 18px; }
.lookup { display: flex; gap: 12px; }
select { width: 100%; min-height: 32px; border: 1px solid #cbd5e1; border-radius: 6px; padding: 4px 8px; }
p { line-height: 1.6; overflow-wrap: anywhere; }
</style>
