<template>
  <section v-if="selected" class="actual-usage" :aria-busy="busy">
    <h2>记录已发生的使用</h2><p>这里记录现场已经发生的事实。保存原件、主管认定和计数入账分别处理；补录不追授当时的使用许可。</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
    <div v-if="pending"><p>原实际使用操作尚未确认。</p><button :disabled="busy" @click="recover(false)">查询原实际使用操作</button><button :disabled="busy" @click="recover(true)">结束未提交实际使用操作</button></div>
    <button :disabled="busy || !!pending" @click="load(1)">读取实际使用记录</button>
    <template v-if="context">
      <p>当前周期 {{ currentCycle?.number ?? '未知' }} · 次数 {{ currentCycle?.shots ?? '待核对' }} · 终身次数 {{ context.lifetimeShots ?? '历史未知' }}</p>
      <p v-if="context.knowledge === 'ACTUAL_RECONCILIATION_REQUIRED'">存在实际发生的受限记录。完成计数入账后，生产使用仍须独立核对当前资格。</p>
      <p v-else-if="context.knowledge === 'RECONCILIATION_REQUIRED'">原生周期发生未归属变化，须先核对历史。</p>
      <p>认定政策：{{ context.policyKnowledge === 'CURRENT' ? '当前有效' : '等待有效政策' }}。报告无政策也可保存，计数不会因此增加。</p>
      <form v-if="can('actual-record')" aria-label="保存实际使用原件" @submit.prevent="saveEvidence"><fieldset :disabled="locked">
        <h3>现场原件</h3><p>填写现场原凭据的单据、行和发生序号。同一事件补充说明时请选择原修订；不得另造发生序号重复入账。</p>
        <label>原单据标识 <input v-model="documentKey" aria-label="原使用单据" maxlength="100" required /></label>
        <label>原行标识 <input v-model="lineKey" aria-label="原使用行" maxlength="80" required /></label>
        <label>发生序号 <input v-model="ordinal" aria-label="原使用发生序号" maxlength="80" required /></label>
        <label>实际次数 <input v-model="shots" aria-label="实际使用次数" inputmode="numeric" required /></label>
        <label>实际发生时间（UTC）<input v-model="occurred" aria-label="实际使用时间" type="datetime-local" step="0.001" required /></label>
        <label>原始依据 <textarea v-model="evidenceText" aria-label="实际使用依据" maxlength="4000" required /></label>
        <label>补充原件（首次保存留空）<select v-model="previousId" aria-label="补充使用原件"><option value="">首次保存</option><option v-for="f in context.evidence" :key="f.reference.id" :value="f.reference.id">{{ evidenceLabel(f) }}</option></select></label>
        <button>保存现场原件</button>
      </fieldset></form>
      <div v-if="can('actual-record')"><fieldset :disabled="locked">
        <h3>登记一次真实发生</h3><label>选择原件 <select v-model="evidenceId" aria-label="发生的原使用凭据"><option value="">请选择</option><option v-for="f in context.evidence" :key="f.reference.id" :value="f.reference.id">{{ evidenceLabel(f) }}</option></select></label>
        <label>登记说明 <textarea v-model="registerReason" aria-label="发生登记说明" maxlength="500" /></label><button :disabled="!evidenceId || !registerReason.trim()" @click="register">登记原件对应的发生</button>
        <h3>保存受限观察</h3><label>选择真实发生 <select v-model="occurrenceId" aria-label="选择实际发生"><option value="">请选择</option><option v-for="f in context.occurrences" :key="f.reference.id" :value="f.reference.id">{{ f.reference.id }} · {{ f.fact.body.shots }}次 · {{ f.fact.body.occurredAt }}</option></select></label>
        <label>现场说明 <textarea v-model="observationReason" aria-label="受限观察说明" maxlength="500" /></label><button :disabled="!occurrenceId || !observationReason.trim()" @click="observe">明确记录已发生</button>
      </fieldset></div>
      <form v-if="can('actual-policy')" aria-label="发布实际认定政策" @submit.prevent="publish"><fieldset :disabled="locked">
        <h3>认定政策</h3><label>状态 <select v-model="policyState" aria-label="实际认定政策状态"><option value="ENABLED">启用新政策</option><option value="REVOKED">撤销</option></select></label>
        <label><input v-model="allowLocal" type="checkbox" aria-label="允许本域原件认定" />允许独立人员核实本域现场原件</label>
        <p>认定人必须与观察登记人及所采用原件的登记人不同。</p>
        <label>生效时间（UTC）<input v-model="policyFrom" aria-label="认定政策生效时间" type="datetime-local" required /></label>
        <label>失效时间（UTC）<input v-model="policyTo" aria-label="认定政策失效时间" type="datetime-local" required /></label>
        <label>政策依据 <textarea v-model="policyReason" aria-label="认定政策依据" maxlength="2000" required /></label><button>保存认定政策</button>
      </fieldset></form>
      <h3>观察与计数应用</h3>
      <ul><li v-for="o in context.observations" :key="o.original.reference.id"><button :disabled="busy || !!pending" @click="choose(o.original.reference.id)">观察 {{ o.original.reference.id }}</button> {{ state(o.applicationState) }} · {{ o.original.fact.body.shots }}次 · {{ o.original.fact.body.occurredAt }}</li></ul>
      <nav><button :disabled="busy || !!pending || context.page === 1" @click="load(context.page - 1)">实际记录上一页</button><span>第 {{ context.page }} 页</span><button :disabled="busy || !!pending || Math.max(context.evidence.length, context.occurrences.length, context.observations.length, context.uses.length) < 20" @click="load(context.page + 1)">实际记录下一页</button></nav>
      <article v-if="observation">
        <p>原观察 {{ observation.original.reference.id }}：{{ observation.original.fact.body.reason }}；记录时间 {{ observation.original.fact.recordedAt }}。</p>
        <p>计数应用：{{ state(observation.applicationState) }}。{{ observation.application ? '已保存一次性应用回执；当前使用资格仍受限。' : '尚未改变次数。' }}</p>
        <button :disabled="locked" @click="loadApplication">核对原观察应用条件</button>
        <p v-if="application?.missing.length">仍缺：{{ application.missing.map(missingLabel).join('、') }}</p>
        <form v-if="can('actual-recognize') && !observation.application" aria-label="独立认定实际使用" @submit.prevent="recognize"><fieldset :disabled="locked">
          <label>认定结论 <select v-model="recognitionOutcome" aria-label="实际认定结论"><option value="NEEDS_EVIDENCE">需要补证</option><option value="ACCEPT_FACT">确认实际发生及周期归属</option><option value="REJECT_ASSERTION">不接受本次主张</option></select></label>
          <label>实际归属周期 <select v-model="cycleId" aria-label="认定的原周期"><option value="">尚不明确</option><option v-for="c in application?.cycles ?? context.cycles" :key="c.reference.id" :value="c.reference.id">周期 {{ c.number ?? '未知' }} · {{ c.current ? '当前' : '已关闭' }} · {{ c.shots }}次 · {{ c.startedAt ?? '起点未知' }} 至 {{ c.closedAt ?? '当前' }}</option></select></label>
          <label>采用的原凭据 <select v-model="recognitionEvidenceId" aria-label="认定的使用凭据"><option value="">尚无补证</option><option v-for="f in application?.evidence ?? context.evidence" :key="f.reference.id" :value="f.reference.id">{{ evidenceLabel(f) }}</option></select></label>
          <label>独立核验说明 <textarea v-model="recognitionReason" aria-label="独立认定说明" maxlength="2000" required /></label><button>保存独立认定</button>
        </fieldset></form>
        <ul><li v-for="d in observation.decisions" :key="d.reference.id">认定 {{ d.reference.id }}：{{ state(String(d.fact.body.outcome)) }} · {{ d.fact.body.reason }}；发生时资格 {{ d.fact.body.qualificationAtOccurrence === 'VIOLATION' ? '违反当时状态限制' : '未获证明' }}。</li></ul>
        <fieldset v-if="can('actual-apply') && !observation.application" :disabled="locked || !application">
          <label>用于应用的原决定 <select v-model="decisionId" aria-label="应用的原认定"><option value="">请选择</option><option v-for="d in observation.decisions" :key="d.reference.id" :value="d.reference.id">{{ d.reference.id }} · {{ state(String(d.fact.body.outcome)) }}</option></select></label>
          <button :disabled="!decisionId" @click="apply">应用原认定的次数</button>
        </fieldset>
      </article>
      <h3>已入账使用</h3><ul><li v-for="u in context.uses" :key="u.reference.id">{{ u.fact.body.shots }}次 · 实际 {{ u.fact.body.occurredAt }} · 入账 {{ u.fact.recordedAt }} · 受限实际发生</li></ul>
    </template>
  </section>
</template>
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { EngineeringScope } from '@/api/plm/engineeringProducts'
import type { ToolInstanceView } from '@/api/wms/toolInstances'
import { toolActualUsageApi as api, type ActualContext, type ActualFact, type ActualApplicationContext, type ActualCommandKind } from '@/api/wms/toolActualUsage'
import { actualResponse as result, pendingActualUsage, saveActualUsage, resolveActualUsage, type PendingActualUsage } from '@/auth/toolActualUsageRecovery'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { usePermissionStore } from '@/stores/permission'
const props = defineProps<{ scope: EngineeringScope | null; selected: ToolInstanceView | null }>(), emit = defineEmits<{ 'authority-denied': [] }>()
const permission = usePermissionStore(), can = (action: string) => permission.has(`wms-plate-mold:${action}`)
const context = ref<ActualContext | null>(null), application = ref<ActualApplicationContext | null>(null), busy = ref(false), completed = ref(false), pending = ref<PendingActualUsage | null>(null), error = ref(''), notice = ref('')
const documentKey = ref(''), lineKey = ref(''), ordinal = ref(''), shots = ref(''), occurred = ref(''), evidenceText = ref(''), previousId = ref(''), evidenceId = ref(''), occurrenceId = ref(''), registerReason = ref(''), observationReason = ref('')
const policyState = ref('ENABLED'), allowLocal = ref(false), policyFrom = ref(''), policyTo = ref(''), policyReason = ref('')
const observationId = ref(''), recognitionOutcome = ref('NEEDS_EVIDENCE'), cycleId = ref(''), recognitionEvidenceId = ref(''), recognitionReason = ref(''), decisionId = ref('')
const locked = computed(() => busy.value || !!pending.value || completed.value), currentCycle = computed(() => context.value?.cycles.find(c => c.current)), observation = computed(() => context.value?.observations.find(o => o.original.reference.id === observationId.value))
function evidenceLabel(f: ActualFact) { return `${f.fact.body.documentKey} / ${f.fact.body.lineKey} / ${f.fact.body.eventOrdinal} · 修订 ${f.reference.version} · ${f.fact.body.shots}次` }
function state(s: string) { return ({ UNAPPLIED: '尚未应用', WAITING_EVIDENCE: '等待补证或应用', APPLIED: '已应用', ACCEPT_FACT: '事实已认定', NEEDS_EVIDENCE: '需要补证', REJECT_ASSERTION: '本次主张未获接受' } as Record<string, string>)[s] ?? s }
function missingLabel(s: string) { return ({ CURRENT_RECOGNITION_POLICY: '当前认定政策', NATIVE_CYCLE_RECONCILIATION: '原生周期核对', KNOWN_CYCLE: '明确周期', ACTUAL_RECOGNITION_DECISION: '合法事实认定' } as Record<string, string>)[s] ?? s }
function utc(v: string) { if (!v) throw new Error('请填写实际时间。'); const d = new Date(`${v}Z`); if (!Number.isFinite(d.valueOf())) throw new Error('时间格式无效。'); return d.toISOString() }
let live = true, generation = 0
function clear() { context.value = null; application.value = null; pending.value = null; completed.value = false; notice.value = ''; for (const r of [documentKey, lineKey, ordinal, shots, occurred, evidenceText, previousId, evidenceId, occurrenceId, registerReason, observationReason, policyFrom, policyTo, policyReason, observationId, cycleId, recognitionEvidenceId, recognitionReason, decisionId]) r.value = ''; allowLocal.value = false; policyState.value = 'ENABLED'; recognitionOutcome.value = 'NEEDS_EVIDENCE' }
async function run(action: (check: () => void) => Promise<void>) {
  if (busy.value || !props.scope || !props.selected) return
  const stamp = identityStamp(), g = generation, check = () => { requireIdentity(stamp); if (!live || generation !== g) throw new Error('对象或身份已变化。') }
  busy.value = true; error.value = ''
  try { await action(check); check() } catch (e: any) { try { check(); if ([401, 403].includes(e?.response?.status)) { clear(); emit('authority-denied'); error.value = '当前权限已变化，请重新核对。' }
    else error.value = e?.response?.status === 412 ? '版本已变化，输入及原引用已保留。先查询或结束原操作，再明确重新读取。' : e?.response?.status === 503 ? '当前权威或运行环境不可用，请保留原操作并核对服务配置。' : e?.message || '结果尚未确认，请查询原操作。' } catch { /* former identity or tool */ } }
  finally { if (live && generation === g) { busy.value = false; if (props.scope) pending.value = pendingActualUsage(props.scope) } }
}
async function load(page: number) { if (pending.value || !props.selected) return; const id = props.selected.reference.id; await run(async check => { const c = result(await api.context(id, page)); check(); if (c.tenantId !== props.scope?.tenantId || c.toolId !== id || c.native.id !== id || c.isUsePermit !== false) throw new Error('实际使用来源与当前对象不一致。'); context.value = c; application.value = null; observationId.value = ''; decisionId.value = ''; completed.value = false; notice.value = '' }) }
function choose(id: string) { observationId.value = id; application.value = null; recognitionReason.value = ''; recognitionEvidenceId.value = ''; cycleId.value = ''; decisionId.value = ''; recognitionOutcome.value = 'NEEDS_EVIDENCE' }
async function loadApplication() { const c = context.value, o = observation.value; if (!c || !o) return; await run(async check => { const v = result(await api.applicationContext(c.toolId, o.original.reference.id)); check(); if (observationId.value !== o.original.reference.id || v.observation.id !== o.original.reference.id || v.observation.digest !== o.original.reference.digest || v.isUsePermit !== false) throw new Error('应用条件与原观察不一致。'); application.value = v }) }
async function write(kind: ActualCommandKind, body: Record<string, unknown>, target: string | null = null) {
  const scope = props.scope, c = context.value; if (!scope || !c || locked.value) return
  const action = ({ EVIDENCE: 'actual-record', OCCURRENCE: 'actual-record', OBSERVATION: 'actual-record', POLICY: 'actual-policy', RECOGNITION: 'actual-recognize', APPLICATION: 'actual-apply' })[kind]
  if (!can(action)) return
  await run(async check => { const saved = await saveActualUsage(scope, c.toolId, kind, check, operationId => api.write(c.toolId, target, kind, c.etag, { ...body, operationId })); check(); completed.value = true; notice.value = kind === 'APPLICATION' ? '实际次数已入账，当前使用仍受限。请重新读取。' : `原事实已保存：${state(saved.result!.outcome)}。请重新读取。` })
}
async function saveEvidence() { if (!context.value || !documentKey.value.trim() || !lineKey.value.trim() || !ordinal.value.trim() || !evidenceText.value.trim()) return; try { if (!/^[1-9][0-9]*$/.test(shots.value) || Number(shots.value) > 2147483647) throw new Error('次数须为不超过2147483647的正整数。'); await write('EVIDENCE', { documentKey: documentKey.value, lineKey: lineKey.value, eventOrdinal: ordinal.value, shots: Number(shots.value), occurredAt: utc(occurred.value), evidence: evidenceText.value, previous: context.value.evidence.find(f => f.reference.id === previousId.value)?.reference ?? null }) } catch (e: any) { error.value = e.message } }
async function register() { const f = context.value?.evidence.find(f => f.reference.id === evidenceId.value); if (!f || !registerReason.value.trim()) return; await write('OCCURRENCE', { sourceEvidenceSelectionRef: f.reference, reportedShots: f.fact.body.shots, occurredAt: f.fact.body.occurredAt, reason: registerReason.value }) }
async function observe() { const f = context.value?.occurrences.find(f => f.reference.id === occurrenceId.value); if (!f || !observationReason.value.trim()) return; await write('OBSERVATION', { occurrenceSelectionRef: f.reference, shots: f.fact.body.shots, occurredAt: f.fact.body.occurredAt, reason: observationReason.value }) }
async function publish() { if (!context.value || !policyReason.value.trim()) return; try { await write('POLICY', { state: policyState.value, site: context.value.site, allowLocalAttestation: allowLocal.value, effectiveFrom: utc(policyFrom.value), effectiveTo: utc(policyTo.value), reason: policyReason.value }) } catch (e: any) { error.value = e.message } }
async function recognize() { const c = context.value, o = observation.value; if (!c || !o || !recognitionReason.value.trim()) return; const evidence = (application.value?.evidence ?? c.evidence).find(f => f.reference.id === recognitionEvidenceId.value); await write('RECOGNITION', { expectedObservationDigest: o.original.reference.digest, outcome: recognitionOutcome.value, recognizedCycleRef: (application.value?.cycles ?? c.cycles).find(c => c.reference.id === cycleId.value)?.reference ?? null, evidenceRefs: evidence ? [evidence.reference] : [], reason: recognitionReason.value }, o.original.reference.id) }
async function apply() { const o = observation.value, app = application.value, d = o?.decisions.find(f => f.reference.id === decisionId.value); if (!o || !app || !d) return; await write('APPLICATION', { expectedObservationApplicationRevision: app.revision, applicationContextRef: app.contextRef, decisionRef: d.reference }, o.original.reference.id) }
async function recover(close: boolean) { const scope = props.scope; if (!scope) return; await run(async check => { const value = await resolveActualUsage(scope, close, check); check(); completed.value = value.state === 'COMMITTED'; notice.value = value.state === 'UNKNOWN' ? '原结果仍未知，请继续查询。' : value.state === 'CLOSED' ? '未提交操作已结束，输入保留。' : '原实际使用结果已恢复，请重新读取。' }) }
function changed() { generation++; clear(); busy.value = false; error.value = ''; if (props.scope) pending.value = pendingActualUsage(props.scope) }
watch(() => [props.scope?.tenantId, props.scope?.actorId, props.selected?.reference.id, props.selected?.reference.version], changed, { immediate: true })
onMounted(() => { window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed) })
onBeforeUnmount(() => { live = false; generation++; clear(); window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
</script>
<style scoped>
.actual-usage{margin-top:20px;padding:22px;border:1px solid #d7e3e7;border-radius:12px;background:var(--surface,#fff)}label{display:block;margin:12px 0}input,select,textarea,button{font:inherit;color:inherit;padding:8px;border:1px solid #bdcdd3;border-radius:6px;background:#fff}textarea{display:block;width:min(100%,650px);min-height:65px}button{margin:5px;cursor:pointer}button:disabled,fieldset:disabled{opacity:.65}fieldset{border:0;padding:0}article,form{border-top:1px solid #d7e3e7;padding-top:15px}p{overflow-wrap:anywhere}[role=alert]{color:#a02828}input:focus,textarea:focus,select:focus,button:focus-visible{outline:2px solid #14969e;outline-offset:2px}
</style>
