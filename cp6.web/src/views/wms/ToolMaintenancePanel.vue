<template>
  <section v-if="selected" class="maintenance" :aria-busy="busy">
    <h2>实物维护</h2><p>维修报告、完成记录、恢复可用分别处理。维护恢复仅重开维护周期，生产使用仍须工程用途资格。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>维护操作结果尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原维护操作</button><button :disabled="busy" @click="recover(true)">结束未提交维护操作</button></div>
    <button :disabled="busy || !!pending" @click="load(1)">读取当前维护</button>
    <template v-if="context">
      <p>当前周期 {{ context.cycle.number ?? '未知' }} · 本周期次数 {{ context.cycle.shots ?? '待核对' }} · 终身次数 {{ context.cycle.lifetimeShots ?? '历史未知' }}</p>
      <p v-if="context.cycle.knowledge === 'RECONCILIATION_REQUIRED'">原始生命周期数据发生未归属变更，须核对历史后恢复。</p>
      <p>维护政策：{{ context.policyKnowledge === 'CURRENT' ? '当前有效' : '等待有效政策' }}；当前资格：{{ context.currentEligibility === 'RESTRICTED' ? '受限' : '生产使用资格待核实' }}。</p>
      <p v-if="policy">政策检查项：{{ policy.requiredChecks.join('、') }}。{{ policy.independentInspector ? '完成确认须与报告登记人不同。' : '本政策未要求独立报告登记人。' }}</p>
      <p v-if="policy">政策期间 {{ policy.effectiveFrom }} 至 {{ policy.effectiveTo }}；依据：{{ policy.reason }}。</p>
      <form v-if="can('maintenance-policy')" aria-label="发布维护政策" @submit.prevent="publish"><fieldset :disabled="locked">
        <h3>维护政策</h3><label>政策状态 <select v-model="policyState" aria-label="政策状态"><option value="ENABLED">发布新政策</option><option value="REVOKED">撤销当前政策</option></select></label>
        <label>必需检查项（每行一项）<textarea v-model="requiredChecks" aria-label="维护政策检查项" required /></label>
        <label><input v-model="allowReset" type="checkbox" aria-label="政策允许周期恢复" />允许合格维护恢复周期</label>
        <label><input v-model="allowRepair" type="checkbox" aria-label="政策允许不合格后维修" />允许不能使用结论后的新维修评估</label>
        <label><input v-model="independent" type="checkbox" />要求独立完成确认人</label>
        <label>生效时间（UTC）<input v-model="policyFrom" type="datetime-local" aria-label="政策生效时间" required /></label>
        <label>失效时间（UTC）<input v-model="policyTo" type="datetime-local" aria-label="政策失效时间" required /></label>
        <label>政策依据 <textarea v-model="policyReason" aria-label="政策依据" maxlength="1000" required /></label><button>保存维护政策</button>
      </fieldset></form>
      <form v-if="can('maintenance')" aria-label="开始实物维护" @submit.prevent="start"><fieldset :disabled="locked">
        <h3>开始维护</h3><label>原因 <select v-model="reasonCode" aria-label="维护原因"><option value="PLANNED">计划维护</option><option value="LIFE_REACHED">达到寿命次数</option><option value="DAMAGE">损伤</option></select></label>
        <label>说明 <textarea v-model="reason" aria-label="维护说明" maxlength="500" required /></label>
        <label>计划完成日期 <input v-model="planned" type="date" aria-label="计划完成日期" /></label>
        <label>返修来源（返修时明确选择）<select v-model="predecessor" aria-label="返修来源"><option value="">首次维护或已恢复后的新维护</option><option v-for="x in context.maintenance.filter(x => ['REPAIR_REQUIRED', 'UNSERVICEABLE'].includes(x.status))" :key="x.maintenanceId" :value="x.maintenanceId">{{ x.maintenanceId }} · {{ state(x.status) }}</option></select></label>
        <p v-if="predecessorEntry?.status === 'UNSERVICEABLE'">将引用当前新政策申请再次维修，原不能使用结论保留。</p><button>开始维护</button>
      </fieldset></form>
      <h3>维护历史与处理</h3>
      <ul><li v-for="x in context.maintenance" :key="x.maintenanceId"><button :disabled="busy || !!pending" @click="choose(x.maintenanceId)">维护 {{ x.maintenanceId }}</button> {{ state(x.status) }} · {{ x.start.fact.recordedAt }}</li></ul>
      <nav><button :disabled="busy || !!pending || context.page === 1" @click="load(context.page - 1)">维护上一页</button><span>第 {{ context.page }} 页</span><button :disabled="busy || !!pending || context.maintenance.length < 20" @click="load(context.page + 1)">维护下一页</button></nav>
      <article v-if="entry">
        <p>完成记录：{{ entry.completion ? state(entry.status === 'COMPLETED_PASS_RELEASED' ? 'COMPLETED_PASS_PENDING_RESET' : entry.status) : '尚未形成' }}；恢复可用：{{ entry.reset ? '已保存独立恢复回执' : '尚未应用' }}。</p>
        <p v-if="entry.completion">完成事实 {{ entry.completion.reference.id }}；实际完成时间 {{ entry.completion.fact.body.occurredAt }}。</p>
        <ul><li v-for="x in entry.reports" :key="x.reference.id">原维修报告 {{ x.reference.id }} · {{ reportBody(x).outcome }} · {{ reportBody(x).occurredAt }} · {{ reportBody(x).evidence }}</li></ul>
        <form v-if="entry.status === 'OPEN' && can('maintenance-record')" aria-label="登记维修报告" @submit.prevent="report"><fieldset :disabled="locked">
          <h4>登记原始维修报告</h4><p>无有效政策仍可保存报告；报告不会清零次数或自动形成完成事实。</p>
          <label>报告结果 <select v-model="outcome" aria-label="维修报告结果"><option value="PASS">合格</option><option value="REPAIR_REQUIRED">需要返修</option><option value="UNSERVICEABLE">不能使用</option></select></label>
          <label>实际完成时间（UTC）<input v-model="occurred" type="datetime-local" aria-label="维修实际完成时间" required /></label>
          <label>检查记录（每行：检查项 | PASS或FAIL | 实测依据）<textarea v-model="checks" aria-label="维修检查记录" required /></label>
          <label>维修依据 <textarea v-model="evidence" aria-label="维修依据" maxlength="2000" required /></label>
          <label><input v-model="reportUnderPolicy" type="checkbox" :disabled="context.policyKnowledge !== 'CURRENT'" />按当前政策形成的维修报告</label><button>保存原维修报告</button>
        </fieldset></form>
        <div v-if="entry.status === 'OPEN' && can('maintenance')"><fieldset :disabled="locked || context.policyKnowledge !== 'CURRENT'">
          <label>用于完成确认的原报告 <select v-model="reportId" aria-label="完成确认报告"><option value="">请选择</option><option v-for="x in entry.reports" :key="x.reference.id" :value="x.reference.id">{{ x.reference.id }} · {{ reportBody(x).outcome }}</option></select></label>
          <button :disabled="!reportId" @click="complete">形成维护完成事实</button>
        </fieldset></div>
        <template v-if="entry.status === 'COMPLETED_PASS_PENDING_RESET'">
          <p>维护已经完成，等待独立恢复。缺有效政策或 MASTER 当前资格时保持维护受限状态。</p>
          <p v-for="q in qualifications" :key="q.reference.id">MASTER 决定 {{ q.reference.id }}：{{ assessmentBody(q).decision }}；依据：{{ assessmentBody(q).evidence }}。原实物版本 {{ q.fact.nativeAfter.version }}。</p>
          <form v-if="canAssess" aria-label="评估维护恢复资格" @submit.prevent="assess"><fieldset :disabled="locked || context.policyKnowledge !== 'CURRENT'">
            <h4>MASTER 维护恢复资格</h4><label>评估结论 <select v-model="decision" aria-label="维护恢复评估结论"><option value="">请选择</option><option value="ELIGIBLE">符合维护恢复条件</option><option value="RESTRICTED">仍受限制</option></select></label>
            <label>资格有效至（UTC）<input v-model="validUntil" type="datetime-local" aria-label="恢复资格有效至" required /></label>
            <label>生命周期评估依据 <textarea v-model="assessmentEvidence" aria-label="恢复资格依据" maxlength="2000" required /></label><button :disabled="!decision">保存 MASTER 资格决定</button>
          </fieldset></form>
          <fieldset v-if="can('maintenance-restore')" :disabled="locked || context.policyKnowledge !== 'CURRENT'">
            <label>用于恢复的 MASTER 原决定 <select v-model="qualificationId" aria-label="维护恢复资格"><option value="">请选择</option><option v-for="x in qualifications" :key="x.reference.id" :value="x.reference.id">{{ x.reference.id }} · {{ assessmentBody(x).decision }} · 至 {{ assessmentBody(x).validUntil }}</option></select></label>
            <button :disabled="!qualificationId || !policy?.allowReset || !context.cycle.number" @click="reset">应用独立周期恢复</button>
          </fieldset>
        </template>
      </article>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import { toolMaintenanceApi as api, type MaintenanceContext, type MaintenanceKind, type MaintenanceFact, type MaintenancePolicy, type MaintenanceReport, type MaintenanceAssessment } from '@/api/wms/toolMaintenance'
import { maintenanceResponse as result, pendingToolMaintenance, saveToolMaintenance, resolveToolMaintenance, type PendingToolMaintenance } from '@/auth/toolMaintenanceRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ scope: EngineeringScope | null; selected: ToolInstanceView | null }>(), emit = defineEmits<{ 'authority-denied': [] }>()
const permission = usePermissionStore(), can = (action: string) => permission.has(`wms-plate-mold:${action}`), canAssess = computed(() => permission.has('space-site:tool-maintenance-assess'))
const context = ref<MaintenanceContext | null>(null), busy = ref(false), completed = ref(false), pending = ref<PendingToolMaintenance | null>(null), error = ref(''), notice = ref('')
const policyState = ref('ENABLED'), requiredChecks = ref(''), allowReset = ref(false), allowRepair = ref(false), independent = ref(true), policyFrom = ref(''), policyTo = ref(''), policyReason = ref('')
const reasonCode = ref('PLANNED'), reason = ref(''), planned = ref(''), predecessor = ref(''), selectedMaintenance = ref('')
const outcome = ref('PASS'), occurred = ref(''), checks = ref(''), evidence = ref(''), reportUnderPolicy = ref(false), reportId = ref('')
const decision = ref(''), validUntil = ref(''), assessmentEvidence = ref(''), qualificationId = ref('')
const locked = computed(() => busy.value || !!pending.value || completed.value), entry = computed(() => context.value?.maintenance.find(x => x.maintenanceId === selectedMaintenance.value))
const predecessorEntry = computed(() => context.value?.maintenance.find(x => x.maintenanceId === predecessor.value)), policy = computed(() => context.value?.policy?.fact.body as unknown as MaintenancePolicy | undefined)
const qualifications = computed(() => context.value?.assessments.filter(x => x.fact.maintenanceId === selectedMaintenance.value) ?? [])
const reportBody = (f: MaintenanceFact) => f.fact.body as unknown as MaintenanceReport, assessmentBody = (f: MaintenanceFact) => f.fact.body as unknown as MaintenanceAssessment
function state(s: string) { return ({ OPEN: '维护中', COMPLETED_PASS_PENDING_RESET: '合格完成，等待恢复', COMPLETED_PASS_RELEASED: '合格完成且周期已恢复', REPAIR_REQUIRED: '需要返修', UNSERVICEABLE: '不能使用' } as Record<string, string>)[s] ?? s }
function utc(value: string) { if (!value) throw new Error('请填写实际时间。'); const date = new Date(`${value}Z`); if (!Number.isFinite(date.valueOf())) throw new Error('时间格式无效。'); return date.toISOString() }
let live = true, generation = 0
function clear() { context.value = null; pending.value = null; completed.value = false; notice.value = ''; requiredChecks.value = ''; policyReason.value = ''; policyFrom.value = ''; policyTo.value = ''; reason.value = ''; planned.value = ''; predecessor.value = ''; selectedMaintenance.value = ''; checks.value = ''; evidence.value = ''; occurred.value = ''; reportId.value = ''; qualificationId.value = ''; assessmentEvidence.value = ''; validUntil.value = ''; decision.value = ''; allowReset.value = false; allowRepair.value = false; reportUnderPolicy.value = false; independent.value = true; policyState.value = 'ENABLED'; reasonCode.value = 'PLANNED'; outcome.value = 'PASS' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || !props.scope || !props.selected) return
  const stamp = identityStamp(), g = generation, check = () => { requireIdentity(stamp); if (!live || generation !== g) throw new Error('对象或身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (e: any) { try { check(); if ([401, 403].includes(e?.response?.status)) { clear(); emit('authority-denied'); error.value = '当前权限已变化，请重新核对。' }
    else error.value = e?.response?.status === 412 ? '版本已变化，输入及原引用已保留。请查询或结束原操作，再明确重新读取。' : e?.response?.status === 409 ? '当前维护状态不允许此动作，请核对原操作和维护历史。' : e?.message || '维护操作未确认，请查询原操作。' } catch { /* late response */ } }
  finally { if (live && generation === g) { busy.value = false; if (props.scope) pending.value = pendingToolMaintenance(props.scope) } }
}
async function load(page: number) { if (pending.value) return; const id = props.selected?.reference.id; if (!id) return; await run(async check => { const value = result(await api.context(id, page)); check(); if (value.tenantId !== props.scope?.tenantId || value.toolId !== id || value.native.id !== id || value.notExecutionPermit !== true) throw new Error('维护来源与当前对象不一致。'); context.value = value; completed.value = false; selectedMaintenance.value = ''; reportId.value = ''; qualificationId.value = ''; notice.value = '' }) }
function choose(id: string) { selectedMaintenance.value = id; reportId.value = ''; qualificationId.value = ''; checks.value = ''; evidence.value = ''; occurred.value = ''; decision.value = ''; assessmentEvidence.value = ''; validUntil.value = '' }
async function write(kind: MaintenanceKind, body: Record<string, unknown>, maintenanceId: string | null = null) {
  const scope = props.scope, c = context.value; if (!scope || !c || locked.value) return
  const action = ({ POLICY: 'maintenance-policy', START: 'maintenance', REPORT: 'maintenance-record', COMPLETE: 'maintenance', RESET: 'maintenance-restore' } as Record<string, string>)[kind]
  if (kind === 'ASSESSMENT' ? !canAssess.value : !action || !can(action)) return
  await run(async check => { const saved = await saveToolMaintenance(scope, c.toolId, kind, maintenanceId, check, operationId => api.write(c.toolId, maintenanceId, kind, c.etag, { ...body, operationId })); check(); completed.value = true; notice.value = kind === 'COMPLETE' ? '维护完成事实已保存；恢复可用需另行申请。' : kind === 'RESET' ? '周期恢复回执已保存。生产使用资格仍需另行核对。' : `维护操作已保存：${state(saved.result!.outcome)}。请重新读取当前维护。` })
}
async function publish() { if (!context.value || !policyReason.value.trim() || !requiredChecks.value.trim()) return; try { await write('POLICY', { state: policyState.value, site: context.value.site, requiredChecks: requiredChecks.value.split('\n').filter(x => x.length), allowReset: allowReset.value, allowUnserviceableRepair: allowRepair.value, independentInspector: independent.value, effectiveFrom: utc(policyFrom.value), effectiveTo: utc(policyTo.value), reason: policyReason.value }) } catch (e: any) { error.value = e.message } }
async function start() { if (!reason.value.trim()) return; await write('START', { reasonCode: reasonCode.value, reason: reason.value, plannedCompletionDate: planned.value || null, predecessorMaintenanceId: predecessor.value || null, repairPolicy: predecessorEntry.value?.status === 'UNSERVICEABLE' ? context.value?.policy?.reference : null }) }
async function report() {
  if (!entry.value || !evidence.value.trim()) return
  try { const values = checks.value.split('\n').filter(Boolean).map(line => { const [code, result, ...parts] = line.split('|'); if (!code || !['PASS', 'FAIL'].includes(result?.trim() ?? '') || !parts.join('|').trim()) throw new Error('请按检查项 | PASS或FAIL | 实测依据填写。'); return { code: code.trim(), result: result!.trim(), evidence: parts.join('|').trim() } })
    await write('REPORT', { outcome: outcome.value, occurredAt: utc(occurred.value), checks: values, evidence: evidence.value, policy: reportUnderPolicy.value ? context.value?.policy?.reference : null }, entry.value.maintenanceId)
  } catch (e: any) { error.value = e.message }
}
async function complete() { const e = entry.value, report = e?.reports.find(x => x.reference.id === reportId.value); if (!e || !report || !context.value?.policy || context.value.policyKnowledge !== 'CURRENT') return; await write('COMPLETE', { evidenceRef: report.reference, policy: context.value.policy.reference }, e.maintenanceId) }
async function assess() { const e = entry.value; if (!e?.completion || !context.value?.policy || !decision.value || !assessmentEvidence.value.trim()) return; try { await write('ASSESSMENT', { completionRef: e.completion.reference, policy: context.value.policy.reference, decision: decision.value, validUntil: utc(validUntil.value), evidence: assessmentEvidence.value }, e.maintenanceId) } catch (x: any) { error.value = x.message } }
async function reset() { const e = entry.value, c = context.value, qualification = qualifications.value.find(x => x.reference.id === qualificationId.value); if (!e?.completion || !c?.policy || !qualification || !c.cycle.number || !policy.value?.allowReset) return; await write('RESET', { completionRef: e.completion.reference, resetPolicySelectionRef: c.policy.reference, qualificationRef: qualification.reference, expectedCycleRef: c.cycle.reference }, e.maintenanceId) }
async function recover(close: boolean) { const scope = props.scope; if (!scope) return; await run(async check => { const value = await resolveToolMaintenance(scope, close, check); check(); completed.value = value.state === 'COMMITTED'; notice.value = value.state === 'UNKNOWN' ? '维护原结果仍未知，请继续查询。' : value.state === 'CLOSED' ? '未提交操作已结束，输入保留。' : '维护原结果已恢复，请重新读取当前维护。' }) }
function changed() { generation++; clear(); busy.value = false; error.value = ''; if (props.scope) pending.value = pendingToolMaintenance(props.scope) }
watch(() => [props.scope?.tenantId, props.scope?.actorId, props.selected?.reference.id, props.selected?.reference.version], changed, { immediate: true })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed) })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.maintenance{margin-top:20px;padding:22px;background:var(--surface,#fff);border:1px solid #d7e3e7;border-radius:12px}label{display:block;margin:12px 0}input,select,textarea,button{font:inherit;color:inherit;padding:8px;border:1px solid #bdcdd3;border-radius:6px;background:#fff}textarea{display:block;width:min(100%,650px);min-height:65px}button{margin:5px;cursor:pointer}button:disabled,fieldset:disabled{opacity:.65}fieldset{border:0;padding:0}article,form{border-top:1px solid #d7e3e7;padding-top:15px}p{overflow-wrap:anywhere}[role=alert]{color:#a02828}input:focus,textarea:focus,select:focus,button:focus-visible{outline:2px solid #14969e;outline-offset:2px}
</style>
