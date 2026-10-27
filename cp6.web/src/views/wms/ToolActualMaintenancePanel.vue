<template>
  <section v-if="selected" class="maintenance-actual" :aria-busy="busy">
    <h2>核实已发生的维护结果</h2>
    <p>先在维护记录中保存实际检查报告，再登记对应发生并保存观察。独立认定后可正式完成维护；周期恢复还需恢复政策和独立资格。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>有原实际操作尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原维护实际操作</button><button :disabled="busy" @click="recover(true)">结束未提交维护实际操作</button></div>
    <button :disabled="busy || !!pending" @click="load(1)">读取维护实际记录</button>
    <template v-if="context">
      <p>事实认定政策：{{ context.policyKnowledge === 'CURRENT' ? '当前有效' : '等待有效政策' }}。缺少政策仍可保存观察，保存观察不会恢复工具或重置计数。</p>
      <ul><li v-for="m in context.maintenance" :key="m.maintenanceId">维护 {{ m.maintenanceId }}：{{ state(m.status) }} <span v-if="m.status === 'COMPLETED_PASS_PENDING_RESET'">请在维护恢复区核对恢复政策与 MASTER 资格，随后明确申请恢复周期。</span></li></ul>
      <fieldset v-if="can('actual-record')" :disabled="locked">
        <h3>原始维护报告</h3>
        <label>选择报告 <select v-model="reportId" aria-label="原维护报告"><option value="">请选择</option><option v-for="f in context.evidence" :key="f.reference.id" :value="f.reference.id">{{ f.fact.maintenanceId }} · {{ state(String(f.fact.body.outcome)) }} · {{ f.fact.body.occurredAt }}</option></select></label>
        <p v-if="report">{{ report.fact.body.evidence }}</p><pre v-if="report">{{ report.fact.body.checks }}</pre>
        <label>登记说明 <textarea v-model="registerReason" aria-label="维护发生登记说明" maxlength="500" /></label>
        <button :disabled="!report || !registerReason.trim()" @click="register">登记原维护发生</button>
        <h3>保存受限观察</h3>
        <label>选择发生 <select v-model="occurrenceId" aria-label="原维护发生"><option value="">请选择</option><option v-for="f in context.occurrences" :key="f.reference.id" :value="f.reference.id">{{ f.reference.id }} · {{ state(String(f.fact.body.outcome)) }} · {{ f.fact.body.occurredAt }}</option></select></label>
        <label>观察说明 <textarea v-model="observationReason" aria-label="维护观察说明" maxlength="500" /></label>
        <button :disabled="!occurrenceId || !observationReason.trim()" @click="observe">明确记录已维护</button>
      </fieldset>
      <form v-if="can('actual-policy')" aria-label="维护事实认定政策" @submit.prevent="publish"><fieldset :disabled="locked">
        <h3>维护事实认定政策</h3><p>认定人必须独立于原报告和观察登记人。这项政策不授予周期恢复资格。</p>
        <label>政策状态 <select v-model="policyState" aria-label="维护事实政策状态"><option value="ENABLED">启用</option><option value="REVOKED">撤销</option></select></label>
        <label>必需检查项（每行一项）<textarea v-model="requiredChecks" aria-label="维护事实必需检查项" required /></label>
        <label>生效时间（UTC）<input v-model="policyFrom" type="datetime-local" aria-label="维护事实政策生效时间" required /></label>
        <label>失效时间（UTC）<input v-model="policyTo" type="datetime-local" aria-label="维护事实政策失效时间" required /></label>
        <label>政策依据 <textarea v-model="policyReason" aria-label="维护事实政策依据" maxlength="2000" required /></label><button>保存维护事实政策</button>
      </fieldset></form>
      <h3>观察与正式完成</h3><ul><li v-for="o in context.observations" :key="o.original.reference.id"><button :disabled="locked" @click="choose(o.original.reference.id)">维护观察 {{ o.original.reference.id }}</button> {{ state(o.applicationState) }} · {{ state(String(o.original.fact.body.outcome)) }}</li></ul>
      <nav><button :disabled="busy || !!pending || context.page === 1" @click="load(context.page - 1)">维护实际上一页</button><span>第 {{ context.page }} 页</span><button :disabled="busy || !!pending || Math.max(context.evidence.length, context.occurrences.length, context.observations.length, context.maintenance.length) < 20" @click="load(context.page + 1)">维护实际下一页</button></nav>
      <article v-if="observation">
        <p>{{ observation.original.fact.body.reason }} · 实际 {{ observation.original.fact.body.occurredAt }} · 登记 {{ observation.original.fact.recordedAt }}</p>
        <button :disabled="locked" @click="loadApplication">核对维护观察应用条件</button>
        <template v-if="application"><p>原报告：{{ application.evidence.fact.body.evidence }}</p><pre>{{ application.evidence.fact.body.checks }}</pre><p v-if="application.missing.length">仍缺：{{ application.missing.map(missing).join('、') }}</p></template>
        <form v-if="can('actual-recognize') && !observation.application" aria-label="独立认定维护实际结果" @submit.prevent="recognize"><fieldset :disabled="locked || !application">
          <label>认定结论 <select v-model="recognitionOutcome" aria-label="维护事实认定结论"><option value="NEEDS_EVIDENCE">需要补证</option><option value="ACCEPT_FACT">确认原报告中的实际结果</option><option value="REJECT_ASSERTION">不接受主张</option></select></label>
          <label>独立核验说明 <textarea v-model="recognitionReason" aria-label="维护事实认定说明" maxlength="2000" required /></label><button>保存维护事实认定</button>
        </fieldset></form>
        <ul><li v-for="d in observation.decisions" :key="d.reference.id">{{ state(String(d.fact.body.outcome)) }} · {{ d.fact.body.reason }}</li></ul>
        <fieldset v-if="can('actual-apply') && application && !observation.application" :disabled="locked">
          <label>采用原认定 <select v-model="decisionId" aria-label="应用的维护原认定"><option value="">请选择</option><option v-for="d in observation.decisions" :key="d.reference.id" :value="d.reference.id">{{ d.reference.id }} · {{ state(String(d.fact.body.outcome)) }}</option></select></label>
          <button :disabled="!decisionId" @click="apply">应用认定并正式完成维护</button>
        </fieldset>
        <p v-if="observation.application">已关联一次正式完成事实。工具不会因本次应用恢复，周期和计数不变。</p>
      </article>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import type { ActualMaintenanceKind } from '@/api/wms/toolActualUsage'
import { toolActualMaintenanceApi as api, type ActualMaintenanceContext, type MaintenanceApplicationContext } from '@/api/wms/toolActualMaintenance'
import { actualResponse as result, pendingActualUsage, saveActualUsage, resolveActualUsage, type PendingActualUsage } from '@/auth/toolActualUsageRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ scope: EngineeringScope | null; selected: ToolInstanceView | null }>(), emit = defineEmits<{ 'authority-denied': [] }>()
const permission = usePermissionStore(), can = (action: string) => permission.has(`wms-plate-mold:${action}`)
const context = ref<ActualMaintenanceContext | null>(null), application = ref<MaintenanceApplicationContext | null>(null), busy = ref(false), completed = ref(false), pending = ref<PendingActualUsage | null>(null), error = ref(''), notice = ref('')
const reportId = ref(''), occurrenceId = ref(''), registerReason = ref(''), observationReason = ref(''), policyState = ref('ENABLED'), requiredChecks = ref(''), policyFrom = ref(''), policyTo = ref(''), policyReason = ref(''), observationId = ref(''), recognitionOutcome = ref('NEEDS_EVIDENCE'), recognitionReason = ref(''), decisionId = ref('')
const locked = computed(() => busy.value || !!pending.value || completed.value), report = computed(() => context.value?.evidence.find(f => f.reference.id === reportId.value)), observation = computed(() => context.value?.observations.find(o => o.original.reference.id === observationId.value))
function state(s: string) { return ({ PASS: '结果合格', REPAIR_REQUIRED: '需要继续修理', UNSERVICEABLE: '不可用', OPEN: '维护中', COMPLETED_PASS_PENDING_RESET: '已合格完成，等待周期恢复', COMPLETED_PASS_RELEASED: '已完成周期恢复', UNAPPLIED: '尚未应用', WAITING_EVIDENCE: '待补证或应用', APPLIED: '已应用完成事实', ACCEPT_FACT: '实际结果已认定', NEEDS_EVIDENCE: '需要补证', REJECT_ASSERTION: '主张未获接受' } as Record<string, string>)[s] ?? s }
function missing(s: string) { return ({ CURRENT_MAINTENANCE_RECOGNITION_POLICY: '当前维护事实认定政策', ACTUAL_RECOGNITION_DECISION: '有效事实认定' } as Record<string, string>)[s] ?? s }
function utc(v: string) { const d = new Date(`${v}Z`); if (!v || !Number.isFinite(d.valueOf())) throw new Error('请填写有效时间。'); return d.toISOString() }
let live = true, generation = 0
function clear() { context.value = null; application.value = null; pending.value = null; completed.value = false; notice.value = ''; for (const r of [reportId, occurrenceId, registerReason, observationReason, requiredChecks, policyFrom, policyTo, policyReason, observationId, recognitionReason, decisionId]) r.value = ''; policyState.value = 'ENABLED'; recognitionOutcome.value = 'NEEDS_EVIDENCE' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || !props.scope || !props.selected) return
  const stamp = identityStamp(), g = generation, check = () => { requireIdentity(stamp); if (!live || generation !== g) throw new Error('对象或身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (e: any) { try { check(); if ([401, 403].includes(e?.response?.status)) { clear(); emit('authority-denied'); error.value = '当前权限已变化，请重新核对。' } else error.value = e?.response?.status === 412 ? '版本已变化，输入和原引用保留。请查询或结束原操作，再明确重新读取。' : e?.message || '结果尚未确认，请查询原操作。' } catch { /* former identity or object */ } }
  finally { if (live && generation === g) { busy.value = false; if (props.scope) pending.value = pendingActualUsage(props.scope) } }
}
async function load(page: number) { if (pending.value || !props.selected) return; const id = props.selected.reference.id; await run(async check => { const c = result(await api.context(id, page)); check(); if (c.toolId !== id || c.scope.tenantId !== props.scope?.tenantId || c.isUsePermit !== false) throw new Error('来源与当前对象不一致。'); context.value = c; application.value = null; observationId.value = ''; decisionId.value = ''; completed.value = false; notice.value = '' }) }
function choose(id: string) { observationId.value = id; application.value = null; decisionId.value = ''; recognitionReason.value = ''; recognitionOutcome.value = 'NEEDS_EVIDENCE' }
async function loadApplication() { const c = context.value, o = observation.value; if (!c || !o) return; await run(async check => { const a = result(await api.applicationContext(c.toolId, o.original.reference.id)); check(); if (observationId.value !== o.original.reference.id || a.observation.id !== o.original.reference.id || a.observation.digest !== o.original.reference.digest || a.isUsePermit !== false) throw new Error('应用条件与原观察不一致。'); application.value = a }) }
async function write(kind: ActualMaintenanceKind, body: Record<string, unknown>, target: string | null = null) {
  const c = context.value, scope = props.scope; if (!c || !scope || locked.value) return
  const action = ({ M_OCCURRENCE: 'actual-record', M_OBSERVATION: 'actual-record', M_POLICY: 'actual-policy', M_RECOGNITION: 'actual-recognize', M_APPLICATION: 'actual-apply' })[kind]; if (!can(action)) return
  await run(async check => { const saved = await saveActualUsage(scope, c.toolId, kind, check, operationId => api.write(c.toolId, target, kind, c.etag, { ...body, operationId })); check(); completed.value = true; notice.value = `原事实已保存：${state(saved.result!.outcome)}。请重新读取；周期恢复仍须独立办理。` })
}
async function register() { const f = report.value; if (!f || !registerReason.value.trim()) return; await write('M_OCCURRENCE', { maintenanceId: f.fact.maintenanceId, sourceEvidenceSelectionRef: f.reference, reportedOutcome: f.fact.body.outcome, occurredAt: f.fact.body.occurredAt, reason: registerReason.value }) }
async function observe() { const f = context.value?.occurrences.find(f => f.reference.id === occurrenceId.value); if (!f || !observationReason.value.trim()) return; await write('M_OBSERVATION', { occurrenceSelectionRef: f.reference, outcome: f.fact.body.outcome, occurredAt: f.fact.body.occurredAt, reason: observationReason.value, kind: 'MAINTENANCE_RESULT_ACTUAL' }) }
async function publish() { const c = context.value; if (!c || !policyReason.value.trim()) return; try { const checks = requiredChecks.value.split('\n').map(x => x.trim()).filter(Boolean); if (!checks.length || checks.length > 30 || new Set(checks).size !== checks.length) throw new Error('请填写1至30个不同的检查项。'); await write('M_POLICY', { state: policyState.value, site: c.site, requiredChecks: checks, effectiveFrom: utc(policyFrom.value), effectiveTo: utc(policyTo.value), reason: policyReason.value }) } catch (e: any) { error.value = e.message } }
async function recognize() { const o = observation.value, a = application.value; if (!o || !a || !recognitionReason.value.trim()) return; await write('M_RECOGNITION', { expectedObservationDigest: o.original.reference.digest, outcome: recognitionOutcome.value, evidenceRef: a.evidence.reference, reason: recognitionReason.value }, o.original.reference.id) }
async function apply() { const o = observation.value, a = application.value, d = o?.decisions.find(f => f.reference.id === decisionId.value); if (!o || !a || !d) return; await write('M_APPLICATION', { expectedObservationApplicationRevision: a.revision, applicationContextRef: a.contextRef, decisionRef: d.reference }, o.original.reference.id) }
async function recover(close: boolean) { const scope = props.scope; if (!scope) return; await run(async check => { const value = await resolveActualUsage(scope, close, check); check(); completed.value = value.state === 'COMMITTED'; notice.value = value.state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : value.state === 'CLOSED' ? '未提交操作已结束，输入保留。' : '原实际结果已恢复，请重新读取。' }) }
function changed() { generation++; clear(); busy.value = false; error.value = ''; if (props.scope) pending.value = pendingActualUsage(props.scope) }
watch(() => [props.scope?.tenantId, props.scope?.actorId, props.selected?.reference.id, props.selected?.reference.version], changed, { immediate: true })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed) })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.maintenance-actual{margin-top:20px;padding:22px;border:1px solid #d7e3e7;border-radius:12px;background:var(--surface,#fff)}label{display:block;margin:12px 0}input,select,textarea,button{font:inherit;color:inherit;padding:8px;border:1px solid #bdcdd3;border-radius:6px;background:#fff}textarea{display:block;width:min(100%,650px);min-height:65px}button{margin:5px;cursor:pointer}button:disabled,fieldset:disabled{opacity:.65}fieldset{border:0;padding:0}article,form{border-top:1px solid #d7e3e7;padding-top:15px}p,pre{overflow-wrap:anywhere;white-space:pre-wrap}[role=alert]{color:#a02828}input:focus,textarea:focus,select:focus,button:focus-visible{outline:2px solid #14969e;outline-offset:2px}
</style>
